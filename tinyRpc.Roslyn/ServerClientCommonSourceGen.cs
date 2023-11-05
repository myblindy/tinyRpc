using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace TinyRpc.Roslyn;

public class SCMethodType(string name, ITypeSymbol? returnType, IEnumerable<(string Name, ITypeSymbol Type)>? parameters)
{
    public string Name { get; } = name;
    public ITypeSymbol? ReturnType { get; } = returnType;
    public ImmutableArray<(string Name, ITypeSymbol Type)> Parameters { get; } = parameters?.ToImmutableArray() ?? new();
}

public class SCEventType(string name, IEnumerable<(string Name, ITypeSymbol Type)>? parameters)
{
    public string Name { get; } = name;
    public ImmutableArray<(string Name, ITypeSymbol Type)> Parameters { get; } = parameters?.ToImmutableArray() ?? new();
}

public class SCType(string? @namespace, string name, IEnumerable<SCMethodType> methods, IEnumerable<SCEventType> events, INamedTypeSymbol serverSymbol)
{
    public string? Namespace { get; } = @namespace;
    public string Name { get; } = name;
    public ImmutableArray<SCMethodType> Methods { get; } =
        methods.OrderBy(m => m.Name).ThenBy(m => m.Parameters.Length).ToImmutableArray();
    public ImmutableArray<SCEventType> Events { get; } =
        events.OrderBy(e => e.Name).ThenBy(e => e.Parameters.Length).ToImmutableArray();
    public INamedTypeSymbol ServerSymbol { get; } = serverSymbol;
}

public static class Utils
{
    public static SCType? GetSyntaxClassDeclarations(this TypeDeclarationSyntax tds,
        IEnumerable<AttributeData> attributes, SemanticModel semanticModel)
    {
        if (attributes.First().ConstructorArguments.FirstOrDefault() is { } serverType
            && serverType.Type?.ToString() is "System.Type"
            && serverType.Value is INamedTypeSymbol serverNamedTypeSymbol
            && semanticModel.GetDeclaredSymbol(tds) is { } classSymbol)
        {
            var events = serverNamedTypeSymbol.GetMembers().OfType<IEventSymbol>().ToList();

            return new SCType(classSymbol.ContainingNamespace.IsGlobalNamespace ? null : classSymbol.ContainingNamespace.ToDisplayString(),
                tds.Identifier.Text,
                serverNamedTypeSymbol.GetMembers().OfType<IMethodSymbol>()
                    .Where(s => s.Name.FirstOrDefault() is not '.' && !events.Any(e => s.Name == $"add_{e.Name}" || s.Name == $"remove_{e.Name}"))
                    .Select(s => new SCMethodType(s.Name, s.ReturnsVoid ? null : s.ReturnType, s.Parameters.Select(p => (p.Name, p.Type)))),
                events.Select(e => new SCEventType(e.Name,
                    (e.Type as INamedTypeSymbol)?.DelegateInvokeMethod?.Parameters.Select(ep => (ep.Name, ep.Type)))),
                serverNamedTypeSymbol);
        }

        return null;
    }

    public static IncrementalValueProvider<ImmutableArray<SCType>> GetClassDeclarations(this SyntaxValueProvider syntaxValueProvider, string attributeName) => syntaxValueProvider
        .ForAttributeWithMetadataName(
                attributeName,
                predicate: static (n, _) => n.IsKind(SyntaxKind.ClassDeclaration) && ((ClassDeclarationSyntax)n).AttributeLists.Count > 0,
                transform: static (ctx, ct) =>
                {
                    var tds = (TypeDeclarationSyntax)ctx.TargetNode;
                    return tds.GetSyntaxClassDeclarations(ctx.Attributes, ctx.SemanticModel);
                })
            .Where(x => x is not null)
            .Collect()!;

    static readonly SymbolDisplayFormat fullyQualifiedSymbolDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    public static string ToFullyQualifiedString(this ITypeSymbol type) => type.ToDisplayString(fullyQualifiedSymbolDisplayFormat);

    public static string GetBinaryReaderCall(this ITypeSymbol type) => type.ToFullyQualifiedString() switch
    {
        _ when type.IsTupleType => $"({string.Join(", ", ((INamedTypeSymbol)type).TupleElements.Select(p =>
            p.Type.GetBinaryReaderCall()))})",
        "global::System.Byte[]" => "await reader.ReadBytesAsync(await reader.ReadInt32Async().ConfigureAwait(false)).ConfigureAwait(false)",
        _ when type is IArrayTypeSymbol arrayTypeSymbol =>
            $"await reader.ReadArray(async reader => {arrayTypeSymbol.ElementType.GetBinaryReaderCall()})",
        _ when type is INamedTypeSymbol { EnumUnderlyingType: not null } enumNamedTypeSymbol =>
            $"({type.ToFullyQualifiedString()})({enumNamedTypeSymbol.EnumUnderlyingType.GetBinaryReaderCall()})",
        "global::System.String" => "await reader.ReadStringAsync().ConfigureAwait(false)",
        "global::System.Boolean" => "await reader.ReadBooleanAsync().ConfigureAwait(false)",
        "global::System.Byte" => "await reader.ReadByteAsync().ConfigureAwait(false)",
        "global::System.SByte" => "await reader.ReadSByteAsync().ConfigureAwait(false)",
        "global::System.Int16" => "await reader.ReadInt16Async().ConfigureAwait(false)",
        "global::System.UInt16" => "await reader.ReadUInt16Async().ConfigureAwait(false)",
        "global::System.Int32" => "await reader.ReadInt32Async().ConfigureAwait(false)",
        "global::System.UInt32" => "await reader.ReadUInt32Async().ConfigureAwait(false)",
        "global::System.Int64" => "await reader.ReadInt64Async().ConfigureAwait(false)",
        "global::System.UInt64" => "await reader.ReadUInt64Async().ConfigureAwait(false)",
        "global::System.Double" => "await reader.ReadDoubleAsync().ConfigureAwait(false)",
        "global::System.DateTime" => "new System.DateTime(await reader.ReadInt64Async().ConfigureAwait(false))",
        _ => $$"""
            new {{type.ToFullyQualifiedString()}} 
            { 
                {{string.Join(", ", type.GetMembers()
                    .Where(m => m.Kind is SymbolKind.Field or SymbolKind.Property && m.DeclaredAccessibility is Accessibility.Public)
                    .Select(m => $"{m.Name} = {((m as IFieldSymbol)?.Type ?? ((IPropertySymbol)m).Type).GetBinaryReaderCall()}"))}}
            } 
            """
    };

    public static string GetBinaryWriterCall(this ITypeSymbol type, string name) => type.ToFullyQualifiedString() switch
    {
        _ when type.IsTupleType =>
            string.Join("\n", ((INamedTypeSymbol)type).TupleElements.Select(p => p.Type.GetBinaryWriterCall($"{name}.{p.Name}"))),
        _ when type is INamedTypeSymbol { EnumUnderlyingType: not null } enumNamedTypeSymbol =>
            $"await writer.WriteAsync(({enumNamedTypeSymbol.EnumUnderlyingType.ToFullyQualifiedString()}){name}).ConfigureAwait(false);",
        "global::System.Byte[]" => $$"""
            await writer.WriteAsync({{name}}.Length).ConfigureAwait(false);
            await writer.WriteAsync({{name}}).ConfigureAwait(false);
            """,
        _ when type is IArrayTypeSymbol arrayTypeSymbol => $$"""
            await writer.WriteAsync({{name}}.Length).ConfigureAwait(false);
            foreach(var _element{{SymbolEqualityComparer.Default.GetHashCode(type):X}} in {{name}})
            {
                {{arrayTypeSymbol.ElementType.GetBinaryWriterCall($"_element{SymbolEqualityComparer.Default.GetHashCode(type):X}")}}
            }
            """,
        "global::System.DateTime" => $"await writer.WriteAsync({name}.Ticks).ConfigureAwait(false);",
        "global::System.String" or "global::System.Boolean" or "global::System.Byte" or "global::System.SByte"
                or "global::System.Int16" or "global::System.UInt16" or "global::System.Int32" or "global::System.UInt32"
                or "global::System.Int64" or "global::System.UInt64" or "global::System.Double" or "global::System.DateTime" =>
            $"await writer.WriteAsync({name}).ConfigureAwait(false);",
        _ => $$"""
            {{string.Join("\n", type.GetMembers()
                .Where(m => m.Kind is SymbolKind.Field or SymbolKind.Property && m.DeclaredAccessibility is Accessibility.Public)
                .Select(m => ((m as IFieldSymbol)?.Type ?? ((IPropertySymbol)m).Type).GetBinaryWriterCall($"{name}.{m.Name}")))}}
            """
    };
}
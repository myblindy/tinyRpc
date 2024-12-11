using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Xml.Linq;

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
        _ when type is INamedTypeSymbol { EnumUnderlyingType: not null } || type is IArrayTypeSymbol || type.IsTupleType 
                || type.OriginalDefinition is INamedTypeSymbol nullableNamedTypeSymbol && nullableNamedTypeSymbol.ToFullyQualifiedString() is "global::System.Nullable" =>
            $"await SegmentedMessagePackDeserializer.DeserializeAsync<{type.ToFullyQualifiedString()}>(stream!).ConfigureAwait(false)",
        "global::System.String" or "global::System.Boolean" or "global::System.Byte" or "global::System.SByte" or "global::System.Int16"
                or "global::System.UInt16" or "global::System.Int32" or "global::System.UInt32" or "global::System.Int64" or "global::System.UInt64"
                or "global::System.Double" or "global::System.Single" or "global::System.DateTime" =>
            $"await SegmentedMessagePackDeserializer.DeserializeAsync<{type.ToFullyQualifiedString()}>(stream!).ConfigureAwait(false)",
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
        _ when type.OriginalDefinition is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.ToFullyQualifiedString() is "global::System.Nullable" =>
            $"await MessagePackSerializer.SerializeAsync(stream!, {name}).ConfigureAwait(false);",
        _ when type.IsTupleType || type is INamedTypeSymbol { EnumUnderlyingType: not null } || type is IArrayTypeSymbol =>
            $"await MessagePackSerializer.SerializeAsync(stream!, {name}).ConfigureAwait(false);",
        "global::System.DateTime" or "global::System.String" or "global::System.Boolean" or "global::System.Byte" or "global::System.SByte"
                or "global::System.Int16" or "global::System.UInt16" or "global::System.Int32" or "global::System.UInt32"
                or "global::System.Int64" or "global::System.UInt64" or "global::System.Double" or "global::System.Single" or "global::System.DateTime" =>
            $"await MessagePackSerializer.SerializeAsync(stream!, {name}).ConfigureAwait(false);",
        _ => $$"""
            {{string.Join("\n", type.GetMembers()
                .Where(m => m.Kind is SymbolKind.Field or SymbolKind.Property && m.DeclaredAccessibility is Accessibility.Public)
                .Select(m => ((m as IFieldSymbol)?.Type ?? ((IPropertySymbol)m).Type).GetBinaryWriterCall($"{name}.{m.Name}")))}}
            """
    };
}
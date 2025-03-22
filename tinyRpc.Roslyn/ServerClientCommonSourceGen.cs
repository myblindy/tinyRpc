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
}
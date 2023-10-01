using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace TinyRpc.Roslyn;

class SCMethodType
{
    public SCMethodType(string name, ITypeSymbol returnType, IEnumerable<(string Name, ITypeSymbol Type)> parameters) =>
        (Name, Parameters, ReturnType) = (name, parameters.ToImmutableArray(), returnType);

    public string Name { get; }
    public ITypeSymbol ReturnType { get; }
    public ImmutableArray<(string Name, ITypeSymbol Type)> Parameters { get; }
}

class SCType
{
    public SCType(string? @namespace, string name, IEnumerable<SCMethodType> methods, INamedTypeSymbol serverSymbol)
    {
        Namespace = @namespace;
        Name = name;
        Methods = methods.ToImmutableArray();
        ServerSymbol = serverSymbol;
    }

    public string? Namespace { get; }
    public string Name { get; }
    public ImmutableArray<SCMethodType> Methods { get; }
    public INamedTypeSymbol ServerSymbol { get; }
}

static class Utils
{
    internal static IncrementalValueProvider<ImmutableArray<SCType>> GetClassDeclarations(this SyntaxValueProvider syntaxValueProvider, string attributeName) => syntaxValueProvider
        .ForAttributeWithMetadataName(
                attributeName,
                predicate: static (n, _) => n.IsKind(SyntaxKind.ClassDeclaration) && ((ClassDeclarationSyntax)n).AttributeLists.Count > 0,
                transform: static (ctx, ct) =>
                {
                    var cds = (ClassDeclarationSyntax)ctx.TargetNode;

                    if (ctx.Attributes[0].ConstructorArguments.FirstOrDefault() is { } serverType
                        && serverType.Type?.ToString() is "System.Type"
                        && serverType.Value is INamedTypeSymbol serverNamedTypeSymbol
                        && ctx.SemanticModel.GetDeclaredSymbol(cds) is { } classSymbol)
                    {
                        return new SCType(classSymbol.ContainingNamespace.IsGlobalNamespace ? null : classSymbol.ContainingNamespace.ToDisplayString(),
                            cds.Identifier.Text,
                            serverNamedTypeSymbol.GetMembers().OfType<IMethodSymbol>()
                                .Where(s => s.Name.FirstOrDefault() is not '.')
                                .Select(s => new SCMethodType(s.Name, s.ReturnType, s.Parameters.Select(p => (p.Name, p.Type)))),
                            serverNamedTypeSymbol);
                    }

                    return null;
                })
            .Where(x => x is not null)
            .Collect()!;

    static readonly SymbolDisplayFormat fullyQualifiedSymbolDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    internal static string ToFullyQualifiedString(this ITypeSymbol type) => type.ToDisplayString(fullyQualifiedSymbolDisplayFormat);

    internal static string GetBinaryReaderFunctionName(this ITypeSymbol type) => type.ToFullyQualifiedString() switch
    {
        "global::System.String" => "ReadString",
        "global::System.Int32" => "ReadInt32",
        _ => throw new NotImplementedException($"Could not deduce binary reader function name for {type.Name}")
    };

    internal static bool IsVoid(this ITypeSymbol type) =>
        type.ToFullyQualifiedString() is "global::System.Void";
}
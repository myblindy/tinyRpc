using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TinyRpc.Roslyn;

namespace tinyRpc.Roslyn;
class TypeCache
{
    readonly Dictionary<ITypeSymbol, Guid> registeredTypes = new(SymbolEqualityComparer.Default);

    Guid GetTypeGuid(ITypeSymbol type, HashSet<ITypeSymbol>? openSymbolQueue = null)
    {
        if (!registeredTypes.ContainsKey(type))
        {
            openSymbolQueue?.Add(type);
            return registeredTypes[type] = Guid.NewGuid();
        }
        else
            return registeredTypes[type];
    }

    public string GetBinaryReaderCall(ITypeSymbol type)
    {
        var guid = GetTypeGuid(type);
        return $"await Read{guid:N}(stream!).ConfigureAwait(false)";
    }

    public string? GetBinaryWriterCall(ITypeSymbol? type, string name)
    {
        if (type is null) return null;

        var guid = GetTypeGuid(type);
        return $"await Write{guid:N}(stream!, {name}).ConfigureAwait(false);";
    }

    static bool IsEnum(ITypeSymbol type) => type.TypeKind == TypeKind.Enum;
    static bool IsBasic(ITypeSymbol type) => type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean
        or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
        or SpecialType.System_Decimal or SpecialType.System_Char
        or SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_DateTime;
    static bool IsValueTuple(ITypeSymbol type) => type.IsTupleType;
    static bool IsNullable(ITypeSymbol type, out ITypeSymbol? nullableArgumentType)
    {
        if (type is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType && namedTypeSymbol.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T)
        {
            nullableArgumentType = namedTypeSymbol.TypeArguments[0];
            return true;
        }
        else
        {
            nullableArgumentType = null;
            return false;
        }
    }

    public string GetSupportCode()
    {
        StringBuilder sb = new();
        HashSet<ITypeSymbol> queue = new(registeredTypes.Keys, SymbolEqualityComparer.Default);

        while (queue.Count > 0)
        {
            var type = queue.First();
            queue.Remove(type);
            var guid = GetTypeGuid(type);

            sb.AppendLine($$"""
                /// <summary> Writes a <see cref="{{type.ToFullyQualifiedString()}}"/> to the stream. </summary>
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                static async ValueTask Write{{guid:N}}(System.IO.Stream stream, {{type.ToFullyQualifiedString()}} value)
                {
                    {{(type.ToFullyQualifiedString() switch
            {
                // basic type, array of basic type, enum, array of enum, nullable of basic type, nullable of enum
                { } typeString when IsBasic(type)
                        || (type is IArrayTypeSymbol arrayTypeSymbol && IsBasic(arrayTypeSymbol.ElementType))
                        || IsEnum(type)
                        || (type is IArrayTypeSymbol arrayTypeSymbol2 && IsEnum(arrayTypeSymbol2.ElementType))
                        || (IsNullable(type, out var nullableArgumentType) && (IsBasic(nullableArgumentType!) || IsEnum(nullableArgumentType!))) =>
                    $"await MessagePackSerializer.SerializeAsync(stream!, value).ConfigureAwait(false);",
                // tuple
                _ when IsValueTuple(type) => $$"""
                    {{string.Join("\n", type.GetMembers().OfType<IFieldSymbol>()
                        .Where(m => Regex.IsMatch(m.Name, @"^Item\d+$"))
                        .Select((m, i) => $"await Write{GetTypeGuid(m.Type, queue):N}(stream!, value.{m.Name}).ConfigureAwait(false);"))}}
                    """,
                // array
                { } typeString when type is IArrayTypeSymbol arrayTypeSymbol => $$"""
                    await MessagePackSerializer.SerializeAsync(stream!, value.Length).ConfigureAwait(false);
                    foreach (var item in value)
                        await Write{{GetTypeGuid(arrayTypeSymbol.ElementType, queue):N}}(stream, item).ConfigureAwait(false);
                    """,
                // nullable
                _ when IsNullable(type, out var nullableArgumentType) => $$"""
                    if (value.HasValue)
                    {
                        await MessagePackSerializer.SerializeAsync(stream!, true).ConfigureAwait(false);
                        await Write{{GetTypeGuid(nullableArgumentType!, queue):N}}(stream!, value.Value).ConfigureAwait(false);
                    }
                    else
                        await MessagePackSerializer.SerializeAsync(stream!, false).ConfigureAwait(false);
                    """,
                _ => $$"""
                    {{string.Join("\n", type.GetMembers()
                        .Where(m => m.Kind is SymbolKind.Field or SymbolKind.Property && m.DeclaredAccessibility is Accessibility.Public)
                        .Select(m => $"await Write{GetTypeGuid((m as IFieldSymbol)?.Type ?? ((IPropertySymbol)m).Type, queue):N}(stream!, value.{m.Name}).ConfigureAwait(false);"))}}
                    """
            })}}
                }

                /// <summary> Reads a <see cref="{{type.ToFullyQualifiedString()}}"/> from the stream. </summary>
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                static async ValueTask<{{type.ToFullyQualifiedString()}}> Read{{guid:N}}(System.IO.Stream stream)
                {
                    {{(type.ToFullyQualifiedString() switch
            {
                // basic type, array of basic type, enum, array of enum, nullable of basic type, nullable of enum
                { } typeString when IsBasic(type)
                        || (type is IArrayTypeSymbol arrayTypeSymbol && IsBasic(arrayTypeSymbol.ElementType))
                        || IsEnum(type)
                        || (type is IArrayTypeSymbol arrayTypeSymbol2 && IsEnum(arrayTypeSymbol2.ElementType))
                        || (IsNullable(type, out var nullableArgumentType) && (IsBasic(nullableArgumentType!) || IsEnum(nullableArgumentType!))) =>
                    $"return await SegmentedMessagePackDeserializer.DeserializeAsync<{typeString}>(stream).ConfigureAwait(false);",
                // tuple
                _ when IsValueTuple(type) => $$"""
                    return (
                        {{string.Join(", ", type.GetMembers().OfType<IFieldSymbol>()
                            .Where(m => Regex.IsMatch(m.Name, @"^Item\d+$"))
                            .Select((m, i) => $"await Read{GetTypeGuid(m.Type, queue):N}(stream).ConfigureAwait(false)"))}}
                    );
                    """,
                // array
                { } typeString when type is IArrayTypeSymbol arrayTypeSymbol => $$"""
                    var arr = new {{arrayTypeSymbol.ElementType.ToFullyQualifiedString()}}[await SegmentedMessagePackDeserializer.DeserializeAsync<int>(stream!).ConfigureAwait(false)];
                    for (var i = 0; i < arr.Length; i++)
                        arr[i] = await Read{{GetTypeGuid(arrayTypeSymbol.ElementType, queue):N}}(stream!).ConfigureAwait(false);
                    return arr;
                    """,
                // nullable
                _ when IsNullable(type, out var nullableArgumentType) => $$"""
                    if (await SegmentedMessagePackDeserializer.DeserializeAsync<bool>(stream!).ConfigureAwait(false))
                        return await Read{{GetTypeGuid(nullableArgumentType!, queue):N}}(stream!).ConfigureAwait(false);
                    else
                        return null;
                    """,
                _ => $$"""
                    return new {{type.ToFullyQualifiedString()}} 
                    {
                        {{string.Join(", ", type.GetMembers()
                            .Where(m => m.Kind is SymbolKind.Field or SymbolKind.Property && m.DeclaredAccessibility is Accessibility.Public)
                            .Select(m => $"{m.Name} = await Read{GetTypeGuid((m as IFieldSymbol)?.Type ?? ((IPropertySymbol)m).Type, queue):N}(stream!).ConfigureAwait(false)"))}}
                    };
                    """
            })}}
                }
                """);
        }

        return sb.ToString();
    }
}
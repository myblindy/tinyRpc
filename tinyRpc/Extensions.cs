using Overby.Extensions.AsyncBinaryReaderWriter;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace TinyRpc;

public static class TinyRpcExtensions
{
    public static async Task<T[]> ReadArray<T>(this AsyncBinaryReader reader, Func<AsyncBinaryReader, Task<T>> elementReader)
    {
        var result = new T[await reader.ReadInt32Async()];
        for (int i = 0; i < result.Length; ++i)
            result[i] = await elementReader(reader);
        return result;
    }
}

using Microsoft.Extensions.ObjectPool;

namespace Overby.Extensions.AsyncBinaryReaderWriter;

public static class StreamExtensions
{
    class FuncPooledObjectPolicy<T> : IPooledObjectPolicy<T> where T : notnull
    {
        readonly Func<T> generator;

        public FuncPooledObjectPolicy(Func<T> generator) => this.generator = generator;

        public T Create() => generator();

        public bool Return(T obj) => true;
    }

    static readonly DefaultObjectPool<byte[]> byteArrayPool = new(new FuncPooledObjectPolicy<byte[]>(() => new byte[1]));

    public static async Task<int> ReadByteAsync(this Stream stream, CancellationToken cancellationToken)
    {
        byte[]? buffer = default;
        try
        {
            buffer = byteArrayPool.Get();

            var read = await stream.ReadAsync(buffer, 0, 1, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return -1;

            return buffer[0];
        }
        finally { if (buffer is not null) byteArrayPool.Return(buffer); }
    }

    public static Task WriteByteAsync(this Stream stream, byte value, CancellationToken cancellationToken)
    {
        byte[]? buffer = default;

        try
        {
            buffer = byteArrayPool.Get();
            buffer[0] = value;
            return stream.WriteAsync(buffer, 0, 1, cancellationToken);
        }
        finally { if (buffer is not null) byteArrayPool.Return(buffer); }
    }
}
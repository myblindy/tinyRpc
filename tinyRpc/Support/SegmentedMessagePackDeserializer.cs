using MessagePack;
using System.Buffers;

namespace TinyRpc.Support;

public static class SegmentedMessagePackDeserializer
{
    static readonly Dictionary<Stream, DeserializerHelper> deserializerHelpers = [];

    public static async ValueTask<T> DeserializeAsync<T>(Stream stream)
    {
        if (!deserializerHelpers.TryGetValue(stream, out var helper))
        {
            helper = new DeserializerHelper(stream);
            deserializerHelpers.Add(stream, helper);
        }
        return await helper.DeserializeAsync<T>().ConfigureAwait(false);
    }

    class DeserializerHelper
    {
        readonly IAsyncEnumerator<ReadOnlySequence<byte>> enumerator;

        public DeserializerHelper(Stream stream)
        {
            async IAsyncEnumerable<ReadOnlySequence<byte>> EnumerateMessagePackSegment()
            {
                using var reader = new MessagePackStreamReader(stream);
                while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false) is { } segment)
                    yield return segment;
            }
            enumerator = EnumerateMessagePackSegment().GetAsyncEnumerator();
        }

        public async ValueTask<T> DeserializeAsync<T>()
        {
            await enumerator.MoveNextAsync().ConfigureAwait(false);
            var segment = enumerator.Current;
            return MessagePackSerializer.Deserialize<T>(segment);
        }
    }
}


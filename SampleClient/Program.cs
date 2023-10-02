using SampleShared;
using System.Text;
using TinyRpc;

[TinyRpcClientClass(typeof(IServer))]
partial class MyRpcClient { }

static class Program
{
    static async Task Main()
    {
        using var client = new MyRpcClient(@"../../../../SampleServer/bin/Debug/net7.0/SampleServer.exe", CancellationToken.None);
        await Task.WhenAll(
            client.HiAsync(),
            client.HiAsync(),
            client.FancyHiAsync("Moopsies", 25));

        Console.WriteLine($"[CLIENT] 5 + 2 = {await client.AddAsync(5, 2)}");
        Console.WriteLine($"[CLIENT] {Encoding.UTF8.GetString(
            await client.BufferCallAsync(Encoding.UTF8.GetBytes("arf arf"), 10))}");

        var (a, b, c, utf8) = await client.GetValueTupleResultAsync("120 150 1000 plain ol string");
        Console.WriteLine($"[CLIENT] GetValueTupleResultAsync: a={a} b={b} c={c} utf8={Encoding.UTF8.GetString(utf8)}");
    }
}

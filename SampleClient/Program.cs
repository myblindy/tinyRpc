using SampleShared;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TinyRpc;

[TinyRpcClientClass(typeof(IServer))]
partial class MyRpcClient { }

static class Program
{
    static async Task Main()
    {
        using var client = new MyRpcClient(@"../../../../SampleServer/bin/Debug/net7.0/SampleServer.exe", CancellationToken.None);
        //using var client = new MyRpcClient(@"../../../../x64/Debug/CppTest.exe", CancellationToken.None);
        client.OnData += (d, s) => Console.WriteLine($"[SERVER] OnData: {d} {s}");

        await Task.WhenAll(
            client.HiAsync(),
            client.HiAsync(),
            client.FancyHiAsync("Moopsies", 25));

        Console.WriteLine($"[CLIENT] 5 + 2 = {await client.AddAsync(5, 2)}");
        Console.WriteLine($"[CLIENT] {Encoding.UTF8.GetString(
            await client.BufferCallAsync(Encoding.UTF8.GetBytes("arf arf"), 10))}");

        await Task.Delay(TimeSpan.FromSeconds(2));

        var (a, b, c, utf8) = await client.GetValueTupleResultAsync("120 150 1000 plain ol string");
        Console.WriteLine($"[CLIENT] GetValueTupleResultAsync: a={a} b={b} c={c} utf8={Encoding.UTF8.GetString(utf8)}");

        foreach (var entry in await client.GetValueTupleArrayResultAsync())
            Console.WriteLine($"[CLIENT] GetValueTupleArrayResultAsync: a={entry.a} b={entry.b} dt={entry.dt} d={entry.d}");

        while (true)
            await Task.Delay(100).ConfigureAwait(false);
    }
}

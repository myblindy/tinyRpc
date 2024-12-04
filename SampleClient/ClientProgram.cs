using SampleShared;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TinyRpc;

[TinyRpcClientClass(typeof(IServer))]
partial class MyRpcClient { }

static class ClientProgram
{
    static async Task Main()
    {
        if (await MyRpcClient.CreateLocalAsync(
            @"../../../../SampleServer/bin/Debug/net9.0/SampleServer.exe", CancellationToken.None) is not { } client)
        {
            return;
        }
        //using var client = new MyRpcClient(@"../../../../x64/Debug/CppTest.exe", CancellationToken.None);

        using (client)
        {
            client.OnData += (d, s) => Console.WriteLine($"[SERVER] OnData: {d} {s}");

            await Task.WhenAll(
                client.HiAsync(),
                client.HiAsync(),
                client.FancyHiAsync("Moopsies", 25));

            Console.WriteLine($"[CLIENT] 5 + 2 = {await client.AddAsync(5, 2)}");
            Console.WriteLine($"[CLIENT] {Encoding.UTF8.GetString(
                await client.BufferCallAsync(Encoding.UTF8.GetBytes("arf arf"), 10))}");

            await Task.Delay(TimeSpan.FromSeconds(2));

            var s2 = await client.GetStructAsync(12, new() { a = 15, b = "b", S11 = new() { a = 49859485 } }, 3.1415);
            Console.WriteLine($"[CLIENT] GetStructAsync: c={s2.c} d={s2.d} S22.a={s2.S22.a}");

            var (a, b, c, utf8) = await client.GetValueTupleResultAsync("120 150 1000 plain ol string");
            Console.WriteLine($"[CLIENT] GetValueTupleResultAsync: a={a} b={b} c={c} utf8={Encoding.UTF8.GetString(utf8)}");

            var nullableRes = await client.GetNullableValueAsync(15.4f);
            Console.WriteLine($"[CLIENT] GetNullableValueAsync for 15.4f: {nullableRes}");

            foreach (var entry in await client.GetValueTupleArrayResultAsync())
                Console.WriteLine($"[CLIENT] GetValueTupleArrayResultAsync: a={entry.a} b={entry.b} dt={entry.dt} d={entry.d}");

            while (true)
                await Task.Delay(100).ConfigureAwait(false);
        }
    }
}

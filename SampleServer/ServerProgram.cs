using SampleShared;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TinyRpc;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
[TinyRpcServerClass(typeof(IServer))]
partial class MyRpcServer
{
    private partial async ValueTask<int> Add(int x, int y) => x + y;
    private partial async ValueTask<byte[]> BufferCall(byte[] baseUtf8String, int n) =>
        Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(baseUtf8String) + " x" + n);

    private partial async ValueTask FancyHi(string name, int age) =>
        Console.WriteLine($"Fancy hi, {age} years old {name}!");
    private partial async ValueTask<E> GetNewE(E input) => input is E.D ? E.A : input + 1;
    private partial async ValueTask<double?> GetNullableValue(float? val) => val + 50;
    private partial async ValueTask<S2> GetStruct(int a, S1 s, double b) =>
        new()
        {
            c = $"a={a} s.a={s.a} s.b={s.b} s.S11.a={s.S11.a} b={b}",
            d = 514546,
            S22 = new() { a = 123 }
        };

    private partial async ValueTask<(uint a, long b, DateTime dt, double d)[]> GetValueTupleArrayResult() =>
    [
        (1U, 15, DateTime.Now, 35.0),
        (uint.MaxValue, long.MaxValue, DateTime.MaxValue, double.MinValue),
        (uint.MinValue, long.MinValue, DateTime.MinValue, double.MaxValue)
    ];

    readonly byte[] largeArrayBuffer = new byte[1920 * 1080 * 4];
    private partial async ValueTask<byte[]> GetLargeArray()
    {
        Random.Shared.NextBytes(largeArrayBuffer);
        return largeArrayBuffer;
    }

    private partial async ValueTask<(int a, int b, short c, byte[] utf8)> GetValueTupleResult(string s) =>
        Regex.Match(s, @"^(\d+) (\d+) (\d+) (.*)$") is not { Success: true } m ? default
            : (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                short.Parse(m.Groups[3].Value), Encoding.UTF8.GetBytes(m.Groups[4].Value));

    private partial async ValueTask Hi() => Console.WriteLine("hi");
}

static class ServerProgram
{
    public static async Task Main(string[] args)
    {
        if (await MyRpcServer.CreateAsync(args, CancellationToken.None) is not { } rpcServer)
            return;

        using (rpcServer)
            while (rpcServer.Healthy)
            {
                try { await rpcServer.FireOnData(3.5, "marf - " + Random.Shared.Next()); } catch { }
                await Task.Delay(100).ConfigureAwait(false);
            }
    }
}
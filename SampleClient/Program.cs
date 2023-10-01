using SampleShared;
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
    }
}
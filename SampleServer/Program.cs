using SampleShared;
using TinyRpc;

class ServerHandler : IServer
{
    public int Add(int x, int y) => x + y;
    public void FancyHi(string name, int age) =>
        Console.WriteLine($"Fancy hi, {age} years old {name}!");
    public void Hi() => Console.WriteLine("hi");
}

[TinyRpcServerClass(typeof(ServerHandler))]
partial class MyRpcServer { }

static class Program
{
    public static async Task Main(string[] args)
    {
        using var rpcServer = new MyRpcServer(args, new(), CancellationToken.None);

        while (true)
            await Task.Delay(100);
    }
}
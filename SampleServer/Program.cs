﻿using SampleShared;
using System.Text;
using System.Text.RegularExpressions;
using TinyRpc;

class ServerHandler : IServer
{
    public int Add(int x, int y) => x + y;
    public byte[] BufferCall(byte[] baseUtf8String, int n) =>
        Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(baseUtf8String) + " x" + n);
    public void FancyHi(string name, int age) =>
        Console.WriteLine($"Fancy hi, {age} years old {name}!");
    public (int a, int b, short c, byte[] utf8) GetValueTupleResult(string s) =>
        Regex.Match(s, @"^(\d+) (\d+) (\d+) (.*)$") is not { Success: true } m ? default
            : (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                short.Parse(m.Groups[3].Value), Encoding.UTF8.GetBytes(m.Groups[4].Value));
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
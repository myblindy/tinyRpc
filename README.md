# MB.TinyRpc

`TinyRpc` is a small, fast 1:1 RPC client-server framework that uses source generation to communicate using binary data over zero-configuration sockets. 

It sends as little data as necessary, without run-time checks. If the common interface doesn't have events, for example, the machinery to support them isn't generated. Similarly, `void` returns or parameters don't send anything over the pipe. 

It also automatically starts the server when the client is instantiated and it automatically closes the server when the client shuts down (even if it doesn't shut down gracefully). There is no possibility of getting out-of-sync orphaned servers. 

## Support

Type | .NET | Windows C++
-|-|-
Server|✅|❌
Client|✅|✅

## Common interface definition

The source of any operation is a .Net interface that can contain methods or events. This interface doesn't have to be a single common type as long as the definition matches. For example, in [the sample provided](https://github.com/myblindy/tinyRpc/blob/master/SampleShared/IServer.cs) it's used in a shared project (thus creating two different types):

```
internal interface IServer
{
    void Hi();
    void FancyHi(string name, int age);
    int Add(int x, int y);
    byte[] BufferCall(byte[] baseUtf8String, int n);
    (int a, int b, short c, byte[] utf8) GetValueTupleResult(string s);
    (uint a, long b, DateTime dt, double d)[] GetValueTupleArrayResult();

    event Action<double, string> OnData;
}
```

## .NET Server

The pattern for creating a .NET server is as follows:

1. Reference [`MB.TinyRpc`](https://www.nuget.org/packages/MB.TinyRpc/).
2. Create a new partial class and decorate it with `[TinyRpcServerClass(typeof(IServer))]`, where  `IServer` is the common interface above.
3. Implement all the required partial types provided by the source generator.
4. Instantiate the server class above in code. This will create the listener on your thread pool and keep the `IsHealthy` property up to date.

You can find an example of this [here](https://github.com/myblindy/tinyRpc/blob/master/SampleServer/Program.cs).

## .NET Client

The pattern for creating a .NET client is as follows:

1. Reference [`MB.TinyRpc`](https://www.nuget.org/packages/MB.TinyRpc/).
2. Create a partial class and decorate it with `[TinyRpcClientClass(typeof(IServer))]`, where `IServer` is the common interface above.
3. Instantiate the class and call the provided `async` functions on it, or fire its events.

You can find an example of this [here](https://github.com/myblindy/tinyRpc/blob/master/SampleClient/Program.cs).

## C++ Client

1. Reference [`MB.TinyRpc.CppGen`](https://www.nuget.org/packages/MB.TinyRpc.CppGen/).
2. Add a pre-build event that calls the code generator on the .Net client project to extract information about the interface type and build the C++ header file:
```
"$(ProjectDir)TinyRpcCppGen\tinyRpc.CppGen.exe" --input-project-path "$(SolutionDir)SampleClient\SampleClient.csproj" --input-class-names "SampleShared.IServer" --output-path "$(ProjectDir)TinyRpcServer.h" --output-class-names "TinyRpcServer"
```
3. Include the generated header file and call its functions or fire its events. 

You can find an example of this [here](https://github.com/myblindy/tinyRpc/blob/master/CppTest/main.cpp).

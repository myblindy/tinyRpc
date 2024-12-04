using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace TinyRpc.Roslyn;

[Generator]
class ClientSourceGen : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider.GetClassDeclarations("TinyRpc.TinyRpcClientClassAttribute");

        context.RegisterSourceOutput(classDeclarations, static (ctx, clientTypes) =>
        {
            foreach (var clientType in clientTypes)
                if (clientType is not null)
                    ctx.AddSource($"tinyRpc.{clientType.Name}Client.g.cs", SourceText.From($$"""
                        #nullable enable

                        using TinyRpc;
                        using TinyRpc.Support;
                        using System;
                        using System.Diagnostics;
                        using System.IO;
                        using System.Net;
                        using System.Threading;
                        using System.Threading.Tasks;
                        
                        {{(clientType.Namespace is null ? null : $"namespace {clientType.Namespace};")}}

                        partial class {{clientType.Name}} : TinyRpc.TinyRpcClient
                        {
                            public static async Task<{{clientType.Name}}?> CreateLocalAsync(string serverExecutablePath, CancellationToken ct)
                            {
                                var rpcClient = new {{clientType.Name}}();
                                rpcClient.tcpListener.Start();
                                var localPort = ((IPEndPoint)rpcClient.tcpListener.LocalEndpoint).Port;

                                var serverProcess = Process.Start(new ProcessStartInfo(Path.GetFullPath(serverExecutablePath), 
                                    new[] { "localhost", localPort.ToString() })
                                {
                                    WorkingDirectory = Path.GetDirectoryName(serverExecutablePath) is not { } directoryName ? null
                                        : Path.GetFullPath(directoryName)
                                });
                                await rpcClient.ConnectAsync();

                                {{(clientType.Events.Length > 0 ? "_ = rpcClient.ReadLoopAsync(ct);" : null)}}

                                return rpcClient;
                            }

                            {{(clientType.Events.Length > 0 ? $$"""
                                async Task ReadLoopAsync(CancellationToken ct)
                                {
                                    while(true)
                                    {
                                        // read one byte to determine if it's an event or data
                                        var type = await reader!.ReadByteAsync().ConfigureAwait(false);

                                        if(type == 0)
                                        {
                                            // data
                                            returnReadReadyEvent.Set();
                                            await returnReadCompletedEvent.WaitAsync().ConfigureAwait(false);
                                        }
                                        else if(type == 1)
                                        {
                                            // event
                                            var eventIdx = await reader.ReadByteAsync().ConfigureAwait(false);
                                            {{string.Join("\n", clientType.Events.Select((e, eIdx) => $$"""
                                                if(eventIdx == {{eIdx}})        // {{e.Name}}
                                                {
                                                    {{string.Join("\n", e.Parameters.Select((p, pIdx) => $"var p{pIdx} = {p.Type.GetBinaryReaderCall()};"))}}
                                                    {{e.Name}}?.Invoke({{string.Join(", ", e.Parameters.Select((_, pIdx) => $"p{pIdx}"))}});
                                                }
                                                """))}}
                                        }
                                    }
                                }
                                """ : null)}}

                            {{string.Join("\n", clientType.Events.Select(e => $$"""
                                public delegate void {{e.Name}}Delegate({{string.Join(", ", e.Parameters.Select(p => $"{p.Type.ToFullyQualifiedString()} {p.Name}"))}});
                                public event {{e.Name}}Delegate? {{e.Name}};
                                """))}}

                            {{string.Join("\n", clientType.Methods.Select((m, mIdx) => $$"""
                                public async Task{{(m.ReturnType is null ? null : $"<{m.ReturnType.ToFullyQualifiedString()}>")}} {{m.Name}}Async({{string.Join(", ",
                                    m.Parameters.Select(p => $"{p.Type.ToFullyQualifiedString()} {p.Name}"))}}) 
                                {
                                    using (await callMonitor.EnterAsync().ConfigureAwait(false))
                                    {
                                        await writer!.WriteAsync((byte){{mIdx}}).ConfigureAwait(false); // {{m.Name}}
                                        {{string.Join("\n", m.Parameters.Select(p => p.Type.GetBinaryWriterCall(p.Name)))}}
                                        await writer.FlushAsync().ConfigureAwait(false);

                                        {{(m.ReturnType is null ? null : $$"""
                                            // return type
                                            {{(clientType.Events.Length > 0 ? "await returnReadReadyEvent.WaitAsync().ConfigureAwait(false);" : null)}}
                                            var __result = {{m.ReturnType.GetBinaryReaderCall()}};
                                            {{(clientType.Events.Length > 0 ? """
                                                returnReadReadyEvent.Reset();
                                                returnReadCompletedEvent.Set();
                                                """ : null)}}
                                            return __result;
                                            """)}}
                                    }
                                }
                                """))}}
                        }
                        """, Encoding.UTF8));
        });
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace TinyRpc.Roslyn;

[Generator]
class ServerSourceGen : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider.GetClassDeclarations("TinyRpc.TinyRpcServerClassAttribute");

        context.RegisterSourceOutput(classDeclarations, static (ctx, serverTypes) =>
        {
            foreach (var serverType in serverTypes)
                if (serverType is not null)
                    ctx.AddSource($"tinyRpc.{serverType.Name}Server.g.cs", SourceText.From($$"""
                        #nullable enable

                        using MessagePack;
                        using TinyRpc;
                        using TinyRpc.Support;
                        using System;
                        using System.Threading;
                        using System.Threading.Tasks;
                        
                        {{(serverType.Namespace is null ? null : $"namespace {serverType.Namespace};")}}

                        partial class {{serverType.Name}} : TinyRpc.TinyRpcServer
                        {
                            readonly {{serverType.ServerSymbol}} serverHandler;

                            {{serverType.Name}}({{serverType.ServerSymbol}} serverHandler)
                            {
                                this.serverHandler = serverHandler;
                            }

                            public static async Task<{{serverType.Name}}?> CreateAsync(string[] args, 
                                {{serverType.ServerSymbol}} serverHandler, CancellationToken ct)
                            {
                                var rpcServer = new {{serverType.Name}}(serverHandler);
                                await rpcServer.tcpClient.ConnectAsync(args[0], int.Parse(args[1])); 
                                rpcServer.stream = rpcServer.tcpClient.GetStream();

                                _ = rpcServer.MessageHandler(ct);

                                return rpcServer;
                            }

                            {{string.Join("\n", serverType.Events.Select((e, eIdx) => $$"""
                                public async Task Fire{{e.Name}}({{string.Join(", ", e.Parameters.Select(p => $"{p.Type.ToFullyQualifiedString()} {p.Name}"))}})
                                {
                                    using (await writeMonitor.EnterAsync().ConfigureAwait(false))
                                    {
                                        await MessagePackSerializer.SerializeAsync(stream!, (byte)1).ConfigureAwait(false);            // event data
                                        await MessagePackSerializer.SerializeAsync(stream!, (byte){{eIdx}}).ConfigureAwait(false);      // {{e.Name}}
                                        {{string.Join("\n", e.Parameters.Select(p => p.Type.GetBinaryWriterCall(p.Name)))}}
                                        await stream!.FlushAsync().ConfigureAwait(false);
                                    }
                                }
                                """))}}

                            protected override async Task MessageHandler(CancellationToken ct)
                            {
                                try
                                {
                                    while (!ct.IsCancellationRequested)
                                    {
                                        var mIdx = await SegmentedMessagePackDeserializer.DeserializeAsync<byte>(stream!).ConfigureAwait(false);

                                        {{string.Join("\n", serverType.Methods.Select((m, mIdx) => $$"""
                                            if (mIdx == {{mIdx}})     // {{m.Name}}
                                            {
                                                {{(m.ReturnType is null ? null : "var result = ")}}
                                                serverHandler.{{m.Name}} ({{string.Join(", ", m.Parameters.Select(p =>
                                                    p.Type.GetBinaryReaderCall()))}});

                                                {{(m.ReturnType is null ? null : $$"""
                                                    // return the result
                                                    {{(serverType.Events.Length > 0 ? "using (await writeMonitor.EnterAsync().ConfigureAwait(false))" : null)}}
                                                    {
                                                        {{(serverType.Events.Length > 0 ? "await MessagePackSerializer.SerializeAsync(stream!, (byte)0).ConfigureAwait(false);     // return data" : null)}}
                                                        {{m.ReturnType.GetBinaryWriterCall("result")}}
                                                        await stream!.FlushAsync().ConfigureAwait(false);
                                                    }
                                                    """)}}
                                            }
                                            """))}}
                                    }
                                }
                                catch(ArgumentException)
                                {
                                    // pipe broken, end the server
                                    Healthy = false;
                                }
                                catch(System.IO.EndOfStreamException)
                                {
                                    // pipe broken, end the server
                                    Healthy = false;
                                }
                            }
                        }
                        """, Encoding.UTF8));
        });
    }
}

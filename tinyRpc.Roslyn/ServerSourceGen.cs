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
                            public static async Task<{{serverType.Name}}?> CreateAsync(string[] args, CancellationToken ct)
                            {
                                var rpcServer = new {{serverType.Name}}();
                                await rpcServer.tcpClient.ConnectAsync(args[0], int.Parse(args[1])).ConfigureAwait(false); 
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
                                        var responseId = await SegmentedMessagePackDeserializer.DeserializeAsync<int>(stream!).ConfigureAwait(false);

                                        {{string.Join("\n", serverType.Methods.Select((m, mIdx) => $$"""
                                            if (mIdx == {{mIdx}})     // {{m.Name}}
                                            {
                                                {{string.Join("\n", m.Parameters.Select((p, pIdx) =>
                                                    $"var p{pIdx} = {p.Type.GetBinaryReaderCall()};"))}}

                                                async ValueTask asyncHelper()
                                                {
                                                    {{(m.ReturnType is null ? null : "var result = ")}}
                                                    await {{m.Name}}Async({{string.Join(", ", Enumerable.Range(0, m.Parameters.Length).Select(pIdx => $"p{pIdx}"))}})
                                                        .ConfigureAwait(false);

                                                    // return the result, if any
                                                    using (await writeMonitor.EnterAsync().ConfigureAwait(false))
                                                    {
                                                        await MessagePackSerializer.SerializeAsync(stream!, (byte)0).ConfigureAwait(false);     // return data
                                                        await MessagePackSerializer.SerializeAsync(stream!, responseId).ConfigureAwait(false);  // for response id
                                                        {{m.ReturnType?.GetBinaryWriterCall("result")}}
                                                        await stream!.FlushAsync().ConfigureAwait(false);
                                                    }
                                                }
                                                _ = asyncHelper();
                                            }
                                            """))}}
                                    }
                                }
                                catch(Exception ex) when (ex is 
                                    ObjectDisposedException or MessagePackSerializationException or System.IO.IOException)
                                {
                                    // pipe broken, end the server
                                    Healthy = false;
                                    FireHealthyChanged(false);
                                }
                            }

                            {{string.Join("\n", serverType.Methods.Select(m => $$"""
                                private partial ValueTask{{(m.ReturnType is null ? null : $"<{m.ReturnType.ToFullyQualifiedString()}>")}}
                                    {{m.Name}}Async({{string.Join(", ", m.Parameters.Select(p => $"{p.Type.ToFullyQualifiedString()} {p.Name}"))}});
                                """))}}
                        }
                        """, Encoding.UTF8));
        });
    }
}

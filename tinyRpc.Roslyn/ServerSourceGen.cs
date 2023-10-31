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
                        using TinyRpc;
                        using System;
                        using System.Threading;
                        using System.Threading.Tasks;
                        
                        {{(serverType.Namespace is null ? null : $"namespace {serverType.Namespace};")}}

                        partial class {{serverType.Name}} : TinyRpc.TinyRpcServer
                        {
                            readonly {{serverType.ServerSymbol}} serverHandler;

                            public {{serverType.Name}}(string[] args, {{serverType.ServerSymbol}} serverHandler, CancellationToken ct)
                                : base(args, ct)
                            {
                                this.serverHandler = serverHandler;
                            }

                            {{string.Join("\n", serverType.Events.Select((e, eIdx) => $$"""
                                public async Task Fire{{e.Name}}({{string.Join(", ", e.Parameters.Select(p => $"{p.Type.ToFullyQualifiedString()} {p.Name}"))}})
                                {
                                    await connectedEvent.WaitAsync().ConfigureAwait(false);
                                    using (await writeMonitor.EnterAsync().ConfigureAwait(false))
                                    {
                                        await writer.WriteAsync((byte)1).ConfigureAwait(false);             // event data
                                        await writer.WriteAsync((byte){{eIdx}}).ConfigureAwait(false);      // {{e.Name}}
                                        {{string.Join("\n", e.Parameters.Select(p => p.Type.GetBinaryWriterCall(p.Name)))}}
                                        await writer.FlushAsync().ConfigureAwait(false);
                                    }
                                }
                                """))}}

                            protected override async Task MessageHandler(CancellationToken ct)
                            {
                                try
                                {
                                    await pipe.ConnectAsync(ct).ConfigureAwait(false);
                                    connectedEvent.Set();

                                    while (!ct.IsCancellationRequested)
                                    {
                                        var mIdx = await reader.ReadByteAsync().ConfigureAwait(false);

                                        {{string.Join("\n", serverType.Methods.Select((m, mIdx) => $$"""
                                            if (mIdx == {{mIdx}})     // {{m.Name}}
                                            {
                                                {{(m.ReturnType.IsVoid() ? null : "var result = ")}}
                                                serverHandler.{{m.Name}} ({{string.Join(", ", m.Parameters.Select(p =>
                                                    p.Type.GetBinaryReaderCall()))}});

                                                {{(m.ReturnType.IsVoid() ? null : $$"""
                                                    // return the result
                                                    {{(serverType.Events.Length > 0 ? "using (await writeMonitor.EnterAsync().ConfigureAwait(false))" : null)}}
                                                    {
                                                        {{(serverType.Events.Length > 0 ? "await writer.WriteAsync((byte)0).ConfigureAwait(false);     // return data" : null)}}
                                                        {{m.ReturnType.GetBinaryWriterCall("result")}}
                                                        await writer.FlushAsync().ConfigureAwait(false);
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

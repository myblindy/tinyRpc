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
                        {{(serverType.Namespace is null ? null : $"namespace {serverType.Namespace};")}}

                        partial class {{serverType.Name}} : TinyRpc.TinyRpcServer
                        {
                            readonly {{serverType.ServerSymbol}} serverHandler;

                            public {{serverType.Name}}(string[] args, {{serverType.ServerSymbol}} serverHandler, CancellationToken ct)
                                : base(args, ct)
                            {
                                this.serverHandler = serverHandler;
                            }

                            protected override async Task MessageHandler(CancellationToken ct)
                            {
                                try
                                {
                                    await pipe.ConnectAsync(ct).ConfigureAwait(false);

                                    while (!ct.IsCancellationRequested)
                                    {
                                        var name = await reader.ReadStringAsync().ConfigureAwait(false);

                                        {{string.Join("\n", serverType.Methods.Select(m => $$"""
                                            if (name == "{{m.Name}}")
                                            {
                                                {{(m.ReturnType.IsVoid() ? null : "var result = ")}}
                                                serverHandler.{{m.Name}}({{string.Join(", ", m.Parameters.Select(p =>
                                                    p.Type.GetBinaryReaderCall()))}});

                                                {{(m.ReturnType.IsVoid() ? null : $$"""
                                                    // return the result
                                                    {{m.ReturnType.GetBinaryWriterCall("result")}}
                                                    await writer.FlushAsync().ConfigureAwait(false);
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
                            }
                        }
                        """, Encoding.UTF8));
        });
    }
}

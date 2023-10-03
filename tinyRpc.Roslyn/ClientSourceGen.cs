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
                        using TinyRpc;
                        using System;
                        using System.Threading;
                        using System.Threading.Tasks;
                        
                        {{(clientType.Namespace is null ? null : $"namespace {clientType.Namespace};")}}

                        partial class {{clientType.Name}} : TinyRpc.TinyRpcClient
                        {
                            public {{clientType.Name}}(string serverPath, CancellationToken ct)
                                : base(serverPath, ct)
                            {
                                OnConstructed(ct);
                            }

                            partial void OnConstructed(CancellationToken ct);

                            {{string.Join("\n", clientType.Methods.Select(m => $$"""
                                public async Task{{(m.ReturnType.IsVoid() ? null : $"<{m.ReturnType.ToFullyQualifiedString()}>")}} {{m.Name}}Async({{string.Join(", ",
                                    m.Parameters.Select(p => $"{p.Type.ToFullyQualifiedString()} {p.Name}"))}}) 
                                {
                                    await connectedEvent.WaitAsync().ConfigureAwait(false);
                                    using (await monitor.EnterAsync().ConfigureAwait(false))
                                    {
                                        await writer.WriteAsync("{{m.Name}}").ConfigureAwait(false);
                                        {{string.Join("\n", m.Parameters.Select(p => p.Type.GetBinaryWriterCall(p.Name)))}}
                                        await writer.FlushAsync().ConfigureAwait(false);

                                        {{(m.ReturnType.IsVoid() ? null : $$"""
                                            // return type
                                            return {{m.ReturnType.GetBinaryReaderCall()}};
                                            """)}}
                                    }
                                }
                                """))}}
                        }
                        """, Encoding.UTF8));
        });
    }
}

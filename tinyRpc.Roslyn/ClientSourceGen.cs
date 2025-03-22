using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using tinyRpc.Roslyn;

namespace TinyRpc.Roslyn;

[Generator]
class ClientSourceGen : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider.GetClassDeclarations("TinyRpc.TinyRpcClientClassAttribute");

        context.RegisterSourceOutput(classDeclarations, static (ctx, clientTypes) =>
        {
            TypeCache typeCache = new();

            foreach (var clientType in clientTypes)
                if (clientType is not null)
                    ctx.AddSource($"tinyRpc.{clientType.Name}Client.g.cs", SourceText.From($$"""
                        #nullable enable

                        using MessagePack;
                        using TinyRpc;
                        using TinyRpc.Support;
                        using System;
                        using System.Collections.Concurrent;
                        using System.Diagnostics;
                        using System.IO;
                        using System.Linq;
                        using System.Net;
                        using System.Net.NetworkInformation;
                        using System.Text.RegularExpressions;
                        using System.Threading;
                        using System.Threading.Tasks;
                        using Renci.SshNet;
                                     
                        {{(clientType.Namespace is null ? null : $"namespace {clientType.Namespace};")}}

                        partial class {{clientType.Name}} : TinyRpc.TinyRpcClient
                        {
                            static async Task<{{clientType.Name}}?> CreateAsync(Func<{{clientType.Name}}, int, Task<bool>> createClientAction, CancellationToken ct)
                            {
                                var rpcClient = new {{clientType.Name}}();
                                rpcClient.tcpListener.Start();
                                var localPort = ((IPEndPoint)rpcClient.tcpListener.LocalEndpoint).Port;
                        
                                if(!await createClientAction(rpcClient, localPort).ConfigureAwait(false))
                                    return null;
                        
                                {{(clientType.Events.Length > 0 ? "_ = rpcClient.ReadLoopAsync(ct);" : null)}}
                        
                                return rpcClient;
                            }
                            
                            public static Task<{{clientType.Name}}?> CreateLocalAsync(string serverExecutablePath, CancellationToken ct) =>
                                CreateAsync(async (rpcClient, localPort) =>
                                {
                                    var serverProcess = Process.Start(new ProcessStartInfo(Path.GetFullPath(serverExecutablePath), 
                                        new[] { "localhost", localPort.ToString() })
                                    {
                                        WorkingDirectory = Path.GetDirectoryName(serverExecutablePath) is not { } directoryName ? null
                                            : Path.GetFullPath(directoryName)
                                    });
                                    await rpcClient.ConnectAsync().ConfigureAwait(false);

                                    return true;
                                }, ct);

                            public static Task<{{clientType.Name}}?> CreateOverSshAsync(Uri sshServerUri, CancellationToken ct) =>
                                CreateAsync(async (rpcClient, localPort) =>
                                {
                                    if(Dns.GetHostAddresses(sshServerUri.Host).FirstOrDefault() is not { } sshServerIpAddress)
                                        return false;

                                    // find my IP address that can connect to the target ip address
                                    IPAddress? localIpAddress = default;
                                    foreach (var @interface in NetworkInterface.GetAllNetworkInterfaces())
                                        if (@interface.OperationalStatus is OperationalStatus.Up)
                                        {
                                            var properties = @interface.GetIPProperties();
                                            foreach (var unicastIpAddressInformation in properties.UnicastAddresses)
                                                if (unicastIpAddressInformation.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                                    && IsInSameSubnet(sshServerIpAddress, unicastIpAddressInformation.Address, unicastIpAddressInformation.IPv4Mask))
                                                {
                                                    localIpAddress = unicastIpAddressInformation.Address;
                                                    break;
                                                }
                                        }

                                    if (localIpAddress is null)
                                        return false;

                                    // start the ssh connection
                                    string? username = null, password = null;
                                    if (Regex.Match(sshServerUri.UserInfo, @"^(?<username>[^:]*)(:(?<password>.*))?$") is { Success: true } m)
                                        (username, password) = (m.Groups["username"].Value, m.Groups["password"].Value);
                                    if(string.IsNullOrWhiteSpace(username))
                                        return false;

                                    using var sshClient = new SshClient(new ConnectionInfo(
                                        sshServerUri.Host, sshServerUri.IsDefaultPort ? 22 : sshServerUri.Port, username, string.IsNullOrWhiteSpace(password)
                                            ? new PrivateKeyAuthenticationMethod(username, new[] { new PrivateKeyFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519")) })
                                            : new PasswordAuthenticationMethod(username, password)));

                                    await sshClient.ConnectAsync(ct).ConfigureAwait(false);

                                    // run the server executable
                                    using var command = sshClient.CreateCommand($"cd \"{Path.GetDirectoryName(sshServerUri.LocalPath)!.Replace('\\', '/')}\" && \"./{Path.GetFileName(sshServerUri.LocalPath)}\" {localIpAddress} {localPort}");
                                    _ = command.ExecuteAsync(ct); // let it run

                                    await rpcClient.ConnectAsync().ConfigureAwait(false);
                                    return true;
                                }, ct);

                            static bool IsInSameSubnet(IPAddress targetIP, IPAddress address, IPAddress mask)
                            {
                                var targetIPBytes = targetIP.GetAddressBytes();
                                var addressBytes = address.GetAddressBytes();
                                var maskBytes = mask.GetAddressBytes();
                                for (int i = 0; i < targetIPBytes.Length; i++)
                                    if ((targetIPBytes[i] & maskBytes[i]) != (addressBytes[i] & maskBytes[i]))
                                        return false;
                                return true;
                            }

                            readonly ConcurrentDictionary<int, Func<ValueTask>> requestsInFlight = [];
                            int nextRequestId = 0;

                            async Task ReadLoopAsync(CancellationToken ct)
                            {
                                try
                                {
                                    while(true)
                                    {
                                        // read one byte to determine if it's an event or data
                                        var type = await SegmentedMessagePackDeserializer.DeserializeAsync<byte>(stream!).ConfigureAwait(false);

                                        if(type == 0)
                                        {
                                            // data
                                            var requestId = await SegmentedMessagePackDeserializer.DeserializeAsync<int>(stream!).ConfigureAwait(false);
                                            if(requestsInFlight.TryGetValue(requestId, out var action))
                                            {
                                                await action().ConfigureAwait(false);
                                                requestsInFlight.TryRemove(requestId, out _);
                                            }
                                        }
                                        else if(type == 1)
                                        {
                                            // event
                                            var eventIdx = await SegmentedMessagePackDeserializer.DeserializeAsync<byte>(stream!).ConfigureAwait(false);
                                            {{string.Join("\n", clientType.Events.Select((e, eIdx) => $$"""
                                                if(eventIdx == {{eIdx}})        
                                                {
                                                    // {{e.Name}}
                                                    {{string.Join("\n", e.Parameters.Select((p, pIdx) => $"var p{pIdx} = {typeCache.GetBinaryReaderCall(p.Type)};"))}}
                                                    {{e.Name}}?.Invoke({{string.Join(", ", e.Parameters.Select((_, pIdx) => $"p{pIdx}"))}});
                                                }
                                                """))}}
                                        }
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

                            {{string.Join("\n", clientType.Events.Select(e => $$"""
                                public delegate void {{e.Name}}Delegate({{string.Join(", ", e.Parameters.Select(p => $"{p.Type.ToFullyQualifiedString()} {p.Name}"))}});
                                public event {{e.Name}}Delegate? {{e.Name}};
                                """))}}

                            {{string.Join("\n", clientType.Methods.Select((m, mIdx) => $$"""
                                public async ValueTask{{(m.ReturnType is null ? null : $"<{m.ReturnType.ToFullyQualifiedString()}>")}} {{m.Name}}Async({{string.Join(", ",
                                    m.Parameters.Select(p => $"{p.Type.ToFullyQualifiedString()} {p.Name}"))}}) 
                                {
                                    try
                                    {
                                        using (await callMonitor.EnterAsync().ConfigureAwait(false))
                                        {
                                            // {{m.Name}}
                                            await MessagePackSerializer.SerializeAsync(stream!, {{mIdx}}).ConfigureAwait(false); 

                                            // request id
                                            var requestId = Interlocked.Increment(ref nextRequestId);
                                            await MessagePackSerializer.SerializeAsync(stream!, requestId).ConfigureAwait(false);

                                            // parameters
                                            {{string.Join("\n", m.Parameters.Select(p => typeCache.GetBinaryWriterCall(p.Type, p.Name)))}}

                                            // done
                                            await stream!.FlushAsync().ConfigureAwait(false);

                                #if (NET5_0_OR_GREATER)
                                            var tcs = new TaskCompletionSource{{(m.ReturnType is null ? null : $"<{m.ReturnType.ToFullyQualifiedString()}>")}}();
                                #else
                                            var tcs = new TaskCompletionSource<{{(m.ReturnType is null ? "bool" : m.ReturnType.ToFullyQualifiedString())}}>();
                                #endif

                                            {{(m.ReturnType is null
                                                ? $$"""
                                                    requestsInFlight[requestId] = () =>
                                                    {
                                                    #if (NET5_0_OR_GREATER)
                                                        tcs.SetResult();
                                                    #else
                                                        tcs.SetResult(true);
                                                    #endif

                                                        return ValueTask.CompletedTask;
                                                    };

                                                    await tcs.Task.ConfigureAwait(false);
                                                    """
                                                : $$"""
                                                    requestsInFlight[requestId] = async () =>
                                                    {
                                                        // return type
                                                        tcs.SetResult({{typeCache.GetBinaryReaderCall(m.ReturnType)}});
                                                    };

                                                    return await tcs.Task.ConfigureAwait(false);
                                                    """)}}
                                        }
                                    }
                                    catch(Exception ex) when (ex is 
                                        ObjectDisposedException or MessagePackSerializationException or System.IO.IOException)
                                    {
                                        // pipe broken, end the server
                                        Healthy = false;
                                        FireHealthyChanged(false);

                                        throw;
                                    }
                                }
                                """))}}

                            {{typeCache.GetSupportCode()}}
                        }
                        """, Encoding.UTF8));
        });
    }
}

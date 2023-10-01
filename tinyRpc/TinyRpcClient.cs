using Nito.AsyncEx;
using Overby.Extensions.AsyncBinaryReaderWriter;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TinyRpc;

public class TinyRpcClient : IDisposable
{
    protected readonly Process serverProcess;
    protected readonly NamedPipeServerStream pipe;
    protected readonly AsyncBinaryWriter writer;
    protected readonly AsyncBinaryReader reader;
    protected readonly AsyncMonitor monitor = new();
    protected readonly AsyncManualResetEvent connectedEvent = new();

    public TinyRpcClient(string serverPath, CancellationToken ct)
    {
        var clientId = Guid.NewGuid().ToString();
        pipe = new(clientId);
        writer = new(pipe);
        reader = new(pipe);

        async Task waitForConnectionAsync()
        {
            await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            connectedEvent.Set();
        }
        _ = waitForConnectionAsync();

        serverProcess = Process.Start(new ProcessStartInfo(Path.GetFullPath(serverPath), clientId)
        {
            WorkingDirectory = Path.GetFullPath(Path.GetDirectoryName(serverPath))
        });
    }

    #region IDisposable
    private bool disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // managed
            }

            // unmanaged
            disposedValue = true;
        }
    }

    ~TinyRpcClient()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}

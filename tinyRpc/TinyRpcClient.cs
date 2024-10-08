﻿using Nito.AsyncEx;
using Overby.Extensions.AsyncBinaryReaderWriter;
using System.Diagnostics;
using System.IO.Pipes;

namespace TinyRpc;

public class TinyRpcClient : IDisposable
{
    public Process? ServerProcess { get; }
    protected readonly NamedPipeServerStream pipe;
    protected readonly AsyncBinaryWriter writer;
    protected readonly AsyncBinaryReader reader;
    protected readonly AsyncMonitor callMonitor = new();
    protected readonly AsyncMonitor readMonitor = new();
    protected readonly AsyncManualResetEvent returnReadReadyEvent = new();
    protected readonly AsyncAutoResetEvent returnReadCompletedEvent = new();
    protected readonly AsyncManualResetEvent connectedEvent = new();

    public TinyRpcClient(string serverPath, CancellationToken ct)
    {
        var clientId = Guid.NewGuid().ToString();
        pipe = new(clientId, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        writer = new(pipe);
        reader = new(pipe);

        async Task waitForConnectionAsync()
        {
            await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            connectedEvent.Set();
        }
        _ = waitForConnectionAsync();

        // despite the method description, this CAN return null
        ServerProcess = Process.Start(new ProcessStartInfo(Path.GetFullPath(serverPath), clientId)
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

            reader.Dispose();
            writer.Dispose();
            pipe.Dispose();

            // the server process can be null
            try { ServerProcess?.Kill(); } catch { }
            ServerProcess?.Dispose();

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

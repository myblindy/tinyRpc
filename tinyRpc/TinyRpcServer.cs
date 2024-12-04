using Nito.AsyncEx;
using Overby.Extensions.AsyncBinaryReaderWriter;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;

namespace TinyRpc;

public abstract class TinyRpcServer : IDisposable
{
    protected TcpClient tcpClient = new();
    protected NetworkStream? tcpStream;
    protected AsyncBinaryReader? reader;
    protected AsyncBinaryWriter? writer;

    protected readonly AsyncMonitor writeMonitor = new();

    public bool Healthy { get; protected set; } = true;

    protected abstract Task MessageHandler(CancellationToken ct);

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
            reader?.Dispose();
            writer?.Dispose();
            tcpStream?.Dispose();
            tcpClient?.Dispose();

            disposedValue = true;
        }
    }

    ~TinyRpcServer()
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

using Nito.AsyncEx;
using System.Net.Sockets;

namespace TinyRpc;

public abstract class TinyRpcServer : IDisposable
{
    protected TcpClient tcpClient = new();
    protected NetworkStream? stream;

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
            stream?.Dispose();
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

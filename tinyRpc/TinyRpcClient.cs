using Nito.AsyncEx;
using System.Net;
using System.Net.Sockets;

namespace TinyRpc;

public abstract class TinyRpcClient : IDisposable
{
    protected readonly TcpListener tcpListener = new(IPAddress.Any, 0);
    protected TcpClient? tcpClient;
    protected NetworkStream? stream;

    protected readonly AsyncMonitor callMonitor = new();

    public event EventHandler<bool>? HealthyChanged;
    protected void FireHealthyChanged(bool value) => HealthyChanged?.Invoke(this, value);
    public bool Healthy { get; protected set; } = true;

    protected async Task ConnectAsync()
    {
        tcpClient = await tcpListener.AcceptTcpClientAsync();
        stream = tcpClient.GetStream();
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

            stream?.Dispose();
            tcpClient?.Dispose();
            tcpListener.Stop();

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

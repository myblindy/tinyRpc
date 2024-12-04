using Nito.AsyncEx;
using Overby.Extensions.AsyncBinaryReaderWriter;
using System.Net;
using System.Net.Sockets;

namespace TinyRpc;

public abstract class TinyRpcClient : IDisposable
{
    protected readonly TcpListener tcpListener = new(IPAddress.Any, 0);
    protected TcpClient? tcpClient;
    protected NetworkStream? tcpStream;
    protected AsyncBinaryWriter? writer;
    protected AsyncBinaryReader? reader;

    protected readonly AsyncMonitor callMonitor = new();
    protected readonly AsyncMonitor readMonitor = new();
    protected readonly AsyncManualResetEvent returnReadReadyEvent = new();
    protected readonly AsyncAutoResetEvent returnReadCompletedEvent = new();

    protected async Task ConnectAsync()
    {
        tcpClient = await tcpListener.AcceptTcpClientAsync();
        tcpStream = tcpClient.GetStream();

        writer = new(tcpStream);
        reader = new(tcpStream);
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

            reader?.Dispose();
            writer?.Dispose();
            tcpStream?.Dispose();
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

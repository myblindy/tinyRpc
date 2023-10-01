﻿using Overby.Extensions.AsyncBinaryReaderWriter;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace TinyRpc;

public abstract class TinyRpcServer : IDisposable
{
    protected readonly NamedPipeClientStream pipe;
    protected readonly AsyncBinaryReader reader;
    protected readonly AsyncBinaryWriter writer;

    public TinyRpcServer(string[] args, CancellationToken ct)
    {
        pipe = new(".", args[0], PipeDirection.InOut, PipeOptions.Asynchronous);
        reader = new(pipe);
        writer = new(pipe);

        _ = MessageHandler(ct);
    }

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

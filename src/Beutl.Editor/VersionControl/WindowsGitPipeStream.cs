using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Editor.VersionControl;

// FileStream cannot interrupt a pending synchronous pipe read when it is disposed: the read keeps
// the handle open until the other end closes. PipeStream's async methods support CancelSynchronousIo
// on Windows. Give every operation a lifetime token so closing our end also releases pending I/O,
// even when a descendant has left the job and still holds the other end.
internal sealed class WindowsGitPipeStream : Stream
{
    private readonly AnonymousPipeClientStream _pipe;
    private readonly CancellationTokenSource _closed = new();
    private readonly CancellationToken _closedToken;
    private int _disposed;

    public WindowsGitPipeStream(SafePipeHandle handle, PipeDirection direction)
    {
        _pipe = new AnonymousPipeClientStream(direction, handle);
        _closedToken = _closed.Token;
    }

    public override bool CanRead => _pipe.CanRead;

    public override bool CanWrite => _pipe.CanWrite;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _pipe.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource? linked = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closedToken)
            : null;
        return await _pipe.ReadAsync(buffer, linked?.Token ?? _closedToken).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource? linked = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closedToken)
            : null;
        await _pipe.WriteAsync(buffer, linked?.Token ?? _closedToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try
                {
                    _closed.Cancel();
                }
                finally
                {
                    _pipe.Dispose();
                    _closed.Dispose();
                }
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }
}

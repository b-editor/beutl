using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Beutl.Services;

// Windows/Linux protocol handlers start another process. Hand its link to the first
// running instance, including while that instance is still initializing Avalonia.
internal sealed class PackageLinkBroker : IDisposable
{
    private static readonly UTF8Encoding s_encoding = new(false, true);
    private readonly NamedPipeServerStream _pipe;
    private readonly Mutex _instance;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Queue<string> _pending = new();
    private readonly object _gate = new();
    private readonly Task _listener;
    private Action<string>? _handler;

    internal static string PipeName => "beutl-install-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "\n" + BeutlEnvironment.GetHomeDirectoryPath())))[..24];

    private PackageLinkBroker(string pipeName, Mutex instance)
    {
        _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _instance = instance;
        _listener = Task.Run(ListenAsync);
    }

    public static PackageLinkBroker? TryCreate(string? pipeName = null)
    {
        Mutex? instance = null;
        try
        {
            pipeName ??= PipeName;
            // Keep the named object alive without thread-affine ownership. Its lifetime
            // selects the server; a new server can clean up a Unix socket left by a crash.
            instance = new Mutex(false, pipeName, out bool createdNew);
            if (!createdNew)
            {
                instance.Dispose();
                return null;
            }
            return new PackageLinkBroker(pipeName, instance);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            instance?.Dispose();
            return null;
        }
    }

    public void SetHandler(Action<string> handler)
    {
        lock (_gate)
        {
            _handler = handler;
            while (_pending.TryDequeue(out string? uri))
                handler(uri);
        }
    }

    public static async Task<bool> TryForwardAsync(string uri, string? pipeName = null)
    {
        if (!PackageInstallRequest.TryParse(uri, out _))
            return false;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            byte[] payload = s_encoding.GetBytes(uri);
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await pipe.WriteAsync(header, timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            byte[] response = new byte[1];
            await pipe.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
            return response[0] == 1;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await _pipe.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                byte[] header = new byte[4];
                await _pipe.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is <= 0 or > PackageInstallRequest.MaxUriLength)
                    continue;

                byte[] payload = new byte[length];
                await _pipe.ReadExactlyAsync(payload, timeout.Token).ConfigureAwait(false);
                string uri = s_encoding.GetString(payload);
                if (!PackageInstallRequest.TryParse(uri, out _))
                    continue;

                lock (_gate)
                {
                    if (_handler != null)
                        _handler(uri);
                    else if (_pending.Count < 16)
                        _pending.Enqueue(uri);
                    else
                        continue;
                }

                await _pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or DecoderFallbackException or UnauthorizedAccessException)
            {
                // A disconnected or malformed client must not stop later activations.
            }
            finally
            {
                if (_pipe.IsConnected)
                    _pipe.Disconnect();
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener.GetAwaiter().GetResult();
        _pipe.Dispose();
        _instance.Dispose();
        _cancellation.Dispose();
    }
}

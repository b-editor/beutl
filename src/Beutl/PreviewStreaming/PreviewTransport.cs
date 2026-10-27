using System.Text.Json;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.PreviewStreaming;

// One Pion worker per sharing session. The OS pipe is private to its parent and transports
// compressed media only. All writes are serialized; pipe backpressure bounds queued media.
internal sealed class PreviewTransport : IAsyncDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _write = new(1);
    private readonly Task _reader;
    private readonly Task _errors;

    internal PreviewTransport(Action<JsonElement> received, Action stopped)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "Beutl.PreviewTransport" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _process = Process.Start(new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start the WebRTC transport.");
        _reader = ReadAsync(received, stopped);
        _errors = ReadErrorsAsync();
    }

    internal async Task SendAsync(object message, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(message);
        await _write.WaitAsync(ct).ConfigureAwait(false);
        try { await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false); }
        finally { _write.Release(); }
    }

    private async Task ReadAsync(Action<JsonElement> received, Action stopped)
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                using var json = JsonDocument.Parse(line);
                received(json.RootElement);
            }
        }
        catch (Exception ex)
        {
            Log.CreateLogger<PreviewTransport>().LogError(ex, "WebRTC transport stopped.");
        }
        finally { stopped(); }
    }

    private async Task ReadErrorsAsync()
    {
        while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            Log.CreateLogger<PreviewTransport>().LogWarning("WebRTC transport: {Message}", line);
    }

    public async ValueTask DisposeAsync()
    {
        _process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); }
        await Task.WhenAll(_reader, _errors).ConfigureAwait(false);
        _process.Dispose();
        _write.Dispose();
    }
}

using System.Security.Cryptography;

namespace Beutl.PreviewStreaming;

// Publishes media from the editor to WebRTC peers. Pairing, signaling and the viewer
// page are hosted by Beutl Web; the desktop does not serve HTTP or embed a web UI.
internal sealed partial class MobilePreviewServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, string> _paired = [];
    private string _pairingCode = "";
    private DateTimeOffset _codeExpires;
    private Task? _disposeTask;
    private Task _encoderTask = Task.CompletedTask;

    private MobilePreviewServer() { }

    internal Uri Endpoint { get; private set; } = null!;
    internal string PairingCode { get { lock (_gate) return _pairingCode; } }
    internal DateTimeOffset CodeExpires { get { lock (_gate) return _codeExpires; } }
    internal event EventHandler? ClientsChanged;

    internal void RenewPairingCode()
    {
        lock (_gate)
        {
            _pairingCode = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            _codeExpires = DateTimeOffset.UtcNow.AddMinutes(10);
            SendRelayControl(new { type = "pairing", code = _pairingCode, expires = _codeExpires.ToUnixTimeMilliseconds() });
        }
    }

    internal (string Id, string Address)[] GetClients()
    {
        lock (_gate) return _relayClients;
    }

    internal (string Id, string Address, bool Connected)[] GetDevices()
    {
        lock (_gate)
            return _paired.Select(pair => (pair.Key, pair.Value,
                _relayClients.Any(client => client.Id == pair.Key))).ToArray();
    }

    internal void Disconnect(string id)
    {
        lock (_gate)
        {
            SendRelayControl(new { type = "disconnect", id });
            _paired.Remove(id);
        }
        ClientsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void DisconnectAll()
    {
        lock (_gate)
        {
            SendRelayControl(new { type = "disconnectAll" });
            _paired.Clear();
        }
        RenewPairingCode();
        ClientsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEncodingFailed()
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return;
            // A stopped encoder cannot recover by renegotiating a peer. End this sharing
            // session through the same lifetime path as a lost publisher connection.
            _relay?.Socket.Abort();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        // Closing the publisher socket ends the room and revokes all pairings on the Web side.
        _stop.Cancel();
        _latest?.Dispose();
        _latest = null;
        await Task.WhenAll(_encoderTask, _audioTask, _relayTask).ConfigureAwait(false);
        await _transportGate.WaitAsync().ConfigureAwait(false);
        try { if (_transport is not null) await _transport.DisposeAsync().ConfigureAwait(false); }
        finally { _transportGate.Release(); }
    }
}

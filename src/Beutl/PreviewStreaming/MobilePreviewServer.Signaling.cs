using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Beutl.Api;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    private PreviewTransport? _transport;
    private readonly SemaphoreSlim _transportGate = new(1);
    private readonly Dictionary<string, bool> _peers = [];
    private PreviewIceServer[] _iceServers = [];
    private int _keyFrame = 1;

    private void OnTransportStopped()
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return;
            foreach (string id in _peers.Keys)
                SendToViewer(id, "{\"type\":\"error\",\"fatal\":true}");
        }
    }

    private void OnTransportMessage(JsonElement message)
    {
        string id = message.GetProperty("id").GetString()!;
        string type = message.GetProperty("type").GetString()!;
        lock (_gate)
        {
            if (type == "keyframe" || (type == "state" && message.GetProperty("state").GetString() == "connected"))
            {
                Interlocked.Exchange(ref _keyFrame, 1);
                _updates.Writer.TryWrite(true);
            }
            if (type == "keyframe") return;
            SendToViewer(id, message.GetRawText());
        }
    }

    private void SendToViewer(string id, string json)
    {
        if (_relayClients is not null) SendRelayControl(new { type = "signal", id, signal = JsonSerializer.Deserialize<JsonElement>(json) });
        else if (_clients.TryGetValue(id, out var client)) client.Controls.Writer.TryWrite(json);
    }

    private async Task HandleSignalAsync(string id, JsonElement signal, CancellationToken ct)
    {
        string? type = signal.TryGetProperty("type", out var kind) ? kind.GetString() : null;
        if (type == "control") { await HandleControlAsync(id, signal, ct).ConfigureAwait(false); return; }
        if (type is not ("offer" or "candidate")) return;
        await _transportGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _transport ??= new PreviewTransport(OnTransportMessage, OnTransportStopped);
            if (type == "offer")
            {
                bool hdr = signal.TryGetProperty("hdr", out var value) && value.GetBoolean();
                lock (_gate) _peers[id] = hdr;
                await _transport.SendAsync(new { type, id, hdr, sdp = signal.GetProperty("sdp").GetString(), iceServers = _iceServers }, ct).ConfigureAwait(false);
            }
            else
                await _transport.SendAsync(new { type, id, candidate = signal.GetProperty("candidate") }, ct).ConfigureAwait(false);
        }
        finally { _transportGate.Release(); }
    }

    private async Task RemovePeerAsync(string id, Client? client = null)
    {
        await _transportGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                // A reconnect can replace the socket before the previous request finishes.
                if (client is not null && _clients.TryGetValue(id, out var current) && current != client) return;
                _peers.Remove(id);
            }
            if (_transport is not null && !_stop.IsCancellationRequested)
                await _transport.SendAsync(new { type = "remove", id }, _stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _transportGate.Release(); }
    }

    private async Task RunClientAsync(Client client, CancellationTokenSource cancellation, string? id = null)
    {
        var token = cancellation.Token;
        Task send = SendControlsAsync();
        Task receive = ReceiveAsync();
        try { await Task.WhenAny(send, receive); }
        finally
        {
            cancellation.Cancel();
            client.Socket.Abort();
            try { await Task.WhenAll(send, receive); }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        }

        async Task SendControlsAsync()
        {
            await foreach (string control in client.Controls.Reader.ReadAllAsync(token))
                await client.Socket.SendAsync(Encoding.UTF8.GetBytes(control).AsMemory(), WebSocketMessageType.Text, true, token);
        }

        async Task ReceiveAsync()
        {
            byte[] buffer = new byte[64 * 1024];
            while (!token.IsCancellationRequested)
            {
                int count = 0;
                ValueWebSocketReceiveResult message;
                do
                {
                    message = await client.Socket.ReceiveAsync(buffer.AsMemory(count), token);
                    if (message.MessageType != WebSocketMessageType.Text) return;
                    count += message.Count;
                    if (count == buffer.Length && !message.EndOfMessage) return;
                } while (!message.EndOfMessage);
                using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
                if (id is not null) await HandleSignalAsync(id, json.RootElement, token);
                else await HandleRelayMessageAsync(json.RootElement, token);
            }
        }
    }

    private sealed record Client(WebSocket Socket, string Address)
    {
        internal Channel<string> Controls { get; } = Channel.CreateBounded<string>(64);
    }
}

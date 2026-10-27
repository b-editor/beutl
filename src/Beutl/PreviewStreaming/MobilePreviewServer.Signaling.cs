using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Beutl.Api;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    private PreviewTransport? _transport;
    private readonly SemaphoreSlim _transportGate = new(1);
    private readonly Dictionary<string, bool> _peers = [];
    private int _keyFrame = 1;

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

    private void SendToViewer(string id, string json, bool playback = false)
    {
        SendRelayControl(new { type = "signal", id, signal = JsonSerializer.Deserialize<JsonElement>(json) }, playback ? id : null);
    }

    private async Task HandleSignalAsync(string id, JsonElement signal, CancellationToken ct)
    {
        if (signal.ValueKind != JsonValueKind.Object
            || !signal.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String) return;
        string? type = kind.GetString();
        if (type == "control") { await HandleControlAsync(id, signal, ct).ConfigureAwait(false); return; }
        if (type is not ("offer" or "candidate")) return;
        if (!signal.TryGetProperty("connectionId", out var connection) || connection.ValueKind != JsonValueKind.String) return;
        string connectionId = connection.GetString()!;
        if (!signal.TryGetProperty(type == "offer" ? "sdp" : "candidate", out var payload)) return;
        if (type == "offer" && payload.ValueKind != JsonValueKind.String) return;
        await _transportGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _transport ??= new PreviewTransport(OnTransportMessage);
            if (type == "offer")
            {
                bool hdr = signal.TryGetProperty("hdr", out var value) && value.ValueKind == JsonValueKind.True;
                lock (_gate) _peers[id] = hdr;
                var servers = signal.TryGetProperty("iceServers", out var ice) ? ice.Deserialize<PreviewIceServer[]>() ?? [] : [];
                bool relayOnly = signal.TryGetProperty("relayOnly", out var relay) && relay.ValueKind == JsonValueKind.True;
                _transport.Offer(id, connectionId, payload.GetString()!, hdr, servers, relayOnly);
            }
            else
                _transport.AddCandidate(id, connectionId, payload);
        }
        finally { _transportGate.Release(); }
    }

    private async Task RemovePeerAsync(string id)
    {
        await _transportGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                _peers.Remove(id);
            }
            if (_transport is not null && !_stop.IsCancellationRequested)
                _transport.Remove(id);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _transportGate.Release(); }
    }

    private async Task RunClientAsync(Client client, CancellationTokenSource cancellation)
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
            await foreach (string control in client.Controls.ReadAllAsync(token))
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
                await HandleRelayMessageAsync(json.RootElement, token);
            }
        }
    }

    private sealed record Client(WebSocket Socket)
    {
        internal PreviewMessageQueue Controls { get; } = new();
    }
}

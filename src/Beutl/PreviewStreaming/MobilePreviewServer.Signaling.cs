using System.Text.Json;
using Beutl.Api;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    private PreviewTransport? _transport;
    private readonly SemaphoreSlim _transportGate = new(1);
    private readonly Dictionary<string, bool> _peers = [];

    private void OnTransportMessage(PreviewTransportEvent message)
    {
        if (message.Kind == PreviewTransportEventKind.PlaybackReady)
        {
            SendCurrentPlayback(message.Id);
            return;
        }
        if (message.Kind == PreviewTransportEventKind.KeyFrame
            || (message.Kind == PreviewTransportEventKind.State && message.Value == "connected"))
            RequestKeyFrame();

        string id = message.Id;
        string connectionId = message.ConnectionId;
        object? signal = message.Kind switch
        {
            PreviewTransportEventKind.Answer => new { type = "answer", id, connectionId, sdp = message.Value, hdr = message.Hdr },
            PreviewTransportEventKind.Candidate => new { type = "candidate", id, connectionId, candidate = new { candidate = message.Value, sdpMid = message.Mid } },
            PreviewTransportEventKind.State => new { type = "state", id, connectionId, state = message.Value },
            PreviewTransportEventKind.Error => new { type = "error", id, connectionId, message = message.Value },
            _ => null
        };
        if (signal is not null) SendToViewer(id, signal);
    }

    private void SendToViewer(string id, object signal)
        => SendRelayControl(new { type = "signal", id, signal });

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
                if (!TryReadIceServers(signal, out var servers))
                {
                    SendToViewer(id, new { type = "error", connectionId, message = "negotiation-failed" });
                    return;
                }
                bool hdr = signal.TryGetProperty("hdr", out var value) && value.ValueKind == JsonValueKind.True;
                lock (_gate)
                {
                    if (hdr && _hdrUnavailable)
                    {
                        SendToViewer(id, new { type = "error", connectionId, message = "hdr-unavailable" });
                        return;
                    }
                    _peers[id] = hdr;
                }
                bool relayOnly = signal.TryGetProperty("relayOnly", out var relay) && relay.ValueKind == JsonValueKind.True;
                // The viewer is registered before its peer exists, so a failed HDR encoder can still reach it. A
                // rejected offer leaves it without a peer, and nothing may keep encoding video or audio for it.
                if (!_transport.Offer(id, connectionId, payload.GetString()!, hdr, servers, relayOnly))
                {
                    lock (_gate) _peers.Remove(id);
                }
            }
            else if (payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("candidate", out var candidate) && candidate.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(candidate.GetString()))
            {
                string mid = payload.TryGetProperty("sdpMid", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()! : "0";
                _transport.AddCandidate(id, connectionId, candidate.GetString()!, mid);
            }
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

    private static bool TryReadIceServers(JsonElement signal, out PreviewIceServer[] servers)
    {
        servers = [];
        if (!signal.TryGetProperty("iceServers", out var ice)) return true;
        if (ice.ValueKind != JsonValueKind.Array) return false;
        try
        {
            servers = ice.Deserialize<PreviewIceServer[]>()!;
            return servers.All(server => server?.Urls is { } urls && urls.All(url => url is not null));
        }
        catch (JsonException)
        {
            // Reject the viewer's offer without ending the shared publisher connection.
            return false;
        }
    }
}

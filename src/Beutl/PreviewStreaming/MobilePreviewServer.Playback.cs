using System.Text.Json;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.PreviewStreaming;

internal readonly record struct PreviewControl(string Action, double Position);

internal sealed partial class MobilePreviewServer
{
    private readonly SemaphoreSlim _controlGate = new(1);
    private string _playbackState = "{\"type\":\"playback\",\"enabled\":false}";
    internal Func<string, PreviewControl, CancellationToken, Task<bool>>? ControlRequested { get; set; }

    internal bool IsClientConnected(string id)
    {
        lock (_gate)
            return !_stop.IsCancellationRequested && _paired.ContainsKey(id) && _relayClients.Any(c => c.Id == id);
    }

    internal void PublishPlayback(bool playing, double position, double minimum, double duration, double frameRate, bool enabled)
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return;
            _playbackState = JsonSerializer.Serialize(new { type = "playback", playing, position, minimum, duration, frameRate, enabled });
            foreach (string id in _relayClients.Select(c => c.Id))
                _transport?.SendPlayback(id, _playbackState);
        }
    }

    private async Task HandleControlAsync(string id, JsonElement message, CancellationToken ct)
    {
        if (!message.TryGetProperty("requestId", out var request) || request.ValueKind != JsonValueKind.Number || !request.TryGetInt32(out int requestId)) return;
        string? action = message.TryGetProperty("action", out var kind) && kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
        double position = 0;
        bool valid = action is "play" or "pause" or "previous" or "next" || (action == "seek"
            && message.TryGetProperty("position", out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out position) && double.IsFinite(position));
        bool ok = false;
        if (valid)
        {
            await _controlGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (IsClientConnected(id) && ControlRequested is { } control)
                    ok = await control(id, new PreviewControl(action!, position), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { Log.CreateLogger<MobilePreviewServer>().LogWarning(ex, "Mobile preview control failed."); }
            finally { _controlGate.Release(); }
        }
        lock (_gate)
            if (IsClientConnected(id)) SendToViewer(id, JsonSerializer.Serialize(new { type = "control-result", requestId, ok }));
    }
}

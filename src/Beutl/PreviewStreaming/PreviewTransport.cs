using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Beutl.Api;

namespace Beutl.PreviewStreaming;

// Native media stays binary; the only serialized messages are WebRTC signaling.
internal sealed class PreviewTransport : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<long, Peer> Registrations = new();
    private static readonly NativeCallback Callback = OnNativeEvent;
    private static long s_nextToken;
    private readonly object _gate = new();
    private readonly Dictionary<string, Peer> _peers = [];
    private readonly Channel<(Peer Peer, int Kind, string Value, string Mid)> _events =
        Channel.CreateUnbounded<(Peer, int, string, string)>(new() { SingleReader = true });
    private readonly Action<JsonElement> _message;
    private readonly Task _eventTask;

    internal PreviewTransport(Action<JsonElement> message)
    {
        _message = message;
        _eventTask = ProcessEventsAsync();
    }

    internal void Offer(string id, string connectionId, string sdp, bool hdr, PreviewIceServer[] servers, bool relayOnly)
    {
        string? error = null;
        lock (_gate)
        {
            Remove(id);
            var peer = new Peer(this, id, connectionId, hdr, Interlocked.Increment(ref s_nextToken));
            Registrations[peer.Token] = peer;
            _peers[id] = peer;
            var ice = servers.SelectMany(server => server.Urls.Select(url => new NativeIceServer
            {
                Url = url,
                Username = server.Username,
                Password = server.Credential
            })).ToArray();
            peer.Handle = Create(sdp, hdr ? 1 : 0, relayOnly ? 1 : 0, ice, ice.Length, peer.Token, Callback);
            if (peer.Handle == IntPtr.Zero)
            {
                error = Marshal.PtrToStringUTF8(Error()) == "hdr-unavailable" ? "hdr-unavailable" : "negotiation-failed";
                Remove(id);
            }
        }
        // Callers may acquire the server lock here, so never dispatch while holding the transport lock.
        if (error is not null)
            _message(JsonSerializer.SerializeToElement(new { type = "error", id, connectionId, message = error }));
    }

    internal void AddCandidate(string id, string connectionId, JsonElement candidate)
    {
        lock (_gate)
        {
            if (!_peers.TryGetValue(id, out var peer) || peer.ConnectionId != connectionId || peer.Handle == IntPtr.Zero) return;
            if (candidate.ValueKind != JsonValueKind.Object || !candidate.TryGetProperty("candidate", out var value)
                || value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString())) return;
            string mid = candidate.TryGetProperty("sdpMid", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : "0";
            if (Candidate(peer.Handle, value.GetString()!, mid) != 0)
                _events.Writer.TryWrite((peer, 3, "failed", ""));
        }
    }

    internal void SendMedia(byte[] data, uint timestamp, bool video, bool hdr = false)
    {
        lock (_gate)
        {
            foreach (var peer in _peers.Values)
                if (peer.Handle != IntPtr.Zero && (!video || peer.Hdr == hdr) &&
                    Send(peer.Handle, video ? 1 : 0, data, data.Length, timestamp) != 0)
                    _events.Writer.TryWrite((peer, 3, "failed", ""));
        }
    }

    internal void Remove(string id)
    {
        lock (_gate)
        {
            if (!_peers.Remove(id, out var peer)) return;
            Registrations.TryRemove(peer.Token, out _);
            if (peer.Handle != IntPtr.Zero) { Delete(peer.Handle); peer.Handle = IntPtr.Zero; }
        }
    }

    internal void SendPlayback(string id, string state)
    {
        lock (_gate)
        {
            if (_peers.TryGetValue(id, out var peer) && peer.Handle != IntPtr.Zero && Playback(peer.Handle, state) != 0)
                _events.Writer.TryWrite((peer, 3, "failed", ""));
        }
    }

    private static void OnNativeEvent(long token, int kind, IntPtr value, IntPtr mid)
    {
        // Never call native code or block a native callback on the rendering/signaling locks.
        if (Registrations.TryGetValue(token, out var peer))
            peer.Owner._events.Writer.TryWrite((peer, kind, Marshal.PtrToStringUTF8(value) ?? "", Marshal.PtrToStringUTF8(mid) ?? ""));
    }

    private async Task ProcessEventsAsync()
    {
        await foreach (var (peer, kind, value, mid) in _events.Reader.ReadAllAsync())
        {
            if (!Registrations.ContainsKey(peer.Token)) continue;
            object? signal = kind switch
            {
                1 => new { type = "answer", id = peer.Id, connectionId = peer.ConnectionId, sdp = value, hdr = peer.Hdr },
                2 => new { type = "candidate", id = peer.Id, connectionId = peer.ConnectionId, candidate = new { candidate = value, sdpMid = mid } },
                3 => new { type = "state", id = peer.Id, connectionId = peer.ConnectionId, state = value },
                4 => new { type = "keyframe", id = peer.Id, connectionId = peer.ConnectionId },
                5 => new { type = "playback-ready", id = peer.Id, connectionId = peer.ConnectionId },
                _ => null
            };
            if (signal is not null) _message(JsonSerializer.SerializeToElement(signal));
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            foreach (string id in _peers.Keys.ToArray()) Remove(id);
            _events.Writer.TryComplete();
        }
        await _eventTask.ConfigureAwait(false);
    }

    private sealed record Peer(PreviewTransport Owner, string Id, string ConnectionId, bool Hdr, long Token)
    {
        internal IntPtr Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeIceServer
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)] internal string Url;
        [MarshalAs(UnmanagedType.LPUTF8Str)] internal string? Username;
        [MarshalAs(UnmanagedType.LPUTF8Str)] internal string? Password;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeCallback(long token, int kind, IntPtr value, IntPtr mid);
    private const string Library = "BeutlDataChannel";
    [DllImport(Library, EntryPoint = "beutl_rtc_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Create([MarshalAs(UnmanagedType.LPUTF8Str)] string sdp, int hdr, int relayOnly,
        [In] NativeIceServer[] servers, int count, long token, NativeCallback callback);
    [DllImport(Library, EntryPoint = "beutl_rtc_candidate", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Candidate(IntPtr peer, [MarshalAs(UnmanagedType.LPUTF8Str)] string candidate, [MarshalAs(UnmanagedType.LPUTF8Str)] string mid);
    [DllImport(Library, EntryPoint = "beutl_rtc_send", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Send(IntPtr peer, int video, [In] byte[] data, int size, uint timestamp);
    [DllImport(Library, EntryPoint = "beutl_rtc_delete", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Delete(IntPtr peer);
    [DllImport(Library, EntryPoint = "beutl_rtc_playback", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Playback(IntPtr peer, [MarshalAs(UnmanagedType.LPUTF8Str)] string state);
    [DllImport(Library, EntryPoint = "beutl_rtc_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Error();
}

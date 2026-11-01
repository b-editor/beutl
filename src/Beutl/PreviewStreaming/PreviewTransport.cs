using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Beutl.Api;

namespace Beutl.PreviewStreaming;

internal readonly record struct PreviewPlaybackState(long Revision, string Json);

// Owns native peer lifetimes and dispatches callbacks without holding the transport lock.
// The server translates these events to the Web signaling protocol.
internal sealed class PreviewTransport : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<long, Peer> Registrations = new();
    private static readonly NativeCallback Callback = OnNativeEvent;
    private static long s_nextToken;
    private readonly object _gate = new();
    private readonly Dictionary<string, Peer> _peers = [];
    private readonly Channel<(Peer Peer, PreviewTransportEventKind Kind, string Value, string Mid)> _events =
        Channel.CreateUnbounded<(Peer, PreviewTransportEventKind, string, string)>(new() { SingleReader = true });
    private readonly Action<PreviewTransportEvent> _message;
    private readonly Func<IntPtr, string, int> _sendPlayback;
    private readonly Task _eventTask;

    internal PreviewTransport(Action<PreviewTransportEvent> message, Func<IntPtr, string, int>? sendPlayback = null)
    {
        _message = message;
        _sendPlayback = sendPlayback ?? Playback;
        _eventTask = ProcessEventsAsync();
    }

    // Returns false when no peer could be created; the viewer has then been sent the error, and the transport keeps
    // no peer for it, not even an earlier one.
    internal bool Offer(string id, string connectionId, string sdp, bool hdr, PreviewIceServer[] servers, bool relayOnly)
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
            _message(new PreviewTransportEvent(PreviewTransportEventKind.Error, id, connectionId, hdr, error));
        return error is null;
    }

    internal void AddCandidate(string id, string connectionId, string candidate, string mid)
    {
        lock (_gate)
        {
            if (!_peers.TryGetValue(id, out var peer) || peer.ConnectionId != connectionId || peer.Handle == IntPtr.Zero) return;
            if (Candidate(peer.Handle, candidate, mid) != 0)
                _events.Writer.TryWrite((peer, PreviewTransportEventKind.State, "failed", ""));
        }
    }

    internal void SendMedia(byte[] data, uint timestamp, bool video, bool hdr = false)
    {
        lock (_gate)
        {
            foreach (var peer in _peers.Values)
                if (peer.Handle != IntPtr.Zero && (!video || peer.Hdr == hdr) &&
                    Send(peer.Handle, video ? 1 : 0, data, data.Length, timestamp) != 0)
                    _events.Writer.TryWrite((peer, PreviewTransportEventKind.State, "failed", ""));
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

    internal void SendPlayback(string id, PreviewPlaybackState state)
    {
        lock (_gate)
        {
            if (!_peers.TryGetValue(id, out var peer) || peer.Handle == IntPtr.Zero
                || state.Revision < peer.PlaybackRevision) return;
            // A channel may open after this revision was first published, so allow its replay.
            peer.PlaybackRevision = state.Revision;
            if (_sendPlayback(peer.Handle, state.Json) != 0)
                _events.Writer.TryWrite((peer, PreviewTransportEventKind.State, "failed", ""));
        }
    }

    private static void OnNativeEvent(long token, int kind, IntPtr value, IntPtr mid)
    {
        // Never call native code or block a native callback on the rendering/signaling locks.
        if (Registrations.TryGetValue(token, out var peer))
            peer.Owner._events.Writer.TryWrite((peer, (PreviewTransportEventKind)kind,
                Marshal.PtrToStringUTF8(value) ?? "", Marshal.PtrToStringUTF8(mid) ?? ""));
    }

    private async Task ProcessEventsAsync()
    {
        await foreach (var (peer, kind, value, mid) in _events.Reader.ReadAllAsync())
        {
            if (!Registrations.ContainsKey(peer.Token)) continue;
            _message(new PreviewTransportEvent(kind, peer.Id, peer.ConnectionId, peer.Hdr, value, mid));
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
        internal long PlaybackRevision;
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

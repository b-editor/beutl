using System.Threading.Channels;
using Beutl.PreviewStreaming;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
public sealed class PreviewTransportTests
{
    [Test]
    public async Task Failed_offer_callback_does_not_hold_the_transport_lock()
    {
        PreviewTransport? current = null;
        bool playbackCompleted = false;
        Task? playback = null;
        await using var transport = new PreviewTransport(message =>
        {
            if (message.Kind != PreviewTransportEventKind.Error) return;
            // Emulate the UI publishing while signaling reports an error to the server.
            playback = Task.Run(() => current!.SendPlayback("phone", "{}"));
            playbackCompleted = playback.Wait(TimeSpan.FromSeconds(2));
        });
        current = transport;
        transport.Offer("phone", "hdr", Offer, true, [], false);
        Assert.That(playback, Is.Not.Null);
        await playback!;
        Assert.That(playbackCompleted, Is.True, "The callback must not block another thread's playback publication.");
    }

    [Test]
    public async Task Native_transport_negotiates_payloads_and_replaces_failed_connections()
    {
        var messages = Channel.CreateUnbounded<PreviewTransportEvent>();
        await using var transport = new PreviewTransport(message => messages.Writer.TryWrite(message));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        async Task<PreviewTransportEvent> Read(PreviewTransportEventKind kind, string connection)
        {
            await foreach (var message in messages.Reader.ReadAllAsync(timeout.Token))
                if (message.Kind == kind && message.ConnectionId == connection)
                    return message;
            throw new AssertionException("No native response");
        }
        transport.Offer("phone", "first", Offer, false, [], false);
        string sdp = (await Read(PreviewTransportEventKind.Answer, "first")).Value;
        Assert.That(sdp, Does.Contain("a=rtpmap:98 VP8/90000"));
        Assert.That(sdp, Does.Contain("a=rtpmap:109 opus/48000/2"));
        Assert.That(sdp, Does.Contain("m=application"));
        transport.SendPlayback("phone", "{\"type\":\"playback\",\"position\":1}");
        // Stale trickled candidates must not close a valid connection.
        transport.AddCandidate("phone", "old", "invalid", "0");
        transport.Offer("phone", "hdr", Offer, true, [], false);
        Assert.That((await Read(PreviewTransportEventKind.Error, "hdr")).Value, Is.EqualTo("hdr-unavailable"));
        transport.Offer("phone", "sdr", Offer, false, [], false);
        Assert.That((await Read(PreviewTransportEventKind.Answer, "sdr")).Hdr, Is.False);
        transport.Remove("phone");
        transport.AddCandidate("phone", "sdr", "invalid", "0");
    }

    internal static readonly string Offer = """
        v=0
        o=- 1 1 IN IP4 127.0.0.1
        s=-
        t=0 0
        a=group:BUNDLE 0 1 2
        a=ice-ufrag:preview
        a=ice-pwd:preview-test-password-123456
        a=fingerprint:sha-256 AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA
        a=setup:actpass
        m=video 9 UDP/TLS/RTP/SAVPF 98
        c=IN IP4 0.0.0.0
        a=mid:0
        a=recvonly
        a=rtcp-mux
        a=rtpmap:98 VP8/90000
        a=rtcp-fb:98 nack
        a=rtcp-fb:98 nack pli
        a=extmap:1 urn:ietf:params:rtp-hdrext:sdes:mid
        m=audio 9 UDP/TLS/RTP/SAVPF 109
        c=IN IP4 0.0.0.0
        a=mid:1
        a=recvonly
        a=rtcp-mux
        a=rtpmap:109 opus/48000/2
        a=extmap:1 urn:ietf:params:rtp-hdrext:sdes:mid
        m=application 9 UDP/DTLS/SCTP webrtc-datachannel
        c=IN IP4 0.0.0.0
        a=mid:2
        a=sctp-port:5000
        """.Replace("\n", "\r\n") + "\r\n";
}

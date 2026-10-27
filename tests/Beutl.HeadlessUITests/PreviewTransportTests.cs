using System.Text.Json;
using System.Threading.Channels;
using Beutl.PreviewStreaming;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
public sealed class PreviewTransportTests
{
    [Test]
    public async Task Native_transport_negotiates_payloads_and_replaces_failed_connections()
    {
        var messages = Channel.CreateUnbounded<JsonElement>();
        await using var transport = new PreviewTransport(message => messages.Writer.TryWrite(message));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        async Task<JsonElement> Read(string type, string connection)
        {
            await foreach (var message in messages.Reader.ReadAllAsync(timeout.Token))
                if (message.GetProperty("type").GetString() == type && message.GetProperty("connectionId").GetString() == connection)
                    return message;
            throw new AssertionException("No native response");
        }
        transport.Offer("phone", "first", Offer, false, [], false);
        string sdp = (await Read("answer", "first")).GetProperty("sdp").GetString()!;
        Assert.That(sdp, Does.Contain("a=rtpmap:98 VP8/90000"));
        Assert.That(sdp, Does.Contain("a=rtpmap:109 opus/48000/2"));
        // A malformed or stale trickled candidate must not close a valid connection.
        transport.AddCandidate("phone", "first", JsonSerializer.SerializeToElement(new { candidate = 17 }));
        transport.AddCandidate("phone", "first", JsonSerializer.SerializeToElement("invalid"));
        transport.AddCandidate("phone", "old", JsonSerializer.SerializeToElement(new { candidate = "invalid" }));
        transport.Offer("phone", "hdr", Offer, true, [], false);
        Assert.That((await Read("error", "hdr")).GetProperty("message").GetString(), Is.EqualTo("hdr-unavailable"));
        transport.Offer("phone", "sdr", Offer, false, [], false);
        Assert.That((await Read("answer", "sdr")).GetProperty("hdr").GetBoolean(), Is.False);
        transport.Remove("phone");
        transport.AddCandidate("phone", "sdr", JsonSerializer.SerializeToElement(new { candidate = "invalid" }));
    }

    private static readonly string Offer = """
        v=0
        o=- 1 1 IN IP4 127.0.0.1
        s=-
        t=0 0
        a=group:BUNDLE 0 1
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
        """.Replace("\n", "\r\n") + "\r\n";
}

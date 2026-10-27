#include "packetizer.hpp"
#include <atomic>
#include <cctype>
#include <cstring>
#include <mutex>
#include <regex>

#ifdef _WIN32
#define API extern "C" __declspec(dllexport)
#else
#define API extern "C" __attribute__((visibility("default")))
#endif

// All callbacks carry an opaque, monotonically increasing .NET token. The managed
// callback remains rooted for process lifetime and ignores tokens removed before deletion.
using Callback = void (*)(int64_t token, int kind, const char *value, const char *mid);
struct IceServer { const char *url; const char *username; const char *password; };
struct Peer {
    std::shared_ptr<rtc::PeerConnection> pc;
    std::shared_ptr<rtc::Track> video, audio;
    std::shared_ptr<rtc::DataChannel> playback;
    std::shared_ptr<rtc::RtpPacketizationConfig> videoConfig, audioConfig;
    ~Peer() {
        if (video) video->resetCallbacks();
        if (audio) audio->resetCallbacks();
        if (playback) playback->resetCallbacks();
        if (pc) { pc->resetCallbacks(); pc->close(); }
    }
};
static thread_local std::string lastError;
static std::string lower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) { return std::tolower(c); });
    return value;
}
static uint32_t nextSsrc() {
    static std::atomic<uint32_t> next{0x42545000};
    return ++next;
}

API const char *beutl_rtc_error() { return lastError.c_str(); }
API Peer *beutl_rtc_create(const char *sdp, int hdr, int relayOnly, const IceServer *servers,
                         int count, int64_t token, Callback callback) {
    try {
        rtc::Configuration config;
        config.disableAutoNegotiation = true;
        config.iceTransportPolicy = relayOnly ? rtc::TransportPolicy::Relay : rtc::TransportPolicy::All;
        for (int i = 0; i < count; ++i) {
            rtc::IceServer server(servers[i].url);
            if (servers[i].username) server.username = servers[i].username;
            if (servers[i].password) server.password = servers[i].password;
            config.iceServers.push_back(std::move(server));
        }
        auto peer = std::make_unique<Peer>();
        peer->pc = std::make_shared<rtc::PeerConnection>(config);
        peer->pc->onLocalDescription([=](rtc::Description description) { callback(token, 1, std::string(description).c_str(), ""); });
        peer->pc->onLocalCandidate([=](rtc::Candidate candidate) { callback(token, 2, std::string(candidate).c_str(), candidate.mid().c_str()); });
        peer->pc->onStateChange([=](rtc::PeerConnection::State state) {
            if (state == rtc::PeerConnection::State::Connected) callback(token, 3, "connected", "");
            if (state == rtc::PeerConnection::State::Failed) callback(token, 3, "failed", "");
        });
        rtc::Description offer(sdp, "offer");
        for (int i = 0; i < offer.mediaCount(); ++i) {
            auto offered = offer.media(i);
            const auto entry = std::get_if<rtc::Description::Media *>(&offered);
            if (!entry) continue;
            auto media = (*entry)->reciprocate();
            const bool video = media.type() == "video";
            if (!video && media.type() != "audio") continue;
            int payload = -1;
            for (int pt : media.payloadTypes()) {
                const auto map = media.rtpMap(pt);
                const auto codec = lower(map->format);
                const bool profile2 = std::any_of(map->fmtps.begin(), map->fmtps.end(), [](const auto &p) {
                    return std::regex_search(p, std::regex(R"((^|;)\s*profile-id=2\s*(;|$))"));
                });
                if ((video && codec == (hdr ? "vp9" : "vp8") && (!hdr || profile2)) ||
                    (!video && codec == "opus" && map->clockRate == 48000)) { payload = pt; break; }
            }
            if (payload < 0) throw std::runtime_error(video && hdr ? "hdr-unavailable" : "codec-unavailable");
            for (int pt : media.payloadTypes()) if (pt != payload) media.removeRtpMap(pt);
            media.setDirection(rtc::Description::Direction::SendOnly);
            media.clearSSRCs();
            const auto ssrc = nextSsrc();
            media.addSSRC(ssrc, "beutl-preview", "beutl-preview", video ? "video" : "audio");
            auto rtp = std::make_shared<rtc::RtpPacketizationConfig>(ssrc, "beutl-preview", uint8_t(payload), video ? 90000 : 48000);
            // Advertise only extensions whose values we actually send.
            for (int id : media.extIds()) {
                const auto uri = media.extMap(id)->uri;
                if (uri == "urn:ietf:params:rtp-hdrext:sdes:mid") { rtp->midId = id; rtp->mid = media.mid(); }
                else if (video && uri == "http://www.webrtc.org/experiments/rtp-hdrext/color-space") rtp->colorSpaceId = id;
                else media.removeExtMap(id);
            }
            if (video && hdr && !rtp->colorSpaceId) throw std::runtime_error("hdr-unavailable");
            rtp->colorPrimaries = hdr ? 9 : 1; rtp->colorTransfer = hdr ? 16 : 13;
            rtp->colorMatrix = hdr ? 9 : 1; rtp->colorRange = 1;
            auto track = peer->pc->addTrack(media);
            std::shared_ptr<rtc::RtpPacketizer> packetizer;
            if (video) packetizer = std::make_shared<PreviewPacketizer>(rtp, hdr != 0);
            else packetizer = std::make_shared<rtc::OpusRtpPacketizer>(rtp);
            packetizer->addToChain(std::make_shared<rtc::RtcpSrReporter>(rtp));
            packetizer->addToChain(std::make_shared<rtc::RtcpNackResponder>());
            if (video) packetizer->addToChain(std::make_shared<rtc::PliHandler>([=]() { callback(token, 4, "", ""); }));
            track->setMediaHandler(packetizer);
            if (video) { peer->video = track; peer->videoConfig = rtp; }
            else { peer->audio = track; peer->audioConfig = rtp; }
        }
        if (!peer->video || !peer->audio) throw std::runtime_error("codec-unavailable");
        rtc::DataChannelInit playbackInit;
        playbackInit.negotiated = true;
        playbackInit.id = 0;
        peer->playback = peer->pc->createDataChannel("beutl-playback", playbackInit);
        peer->playback->onOpen([=]() { callback(token, 5, "", ""); });
        // This channel only publishes state; controls use the authenticated signaling path.
        peer->playback->onMessage([](rtc::message_variant) {});
        peer->pc->setRemoteDescription(offer);
        peer->pc->setLocalDescription(rtc::Description::Type::Answer);
        return peer.release();
    } catch (const std::exception &e) { lastError = e.what(); return nullptr; }
}
API int beutl_rtc_candidate(Peer *peer, const char *candidate, const char *mid) {
    try { peer->pc->addRemoteCandidate(rtc::Candidate(candidate, mid)); return 0; }
    catch (const std::exception &e) { lastError = e.what(); return -1; }
}
API int beutl_rtc_send(Peer *peer, int video, const uint8_t *data, int size, uint32_t timestamp) {
    try {
        const auto track = video ? peer->video : peer->audio;
        if (!track || !track->isOpen()) return 0;
        const auto config = video ? peer->videoConfig : peer->audioConfig;
        config->timestamp = config->startTimestamp + timestamp;
        track->send(reinterpret_cast<const std::byte *>(data), size);
        return 0;
    } catch (const std::exception &e) { lastError = e.what(); return -1; }
}
API void beutl_rtc_delete(Peer *peer) { delete peer; }
API int beutl_rtc_playback(Peer *peer, const char *state) {
    try {
        if (peer->playback->isOpen()) peer->playback->send(std::string(state));
        return 0;
    } catch (const std::exception &e) { lastError = e.what(); return -1; }
}

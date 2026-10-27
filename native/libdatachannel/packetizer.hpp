#pragma once
#include <rtc/rtc.hpp>
#include <algorithm>

// libdatachannel provides RTP/RTCP, but not VP8/VP9 payload fragmentation.
// RFC 7741 (VP8) and RFC 9628 (VP9): one spatial/temporal layer, 15-bit picture IDs.
class PreviewPacketizer : public rtc::RtpPacketizer {
    bool vp9;
    uint16_t pictureId = 0;
public:
    PreviewPacketizer(std::shared_ptr<rtc::RtpPacketizationConfig> config, bool vp9)
        : RtpPacketizer(std::move(config)), vp9(vp9) {}

    std::vector<rtc::binary> fragment(rtc::binary data) override {
        if (data.empty()) return {};
        // VP9 profile 2: frame marker (2), profile (2), show_existing_frame, frame_type.
        const auto header = std::to_integer<uint8_t>(data[0]);
        const bool predicted = vp9 && (header & 0x0c) != 0;
        const uint16_t id = pictureId++ & 0x7fff;
        std::vector<rtc::binary> packets;
        for (size_t offset = 0; offset < data.size();) {
            const auto count = std::min<size_t>(1100, data.size() - offset);
            const bool first = offset == 0, last = offset + count == data.size();
            rtc::binary packet;
            auto byte = [&](uint8_t value) { packet.push_back(std::byte(value)); };
            if (vp9) {
                // Flexible mode carries P_DIFF=1 for inter frames; key frames start a new chain.
                byte(0x90 | (predicted ? 0x40 : 0) | (first ? 0x08 : 0) | (last ? 0x04 : 0) |
                     (first && !predicted ? 0x02 : 0));
                byte(0x80 | (id >> 8)); byte(id & 0xff);
                if (predicted) byte(2); // P_DIFF=1, N=0
                if (first && !predicted) byte(0); // one spatial layer, no resolutions/GOF
            } else {
                byte(first ? 0x90 : 0x80); // X, S (partition 0)
                byte(0x80); // I: picture ID present
                byte(0x80 | (id >> 8)); byte(id & 0xff);
            }
            packet.insert(packet.end(), data.begin() + offset, data.begin() + offset + count);
            packets.push_back(std::move(packet));
            offset += count;
        }
        return packets;
    }
};

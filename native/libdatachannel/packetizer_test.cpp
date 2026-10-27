#include "packetizer.hpp"
#include <iostream>
#include <stdexcept>
static void require(bool value, const char *message) { if (!value) throw std::runtime_error(message); }
int main() try {
    for (bool vp9 : {false, true}) {
        auto config = std::make_shared<rtc::RtpPacketizationConfig>(42, "preview", 96, 90000);
        config->sequenceNumber = 65535;
        config->timestamp = 12345;
        PreviewPacketizer packetizer(config, vp9);
        for (bool predicted : {false, true}) {
            rtc::binary frame(3200, std::byte{0x80});
            frame[0] = vp9 ? (predicted ? std::byte{0x94} : std::byte{0x90}) : std::byte{0};
            auto packets = packetizer.fragment(frame);
            require(packets.size() == 3, "video frame fragmentation");
            rtc::binary recovered;
            for (size_t i = 0; i < packets.size(); ++i) {
                const auto flags = std::to_integer<uint8_t>(packets[i][0]);
                size_t header = 4;
                if (vp9) {
                    require(bool(flags & 8) == (i == 0), "VP9 B flag");
                    require(bool(flags & 4) == (i + 1 == packets.size()), "VP9 E flag");
                    require(bool(flags & 0x40) == predicted, "VP9 inter-frame prediction");
                    if (!predicted && i != 0) header = 3;
                    if (predicted) require(packets[i][3] == std::byte{2}, "VP9 dependency distance");
                } else require(bool(flags & 0x10) == (i == 0), "VP8 partition start");
                recovered.insert(recovered.end(), packets[i].begin() + header, packets[i].end());
            }
            require(recovered == frame, "lossless reassembly");
        }
        // Exercise real RTP headers, including the formerly truncated HDR extension ID.
        config->colorSpaceId = 20;
        config->colorPrimaries = 9; config->colorTransfer = 16; config->colorMatrix = 9; config->colorRange = 1;
        rtc::message_vector messages{rtc::make_message(rtc::binary(2400, std::byte{0x90}))};
        packetizer.outgoing(messages, [](rtc::message_ptr) {});
        require(messages.size() == 3, "RTP packet count");
        for (size_t i = 0; i < messages.size(); ++i) {
            const auto *data = reinterpret_cast<const uint8_t *>(messages[i]->data());
            require(bool(data[1] & 0x80) == (i == 2), "last packet marker");
            require(uint16_t((data[2] << 8) | data[3]) == uint16_t(65535 + i), "sequence wraps");
            require(data[12] == 0x10 && data[13] == 0, "two-byte header extension");
            require(data[16] == 20 && data[17] == 4, "color extension ID and length");
            require(data[18] == 9 && data[19] == 16 && data[20] == 9 && data[21] == 0x10, "BT.2020 PQ limited range");
        }
    }
    std::cout << "VP8/VP9 fragmentation and HDR RTP metadata passed\n";
    return 0;
} catch (const std::exception &e) { std::cerr << e.what() << '\n'; return 1; }

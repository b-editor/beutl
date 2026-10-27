# Preview WebRTC transport

This small subprocess uses [Pion WebRTC](https://github.com/pion/webrtc) (MIT) for
ICE, DTLS/SRTP, RTP packetization and RTCP feedback. It accepts encoded media from
the desktop through private standard-I/O pipes; it does not render scenes or load
FFmpeg. There is no SIPSorcery dependency.

Install Go (the minimum version is in `go.mod`) alongside the existing .NET tools.
The application's `PreviewTransport.targets` cross-compiles a pure Go executable for
its RID and copies it and `THIRD-PARTY-NOTICES.txt` into build/publish output. macOS
bundle signing includes it with the other executables. Run `go test ./...` here to
verify SDR/HDR negotiation. The pinned dependencies and their licenses are recorded
in `go.mod`, `go.sum` and `THIRD-PARTY-NOTICES.txt`; update the notices with dependency
changes.

Messages are newline-delimited JSON. The parent serializes writes and consumes all
worker output. Each `offer`/`candidate`/`remove` identifies one paired viewer; at most
four peers exist. `video` carries a VP8 or VP9 Profile 2 frame and a 90 kHz timestamp;
`audio` carries a stereo Opus packet and a 48 kHz timestamp. Binary payloads use JSON
base64. Both clocks originate from the same desktop monotonic clock. Worker output
contains the viewer ID and an answer, ICE candidate, state, error or keyframe request.
Closing stdin ends the worker and its peers.

HDR requires VP9 Profile 2 and the negotiated color-space RTP extension. Packetization
includes BT.2020/PQ/limited-range metadata on the last packet of each HDR frame.
The desktop keeps FFmpeg encoding in the separate GPL worker, converting its floating-
point clean frame to 10-bit PQ without an SDR intermediate. SDR viewers retain the
editor's tone mapping. No microphone, screen or system-audio capture is used.

Internet rooms get short-lived ICE credentials from the Beutl Web Cloudflare Worker.
The paired browser exchanges signaling over its authenticated WebSocket; media goes
through WebRTC directly or via TURN. See Beutl Web's `docs/mobile-preview.md` for
Cloudflare configuration, browser capability fallback and device verification.

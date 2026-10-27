# Preview transport

Beutl calls a small C ABI over libdatachannel 0.24.6 for WebRTC. Encoded VP8/VP9
and Opus buffers are passed directly from .NET. `preview.cpp` handles track
negotiation; `packetizer.hpp` adds the VP8/VP9 payload fragmentation absent from
upstream. libdatachannel owns ICE, DTLS/SRTP, RTP headers, RTCP reports, NACK and PLI.
Mbed TLS and the upstream submodules are linked statically into one native library.

`source.json` pins source commits; `runtimes` contains prebuilt libraries, source
pins and SHA-256 manifests. `THIRD-PARTY-NOTICES.txt` contains the bundled licenses.
Normal .NET builds do not download or compile native code. macOS is universal;
Linux and Windows have x64/ARM64 builds.
To rebuild, install Python, CMake 4.4.4, Ninja 1.13.2 and the platform C/C++ compiler:

```sh
python3 native/libdatachannel/build.py --rid osx
```

Use `linux-x64`, `linux-arm64`, `win-x64` or `win-arm64` on the corresponding
platform (an ARM64 MSVC developer shell for Windows cross-compilation).
`.github/workflows/build-preview-native.yml` builds all targets and runs the RTP
packetizer tests. Replace the checked-in runtime directory with its matching
artifact, then review the manifest and notices together with any source changes.

The sole upstream patch fixes two-byte RTP extension IDs for HDR color metadata.
Its regression is covered by `packetizer_test.cpp`. Source is available at the
repositories/commits in `source.json`; changes to MPL-covered code remain MPL-2.0.

The default libjuice ICE backend supports UDP TURN, not TCP/TLS TURN. The desktop
requires outbound UDP; browsers may also use their native TCP/TLS TURN support.
Cloudflare signaling and the Next.js receiver belong in Beutl Web. No Web page,
permanent TURN credential, Go runtime or SIPSorcery dependency is embedded here.

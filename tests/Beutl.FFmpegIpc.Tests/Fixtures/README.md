# Media Foundation fallback fixture

`mf-unsupported.flv` contains the 64 x 64 H.264 video from
`tests/Beutl.Extensions.MediaFoundation.Tests/Fixtures/sample.mp4`, remuxed into FLV
without audio. Tests copy it to Media Foundation file extensions to reproduce an
unsupported byte stream while keeping the content decodable by FFmpeg.

The fixture is checked in so tests do not need an FFmpeg CLI installation.
Regenerate it from the repository root with:

```powershell
ffmpeg -nostdin -i tests/Beutl.Extensions.MediaFoundation.Tests/Fixtures/sample.mp4 -map 0:v:0 -c copy -an -f flv tests/Beutl.FFmpegIpc.Tests/Fixtures/mf-unsupported.flv
```

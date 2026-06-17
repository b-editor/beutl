# Test fixtures

## `sample.mp4`

A tiny, synthetic H.264 + AAC clip used by `MFReaderIntegrationTests` to exercise the real
Media Foundation video/audio decode path.

- Video: H.264 (baseline), 64×64, 10 fps, ~0.6 s
- Audio: AAC, 44100 Hz, stereo

Regenerate with ffmpeg:

```sh
ffmpeg -y \
  -f lavfi -i "testsrc=size=64x64:rate=10:duration=0.6" \
  -f lavfi -i "sine=frequency=440:duration=0.6:sample_rate=44100" \
  -c:v libx264 -pix_fmt yuv420p -profile:v baseline -level 3.0 \
  -c:a aac -b:a 64k -ac 2 -shortest \
  -movflags +faststart \
  sample.mp4
```

The content is generated test signal (no third-party assets), so it carries no extra licensing.

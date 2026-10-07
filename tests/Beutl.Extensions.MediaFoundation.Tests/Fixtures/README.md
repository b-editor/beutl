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

## `marker-bframes.mp4` and `marker-bframes-no-edit.mp4`

Four-second H.264 + AAC synchronization fixtures (64×64, 10 fps, 48000 Hz stereo).
The picture changes from red to blue at 1 s. The tone changes from 440 to 880 Hz
at the same point, then to 1320 and 1760 Hz at 2 and 3 s. Both files expose a
200 ms first video PTS through Media Foundation. The ordinary MP4 has an edit
list that removes the video composition offset; the second file has no edit
list and needs the audio seek to add that offset. `MFAudioVideoSyncTests` checks
decoded picture/audio content, backward seeks, duration, and normalized EOF
in both Audio and AudioVideo modes. Tests use the committed fixtures and do
not require ffmpeg on the Windows runner.

Regenerate with ffmpeg:

```sh
ffmpeg -y \
  -f lavfi -i "color=red:s=64x64:r=10:d=4,drawbox=c=blue:t=fill:enable='gte(t,1)'" \
  -f lavfi -i 'aevalsrc=0.3*sin(2*PI*440*(1+floor(t))*t):s=48000:d=4' \
  -c:v libx264 -pix_fmt yuv420p -g 10 -bf 3 \
  -c:a aac -b:a 128k -ac 2 -movflags +faststart marker-bframes.mp4

ffmpeg -y -i marker-bframes.mp4 -c copy -use_editlist 0 marker-bframes-no-edit.mp4
```

These fixtures also contain only generated test signals.

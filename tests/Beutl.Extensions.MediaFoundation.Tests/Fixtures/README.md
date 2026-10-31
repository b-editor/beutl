# Test fixtures

## `sample.mp4`

A tiny, synthetic H.264 + AAC clip used by `MFReaderIntegrationTests` to exercise the real
Media Foundation video/audio decode path.
`MFStreamProbeTests` also reads it, and `tests/Beutl.FFmpegIpc.Tests/Fixtures/mf-unsupported.flv` is remuxed from it.

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

## `late-audio.mp4`

A 2.5-second H.264 + AAC clip (16×16, 10 fps, 8000 Hz mono) whose audio starts
0.37 s after the container start (delayed 0.5 s, less the AAC priming frame).
Its tone changes from 440 to 880 Hz at 1.5 s. `FFmpegReaderAudioOriginTests` in
Beutl.FFmpegWorker.Tests links it, with `marker-bframes.mp4` and
`marker-bframes-no-edit.mp4`.

Regenerate with ffmpeg:

```sh
ffmpeg -y -f lavfi -i "color=red:s=16x16:r=10:d=2.5" -itsoffset 0.5 \
  -f lavfi -i "aevalsrc=0.3*sin(2*PI*440*(1+gte(t\,1))*t):s=8000:d=2" \
  -c:v libx264 -pix_fmt yuv420p -bf 0 -g 10 \
  -c:a aac -b:a 32k -ac 1 -movflags +faststart late-audio.mp4
```

## `marker-bframes.mp4` and `marker-bframes-no-edit.mp4`

Four-second H.264 + AAC synchronization fixtures (64×64, 10 fps, 48000 Hz stereo).
The picture changes from red to blue at 1 s. The tone changes from 440 to 880 Hz
at the same point, then to 1320 and 1760 Hz at 2 and 3 s. Both files expose a
200 ms first video PTS through Media Foundation. The ordinary MP4 has an edit
list that removes the video composition offset; the second file has no edit
list, so the content of both its streams starts 0.2 s after the container
start. `MFAudioVideoSyncTests` checks decoded picture/audio content, backward
seeks, duration, and EOF in both Audio and AudioVideo modes. Tests use the
committed fixtures and do not require ffmpeg on the Windows runner.
`AVFReaderSeekTests` in Beutl.Extensions.AVFoundation.Tests links `marker-bframes.mp4`.

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

## `surround51-tones.m4a`

A 1.5-second AAC 5.1 clip (48000 Hz), linked by `FFmpegReaderAudioConversionTests`.
Each WAVE speaker position carries a distinct sine: FL 400 Hz, FR 600 Hz,
FC 800 Hz, LFE 80 Hz, BL 1000 Hz, BR 1200 Hz. The LFE tone stays below the
AAC encoder's LFE low-pass cutoff. This is generated test signal only.

Regenerate with ffmpeg (generated with FFmpeg 8.1.3):

```sh
ffmpeg -y -f lavfi \
  -i 'aevalsrc=0.2*sin(2*PI*400*t)|0.2*sin(2*PI*600*t)|0.2*sin(2*PI*800*t)|0.2*sin(2*PI*80*t)|0.2*sin(2*PI*1000*t)|0.2*sin(2*PI*1200*t):s=48000:d=1.5:c=5.1' \
  -c:a aac -b:a 384k -movflags +faststart surround51-tones.m4a
```

## `late-video.mp4`

A 2.5-second H.264 + AAC clip (64×64, 10 fps, 8000 Hz mono) whose video starts
0.5 s after its audio, through an MP4 empty edit. The picture changes from red
to blue and the tone from 440 to 880 Hz together at 1.5 s. Media Foundation did
not open a 16×16 version. `MFAudioVideoSyncTests` reads it, and
`FFmpegReaderFrameOriginTests` in Beutl.FFmpegWorker.Tests links it with
`marker-bframes-no-edit.mp4`.

Regenerate with ffmpeg:

```sh
ffmpeg -y -itsoffset 0.5 \
  -f lavfi -i "color=red:s=64x64:r=10:d=2,drawbox=c=blue:t=fill:enable='gte(t,1)'" \
  -f lavfi -i "aevalsrc=0.3*sin(2*PI*440*(1+gte(t\,1.5))*t):s=8000:d=2.5" \
  -c:v libx264 -pix_fmt yuv420p -bf 0 -g 10 \
  -c:a aac -b:a 32k -ac 1 -movflags +faststart late-video.mp4
```

# Media Foundation fallback fixture

`mf-unsupported.flv` contains the 64 x 64 H.264 video from
`tests/Beutl.Extensions.MediaFoundation.Tests/Fixtures/sample.mp4`, remuxed into FLV
without audio. Tests copy it to Media Foundation file extensions to reproduce an
unsupported byte stream while keeping the content decodable by FFmpeg.
`FFmpegReaderProxySuspendContractTests` and `DecodingHandlerCloseRaceTests` in `Beutl.FFmpegWorker.Tests`, which links it, also use it as a small video that FFmpeg can read.

The fixture is checked in so tests do not need an FFmpeg CLI installation.
Regenerate it from the repository root with:

```powershell
ffmpeg -nostdin -i tests/Beutl.Extensions.MediaFoundation.Tests/Fixtures/sample.mp4 -map 0:v:0 -c copy -an -f flv tests/Beutl.FFmpegIpc.Tests/Fixtures/mf-unsupported.flv
```

## Interlaced motion

`deinterlace-tff.mp4` and `deinterlace-bff.mp4` contain a bar that advances four pixels per field.
They each have 120 interlaced frames at 30000/1001 fps, reordered B frames, and a 4.004 s mono audio track.
Run in this directory with FFmpeg 8.1 once with `order=top parity=tff` and once with `order=bottom parity=bff`.

```sh
ffmpeg -y -nostdin -f lavfi \
  -i "color=c=black:s=192x96:r=60000/1001:d=4.004,geq=lum='if(between(X,8+mod(4*N,156),28+mod(4*N,156)),220,16)':cb=128:cr=128,tinterlace=mode=interleave_$order" \
  -f lavfi -i 'sine=frequency=440:sample_rate=48000:duration=4.004' \
  -c:v libx264 -pix_fmt yuv420p -flags +ilme+ildct -crf 13 \
  -x264-params "$parity=1:interlaced=1:keyint=18:min-keyint=18:scenecut=0:bframes=3:b-adapt=0" \
  -c:a aac "deinterlace-$parity.mp4"
```

`deinterlace-offset-tff.mp4` remuxes a short excerpt with video starting 20 ms after the audio.
Its end falls at 29 frames on the field-rate grid, compared with 15 on the source-rate grid.

```sh
ffmpeg -y -nostdin -itsoffset 0.020 -i deinterlace-tff.mp4 -i deinterlace-tff.mp4 \
  -map 0:v:0 -map 1:a:0 -c copy -t 0.41 deinterlace-offset-tff.mp4
```

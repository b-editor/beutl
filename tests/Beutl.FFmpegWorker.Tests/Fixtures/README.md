# Test fixtures

Frame timing fixtures for `FFmpegReaderVariableFrameRateTests`; the AVFoundation tests' `AVFReaderVariableFrameRateTests`
also reads the two variable frame rate clips. Each H.264 clip (64×32) shows the index of its frame:
the left half has luma 16 + 10 × (index mod 20) and the right half 16 + 10 × (index / 20). The content is generated
test signal only, so the files carry no extra licensing. They were generated with FFmpeg 8.1.

## `vfr-30-60-30.mp4`

Variable frame rate: 105 frames at 30 fps, 90 at 60 fps, then 105 at 30 fps, so its 300 frames span 8.5 s at an
average of 600/17 fps. B frames make seeks skip non-reference pictures near the rate changes.

```sh
ffmpeg -y -f lavfi -i "color=c=black:s=64x32:r=30:d=11,format=yuv420p,geq=lum='16+10*if(lt(X,32),mod(N,20),floor(N/20))':cb=128:cr=128,settb=1/600,setpts='if(lt(N,105),N*20,if(lt(N,195),2100+(N-105)*10,3000+(N-195)*20))'" \
  -frames:v 300 -fps_mode passthrough -enc_time_base filter \
  -c:v libx264 -pix_fmt yuv420p -crf 12 -x264-params keyint=48:min-keyint=48:scenecut=0:bframes=3:b-adapt=0 \
  vfr-30-60-30.mp4
```

## `vfr-screen.mp4`

A screen recording's timing: a still held for 1 s, then bursts of 40 frames at 60 fps, 30 at 30 fps, 60 at 120 fps
and 50 at 60 fps, with holds between them. Its 181 frames average 10860/539 fps, and ffmpeg's `fps` filter presents
them at that rate as 161 frames, since a burst puts several frames on one number.

```sh
ffmpeg -y -f lavfi -i "color=c=black:s=64x32:r=60:d=4,format=yuv420p,geq=lum='16+10*if(lt(X,32),mod(N,20),floor(N/20))':cb=128:cr=128,settb=1/600,setpts='if(lt(N,1),0,if(lt(N,41),600+(N-1)*10,if(lt(N,71),1900+(N-41)*20,if(lt(N,131),3700+(N-71)*5,4300+(N-131)*10))))'" \
  -frames:v 181 -fps_mode passthrough -enc_time_base filter \
  -c:v libx264 -pix_fmt yuv420p -crf 12 -x264-params keyint=48:min-keyint=48:scenecut=0:bframes=3:b-adapt=0 \
  vfr-screen.mp4
```

## `edit-list-trim.mp4`

A 3 s, 10 fps clip cut at 0.5 s without re-encoding. Its only keyframe is the first frame, so the file keeps all 30
samples and its edit list hides the first 5, leaving 25 frames.

```sh
ffmpeg -y -f lavfi -i "color=c=black:s=64x32:r=10:d=4,format=yuv420p,geq=lum='16+10*if(lt(X,32),mod(N,20),floor(N/20))':cb=128:cr=128" \
  -frames:v 30 -c:v libx264 -pix_fmt yuv420p -crf 12 \
  -x264-params keyint=30:min-keyint=30:scenecut=0:bframes=3:b-adapt=0 trim-source.mp4
ffmpeg -y -ss 0.5 -i trim-source.mp4 -c copy edit-list-trim.mp4
```

## `deinterlace-mixed.mkv`

An FFV1 clip with six progressive frames, six TFF frames, then four progressive frames. The
progressive frames have alternating scanline detail. Remuxing sets a TFF container header
while keeping each frame's original scan flags.

```sh
ffmpeg -y -nostdin -f lavfi \
  -i "color=c=black:s=96x48:r=25:d=0.64,geq=lum='if(between(X,8+4*N+4*mod(Y,2)*between(N,6,11),22+4*N+4*mod(Y,2)*between(N,6,11)),if(between(N,6,11),220,220-75*mod(Y,2)),16)':cb=128:cr=128" \
  -filter_complex '[0:v]split=3[a][b][c];[a]trim=end_frame=6,setfield=prog[p];[b]trim=start_frame=6:end_frame=12,setpts=PTS-STARTPTS,setfield=tff[i];[c]trim=start_frame=12,setpts=PTS-STARTPTS,setfield=prog[q];[p][i][q]concat=n=3:v=1:a=0,setpts=N/(25*TB)' \
  -r 25 -c:v ffv1 -level 3 deinterlace-mixed-frames.mkv
ffmpeg -y -nostdin -i deinterlace-mixed-frames.mkv -map 0:v:0 -c copy -field_order tt deinterlace-mixed.mkv
```

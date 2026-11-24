using Beutl.Media.Decoding;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Decoding;

public sealed partial class FFmpegReader
{
    private Queue<MediaFrame>? _videoProbeFrames;
    private VideoDeinterlaceOptions _deinterlace;
    private VideoFieldOrder _streamFieldOrder;
    private MediaDemuxer? _deinterlaceAudioDemuxer;
    private bool _filterHasInput;
    private bool _filterInputClosed;
    private long _firstInputPts;
    private long _nextInputPts;
    private long _nominalInputDuration;
    private double _videoOrigin;
    private MediaFrame? _filteredNextFrame;
    private MediaFrame? _filteredShownFrame;
    // Null means the lookahead must be rebuilt. False means the filter reached EOF.
    private bool? _filteredNextReady;
    private double _filteredTimeBase;
    private long _filteredNextNumber;
    private long _filteredNextEnd;
    private long _filteredShownNumber = long.MinValue;
    private long _filteredShownEnd;
    private long _previousFilteredPts = ffmpeg.AV_NOPTS_VALUE;
    private long _previousFilteredNumber;

    private unsafe VideoFieldOrder ProbeFieldOrder(VideoDeinterlaceOptions options)
    {
        _streamFieldOrder = _videoStream!.CodecparRef.field_order switch
        {
            AVFieldOrder.AV_FIELD_PROGRESSIVE => VideoFieldOrder.Progressive,
            AVFieldOrder.AV_FIELD_TT or AVFieldOrder.AV_FIELD_BT => VideoFieldOrder.TopFieldFirst,
            AVFieldOrder.AV_FIELD_BB or AVFieldOrder.AV_FIELD_TB => VideoFieldOrder.BottomFieldFirst,
            _ => VideoFieldOrder.Unknown,
        };

        if (!IsUsableVideoFrame(ActiveVideoFrame))
            return _streamFieldOrder;

        VideoFieldOrder observed = GetFrameFieldOrder(ActiveVideoFrame);
        if (options.Mode != DeinterlaceMode.Off)
        {
            // Report the scan order observed near the start. Auto still checks flags throughout the stream.
            // Replay the sample from the decoder's original position, including on inputs with sparse seek tables.
            using var first = ActiveVideoFrame.Clone();
            _videoProbeFrames = new Queue<MediaFrame>(3);
            for (int i = 0; i < 3 && DecodeNextVideoFrame(); i++)
            {
                if (!IsUsableVideoFrame(ActiveVideoFrame))
                    continue;
                _videoProbeFrames.Enqueue(ActiveVideoFrame.Clone());
                if (observed != GetFrameFieldOrder(ActiveVideoFrame))
                    observed = VideoFieldOrder.Mixed;
            }
            MoveVideoFrame(first, ActiveVideoFrame);
            _videoNowFrame = _firstVideoFrame;
        }

        return observed;
    }

    private static unsafe VideoFieldOrder GetFrameFieldOrder(AVFrame* frame)
        => (frame->flags & ffmpeg.AV_FRAME_FLAG_INTERLACED) == 0 ? VideoFieldOrder.Progressive
            : (frame->flags & ffmpeg.AV_FRAME_FLAG_TOP_FIELD_FIRST) != 0 ? VideoFieldOrder.TopFieldFirst
            : VideoFieldOrder.BottomFieldFirst;

    private static unsafe void MoveVideoFrame(MediaFrame source, MediaFrame destination)
    {
        destination.Unref();
        ffmpeg.av_frame_move_ref(destination, source);
    }

    private void ClearVideoProbeFrames()
    {
        if (_videoProbeFrames == null) return;
        while (_videoProbeFrames.TryDequeue(out var frame))
            frame.Dispose();
        _videoProbeFrames = null;
    }

    private unsafe DeinterlaceApplicationStatus ConfigureDeinterlacing(string file, VideoDeinterlaceOptions options)
    {
        if (options.Mode == DeinterlaceMode.Off)
            return DeinterlaceApplicationStatus.Off;
        if (!IsUsableVideoFrame(ActiveVideoFrame)
            || !double.IsFinite(_videoAvgFrameRateDouble) || _videoAvgFrameRateDouble <= 0)
        {
            return DeinterlaceApplicationStatus.UnsupportedByBackend;
        }

        // The filter passes progressive frames through in Auto. Configure the output rate at
        // open so later interlaced frames, seeks, and proxy generation stay on the same grid.
        _deinterlace = options;
        long origin = _demuxer!.StartTime == ffmpeg.AV_NOPTS_VALUE ? 0 : _demuxer.StartTime;
        _videoOrigin = origin / (double)ffmpeg.AV_TIME_BASE;
        _nominalInputDuration = Math.Max(1, ffmpeg.av_rescale_q(1,
            ffmpeg.av_inv_q(_videoStream!.AvgFrameRate), _videoStream.TimeBase));
        _firstInputPts = GetInputPts(ActiveVideoFrame);
        if (_firstInputPts == ffmpeg.AV_NOPTS_VALUE)
            _firstInputPts = ffmpeg.av_rescale_q(origin, s_time_base, _videoStream.TimeBase);

        _nextInputPts = _firstInputPts;
        _filterHasInput = true;
        InitFilterGraph(ActiveVideoFrame);

        // Audio seeks must not reposition demuxing underneath the temporal video filter.
        if (HasAudio)
            _deinterlaceAudioDemuxer = MediaDemuxer.Open(file);

        return DeinterlaceApplicationStatus.Applied;
    }

    private bool MatchesFilterInput(MediaFrame frame)
        => _filterGraph != null && _filterWidth == frame.Width && _filterHeight == frame.Height
            && _filterSrcPixFmt == (AVPixelFormat)frame.Format;

    private MediaFilterContext AddDeinterlaceFilter(MediaFilterContext input)
    {
        string method = _deinterlace.Method == DeinterlaceMethod.Yadif ? "yadif" : "bwdif";
        string mode = _deinterlace.FrameRateMode == DeinterlaceFrameRateMode.Field ? "send_field" : "send_frame";
        string parity = _deinterlace.FieldOrder switch
        {
            DeinterlaceFieldOrder.TopFieldFirst => "tff",
            DeinterlaceFieldOrder.BottomFieldFirst => "bff",
            _ => "auto",
        };
        string deint = _deinterlace.Mode == DeinterlaceMode.On ? "all" : "interlaced";
        return input.LinkTo(0, _filterGraph!.AddFilter(new MediaFilter(method), $"mode={mode}:parity={parity}:deint={deint}"));
    }

    private static unsafe long GetInputPts(AVFrame* frame)
        => frame->pts == ffmpeg.AV_NOPTS_VALUE ? frame->best_effort_timestamp : frame->pts;

    private unsafe void FeedFilter(MediaFrame input)
    {
        AVFrame* frame = input;
        long pts = GetInputPts(frame);
        frame->pts = pts == ffmpeg.AV_NOPTS_VALUE ? _nextInputPts : pts;
        if (frame->duration <= 0) frame->duration = _nominalInputDuration;
        _nextInputPts = frame->pts + frame->duration;

        // In forced mode the stream order supplies parity for unflagged frames. Auto passes
        // the decoded scan flags straight to the filter for each frame.
        bool useStreamParity = _deinterlace.Mode == DeinterlaceMode.On
            && _deinterlace.FieldOrder == DeinterlaceFieldOrder.Auto && _streamFieldOrder == VideoFieldOrder.BottomFieldFirst;
        if (useStreamParity && (frame->flags & ffmpeg.AV_FRAME_FLAG_INTERLACED) == 0)
        {
            frame->flags |= ffmpeg.AV_FRAME_FLAG_INTERLACED;
            frame->flags &= ~ffmpeg.AV_FRAME_FLAG_TOP_FIELD_FIRST;
        }

        _bufferSrcCtx!.WriteFrame(input, KeepFrameReference);
        _filterHasInput = false;
    }

    private bool PumpFilteredFrame(MediaFrame output)
    {
        output.Unref();
        while (true)
        {
            if (_bufferSinkCtx is { } sink)
            {
                int result = sink.GetFrame(output);
                if (result >= 0) return true;
                if (result == ffmpeg.AVERROR_EOF)
                {
                    if (!_filterHasInput) return false;
                    ResetVideoFilter();
                }
                else if (result != ffmpeg.AVERROR(ffmpeg.EAGAIN) || _filterInputClosed)
                {
                    result.ThrowIfError();
                }
            }

            while (!_filterHasInput && GrabVideo())
                _filterHasInput = IsUsableVideoFrame(ActiveVideoFrame);

            if (!_filterHasInput || (_filterGraph != null && !MatchesFilterInput(ActiveVideoFrame!)))
            {
                if (_bufferSrcCtx == null) return false;

                // Drain at decoder EOF and before changing input geometry. A frame from the
                // new geometry waits here until the old graph outputs its final field.
                _bufferSrcCtx.WriteFrame(null, 0);
                _filterInputClosed = true;
                continue;
            }

            var input = ActiveVideoFrame!;
            InitFilterGraph(input);
            FeedFilter(input);
        }
    }

    private double OutputFrameRate => VideoInfo.FrameRate.ToDouble();

    private long GetDeinterlacedFrameNumber(long pts, double timeBase, double frameRate)
        => (long)Math.Round((pts * timeBase - _videoOrigin) * frameRate, MidpointRounding.AwayFromZero);

    private long GetDeinterlacedFrameCount(double frameRate, double duration)
    {
        // Measure the end on the output grid. Rounding the original frame count first loses
        // sub-frame offsets between the video stream and the container start at field rate.
        if (_videoStream!.StartTime != ffmpeg.AV_NOPTS_VALUE && _videoStream.Duration > 0)
            return GetDeinterlacedFrameNumber(_videoStream.StartTime + _videoStream.Duration, _videoTimeBaseDouble, frameRate);

        if (_videoStream.NbFrames > 0)
        {
            long first = GetDeinterlacedFrameNumber(_firstInputPts, _videoTimeBaseDouble, frameRate);
            int outputsPerFrame = _deinterlace.FrameRateMode == DeinterlaceFrameRateMode.Field ? 2 : 1;
            return first + _videoStream.NbFrames * outputsPerFrame;
        }

        return (long)Math.Round(duration * frameRate, MidpointRounding.AwayFromZero);
    }

    private void GrabFilteredFrame()
    {
        _filteredNextReady = null;
        if (!PumpFilteredFrame(_filteredNextFrame!))
        {
            _filteredNextReady = false;
            return;
        }

        var output = _filteredNextFrame!;
        long number = GetDeinterlacedFrameNumber(output.Pts, _filteredTimeBase, OutputFrameRate);
        // A one-frame stream has no second PTS from which the filter can interpolate a field time.
        if (_deinterlace.FrameRateMode == DeinterlaceFrameRateMode.Field && output.Pts == _previousFilteredPts)
            number = Math.Max(number, _previousFilteredNumber + 1);
        _previousFilteredPts = output.Pts;
        _previousFilteredNumber = _filteredNextNumber = number;
        _filteredNextEnd = Math.Max(number + 1,
            GetDeinterlacedFrameNumber(output.Pts + output.Duration, _filteredTimeBase, OutputFrameRate));
        _filteredNextReady = true;
    }

    private unsafe MediaFrame? ReadDeinterlacedVideo(int frame)
    {
        long first = GetDeinterlacedFrameNumber(_firstInputPts, _videoTimeBaseDouble, OutputFrameRate);
        long target = Math.Max(first, frame);
        if (VideoInfo.NumFrames > 0 && target >= VideoInfo.NumFrames) return null;

        if (_filteredNextFrame == null)
        {
            _filteredNextFrame = new MediaFrame();
            _filteredShownFrame = new MediaFrame();
            GrabFilteredFrame();
        }
        if (_filteredNextReady is null || target < _filteredShownNumber
            || (_filteredNextReady == true && target - _filteredNextNumber > 0
                && FFmpegSeekDecision.ShouldReseek(true, target - _filteredNextNumber)))
        {
            SeekDeinterlacedVideo(target);
        }

        // Progressive frames in mixed input can cover both field ticks. Like the unfiltered
        // path, retain the most recent frame up to the timestamp of the next one.
        while (_filteredNextReady == true && (target >= _filteredNextNumber || !IsUsableVideoFrame(_filteredShownFrame)))
        {
            (_filteredShownFrame, _filteredNextFrame) = (_filteredNextFrame, _filteredShownFrame);
            _filteredShownNumber = _filteredNextNumber;
            _filteredShownEnd = _filteredNextEnd;
            GrabFilteredFrame();
        }

        if (!IsUsableVideoFrame(_filteredShownFrame) || (_filteredNextReady == false && target >= _filteredShownEnd))
            return null;

        _filterFrame ??= new MediaFrame();
        ffmpeg.av_frame_ref(_filterFrame, _filteredShownFrame).ThrowIfError();
        return _filterFrame;
    }

    private unsafe void SeekDeinterlacedVideo(long target)
    {
        _filteredNextReady = null;
        ResetVideoFilter();
        _filteredNextFrame!.Unref();
        _filteredShownFrame!.Unref();
        _filteredShownNumber = long.MinValue;

        // Two source frames of pre-roll provide the temporal neighbors for either output field.
        double time = target / OutputFrameRate - 2 / _videoAvgFrameRateDouble + _videoOrigin;
        long preroll = Math.Max(_firstInputPts, (long)Math.Floor(time / _videoTimeBaseDouble));
        SeekInput(preroll);
        if (preroll > _firstInputPts && (!_filterHasInput || GetInputPts(ActiveVideoFrame!) > preroll))
            SeekInput(_firstInputPts);

        GrabFilteredFrame();

        void SeekInput(long pts)
        {
            ffmpeg.avformat_seek_file(_demuxer, _videoStream!.Index, long.MinValue, pts, pts, 0).ThrowIfError();
            ffmpeg.avcodec_flush_buffers(_videoDecoder);
            ClearVideoProbeFrames();
            DiscardPendingVideoPacket();
            _videoDecodedPts = long.MinValue;
            ActiveVideoFrame!.Unref();
            _nextInputPts = pts;
            _filterHasInput = GrabVideo() && IsUsableVideoFrame(ActiveVideoFrame);
        }
    }

    private void ResetVideoFilter()
    {
        _filterGraph?.Dispose();
        _filterGraph = null;
        _bufferSinkCtx = _bufferSrcCtx = null;
        _filterInputClosed = false;
        _previousFilteredPts = ffmpeg.AV_NOPTS_VALUE;
    }
}

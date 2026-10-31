using System.Runtime.ExceptionServices;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.Media;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;
using Microsoft.Extensions.Logging;

namespace Beutl.FFmpegWorker.Encoding;

public class FFmpegEncodingController(string outputFile, FFmpegEncodingSettings settings)
    : EncodingController(outputFile)
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegEncodingController>();

    private bool _isHdr;
    private BitmapColorSpace? _targetColorSpace;
    private MediaFilterGraph? _filterGraph;
    private MediaFilterContext? _bufferSrcCtx;
    private MediaFilterContext? _bufferSinkCtx;

    private class EncodeState
    {
        public long NextPts { get; set; }
    }

    public override FFmpegVideoEncoderSettings VideoSettings { get; } = new() { OutputFile = outputFile };

    public override FFmpegAudioEncoderSettings AudioSettings { get; } = new() { OutputFile = outputFile };

    private void InitEncodeFilterGraph(MediaEncoder encoder)
    {
        _filterGraph = new MediaFilterGraph();

        var bufferSrc = new MediaFilter(MediaFilter.VideoSources.Buffer);
        var bufferSink = new MediaFilter(MediaFilter.VideoSinks.Buffersink);

        int width = VideoSettings.SourceSize.Value.Width;
        int height = VideoSettings.SourceSize.Value.Height;
        var srcPixFmt = _isHdr ? AVPixelFormat.AV_PIX_FMT_RGBA64LE : AVPixelFormat.AV_PIX_FMT_BGRA;
        var fps = VideoSettings.FrameRate.Value;
        // The bitmap is full-range RGB, even when the encoded output is limited-range YUV.
        _bufferSrcCtx = _filterGraph.AddVideoSrcFilter(
            bufferSrc, $"video_size={width}x{height}:pix_fmt={(int)srcPixFmt}"
                + $":time_base={fps.Denominator}/{fps.Numerator}:pixel_aspect=1/1"
                + $":frame_rate={fps.Numerator}/{fps.Denominator}:colorspace=gbr:range=pc");
        _bufferSinkCtx = _filterGraph.AddVideoSinkFilter(bufferSink, [encoder.PixFmt]);

        // An output of another aspect ratio gets the whole frame with black bars rather than a stretched one.
        (string scaleStr, string? padStr) = FrameFit.GetFilterArguments(
            VideoSettings.SourceSize.Value, VideoSettings.DestinationSize.Value);
        // Codec tags do not configure swscale. Use the same matrix/range for the pixels and the bitstream.
        scaleStr += $":in_range=pc:out_range={(int)encoder.ColorRange}";
        // swscale rejects matrices it cannot produce; those keep its default matrix, as before the tags were used.
        if (encoder.Colorspace != AVColorSpace.AVCOL_SPC_RGB && ffmpeg.sws_test_colorspace(encoder.Colorspace, 1) > 0)
            scaleStr += $":out_color_matrix={(int)encoder.Colorspace}";
        var scaleFilter = new MediaFilter("scale");
        var scaleCtx = _filterGraph.AddFilter(scaleFilter, scaleStr);

        var formatFilter = new MediaFilter("format");
        var formatStr = $"pix_fmts={ffmpeg.av_get_pix_fmt_name(encoder.PixFmt)}";
        var formatCtx = _filterGraph.AddFilter(formatFilter, formatStr);

        _bufferSrcCtx.LinkTo(0, scaleCtx);
        if (padStr != null)
        {
            var padCtx = _filterGraph.AddFilter(new MediaFilter("pad"), padStr);
            scaleCtx.LinkTo(0, padCtx);
            padCtx.LinkTo(0, formatCtx);
        }
        else
        {
            scaleCtx.LinkTo(0, formatCtx);
        }

        formatCtx.LinkTo(0, _bufferSinkCtx);

        _filterGraph.Initialize();
    }

    private void ConfigureAudioStream(
        OutputFormat outFormat, MediaMuxer muxer,
        MediaFrame audioFrame, out MediaEncoder encoder, out MediaStream stream, out SampleConverter swr)
    {
        var codec = AudioSettings.Codec.Value.Equals(CodecRecord.Default)
            ? MediaCodec.FindEncoder(outFormat.AudioCodec)
            : MediaCodec.FindEncoder(AudioSettings.Codec.Value.Name);
        AVChannelLayout channelLayout = SelectChannelLayout(codec, AudioSettings.Channels.Value);
        int sampleRate = AudioSettings.SampleRate.Value;
        var format = (AVSampleFormat)AudioSettings.Format.Value;
        int bitRate = AudioSettings.Bitrate.Value;
        encoder = MediaEncoder.Create(codec, codecContext =>
        {
            int[] supportedSampleRates = codec.GetSupportedSamplerates().ToArray();
            var supportedFmts = codec.GetSampelFmts().ToArray();

            if (channelLayout.nb_channels <= 0)
                throw new InvalidOperationException("Channels must be greater than 0");
            if (bitRate < 0)
                throw new InvalidOperationException("Bitrate must be greater than 0");
            _logger.LogInformation("Supported sample rates: {Rates}", string.Join(", ", supportedSampleRates));
            _logger.LogInformation("Supported sample formats: {Formats}", string.Join(", ", supportedFmts));

            if (sampleRate <= 0)
            {
                sampleRate = supportedSampleRates.FirstOrDefault(44100);
            }
            else if (supportedSampleRates.Length > 0 && supportedSampleRates.All(i => i != sampleRate))
            {
                throw new InvalidOperationException(
                    $"Invalid sample rate.\nSupported sample rates: {string.Join(", ", supportedSampleRates)}");
            }

            if (format == AVSampleFormat.AV_SAMPLE_FMT_NONE)
            {
                format = supportedFmts.FirstOrDefault(AVSampleFormat.AV_SAMPLE_FMT_S16);
            }
            else if (supportedFmts.Length > 0 && supportedFmts.All(i => i != format))
            {
                throw new InvalidOperationException(
                    $"Invalid sample format.\nSupported sample formats: {string.Join(", ", supportedFmts.Cast<FFmpegAudioEncoderSettings.AudioFormat>())}");
            }

            var layouts = codec.GetChLayouts().ToArray();
            if (layouts.Length > 0
                && layouts.All(i => !i.IsContentEqual(channelLayout)))
            {
                throw new InvalidOperationException("Invalid channel layout");
            }

            codecContext.SampleRate = sampleRate;
            codecContext.ChLayout = channelLayout;
            codecContext.SampleFmt = format;
            codecContext.BitRate = bitRate;
            codecContext.TimeBase = new AVRational { num = 1, den = sampleRate };
            codecContext.ThreadCount = ResolveThreadCount(settings.ThreadCount.Value);

            if ((outFormat.Flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                codecContext.Flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }
        });
        stream = muxer.AddStream(encoder);

        int nbsamples = (codec.Capabilities & ffmpeg.AV_CODEC_CAP_VARIABLE_FRAME_SIZE) != 0
            ? 10000
            : encoder.FrameSize;

        // dst: the encoder's format, which resolves Auto (AV_SAMPLE_FMT_NONE) to one the codec supports
        swr = new SampleConverter();
        swr.SetOpts(encoder.ChLayout, encoder.SampleRate, encoder.SampleFmt, nbsamples);

        // src: the sample provider always delivers planar stereo, which swr remixes into the encoder's layout
        audioFrame.ChLayout = DefaultChannelLayout(2);
        audioFrame.NbSamples = nbsamples;
        audioFrame.Format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        audioFrame.SampleRate = encoder.SampleRate;
        audioFrame.AllocateBuffer();
    }

    // The codec's own layout for the channel count comes first: AAC takes 5.1 only with back
    // speakers, while FFmpeg's default six-channel layout uses side ones.
    private static AVChannelLayout SelectChannelLayout(MediaCodec codec, int channels)
    {
        foreach (AVChannelLayout layout in codec.GetChLayouts())
        {
            if (layout.nb_channels == channels)
                return layout;
        }

        return DefaultChannelLayout(channels);
    }

    private static unsafe AVChannelLayout DefaultChannelLayout(int channels)
    {
        AVChannelLayout layout;
        ffmpeg.av_channel_layout_default(&layout, channels);
        return layout;
    }

    // New settings start with these H.264 options. Only an H.264 encoder gets them, and only the ones it parses (#3434).
    private static bool IsH264Default(AdditionalOption option) =>
        (option.Name.Value, option.Value.Value) is ("profile", "high") or ("preset", "medium") or ("level", "4.0");

    // The options the encoder is opened with: the ones the user set as they are, and the H.264 defaults it takes.
    internal static IEnumerable<KeyValuePair<string, string>> SelectVideoOptions(
        MediaCodec codec, IEnumerable<AdditionalOption> options)
    {
        bool isH264 = codec.Id == AVCodecID.AV_CODEC_ID_H264;
        return options
            .Where(item => !string.IsNullOrWhiteSpace(item.Name.Value))
            .Where(item => !IsH264Default(item) || (isH264 && ParsesOption(codec, item.Name.Value, item.Value.Value)))
            .Select(item => new KeyValuePair<string, string>(item.Name.Value, item.Value.Value))
            .ToArray();
    }

    // Whether codec parses value for name as avcodec_open2 would, on a context that is never opened.
    private static unsafe bool ParsesOption(MediaCodec codec, string name, string value)
    {
        AVCodecContext* context = ffmpeg.avcodec_alloc_context3(codec);
        if (context == null) return false;
        try
        {
            return ffmpeg.av_opt_set(context, name, value, ffmpeg.AV_OPT_SEARCH_CHILDREN) >= 0;
        }
        finally
        {
            ffmpeg.avcodec_free_context(&context);
        }
    }

    private unsafe void ConfigureVideoStream(
        OutputFormat outFormat, MediaMuxer muxer,
        MediaFrame videoFrame, out MediaEncoder encoder, out MediaStream stream)
    {
        var codec = VideoSettings.Codec.Value.Equals(CodecRecord.Default)
            ? MediaCodec.FindEncoder(outFormat.VideoCodec)
            : MediaCodec.FindEncoder(VideoSettings.Codec.Value.Name);
        int width = VideoSettings.DestinationSize.Value.Width;
        int height = VideoSettings.DestinationSize.Value.Height;
        var fps = VideoSettings.FrameRate.Value;
        var format = (AVPixelFormat)VideoSettings.Format.Value;
        int bitRate = VideoSettings.Bitrate.Value;
        var options = new MediaDictionary(SelectVideoOptions(codec, VideoSettings.Options.Value));

        encoder = MediaEncoder.Create(codec, codecContext =>
        {
            var supportedPixelFmts = codec.GetPixelFmts().ToArray();

            if (width <= 0 || height <= 0 || fps.ToDouble() <= 0 || bitRate < 0)
                throw new InvalidOperationException("Invalid video settings");
            if (!supportedPixelFmts.Any())
                throw new InvalidOperationException("Invalid video codec");

            if (format == AVPixelFormat.AV_PIX_FMT_NONE)
                format = supportedPixelFmts.First(i => ffmpeg.sws_isSupportedInput(i) != 0);
            else if (supportedPixelFmts.All(i => i != format))
                throw new InvalidOperationException(
                    $"Invalid pixel format.\nSupported pixel formats: {string.Join(", ", supportedPixelFmts)}");
            codecContext.Width = width;
            codecContext.Height = height;
            codecContext.TimeBase =
                new AVRational { num = (int)fps.Denominator, den = (int)fps.Numerator };
            codecContext.Framerate =
                new AVRational { num = (int)fps.Numerator, den = (int)fps.Denominator };
            codecContext.PixFmt = format;
            codecContext.BitRate = bitRate;
            codecContext.GopSize = VideoSettings.KeyframeRate.Value;
            codecContext.ThreadCount = ResolveThreadCount(settings.ThreadCount.Value);

            ConfigureVideoColor(codecContext, outFormat);

            var hwType = HardwareAcceleration.ToHWDeviceType((int)settings.Acceleration.Value);
            codecContext.InitHWDeviceContext(hwType);

            if ((outFormat.Flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                codecContext.Flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }
        }, options);
        stream = muxer.AddStream(encoder);

        // Apple QuickTime requires 'hvc1' sample entry type for HEVC playback
        if (stream.Codecpar->codec_id == AVCodecID.AV_CODEC_ID_HEVC)
        {
            // MKTAG('h','v','c','1')
            stream.Codecpar->codec_tag = 0x31637668;
        }

        _isHdr = ColorSpaceHelper.IsHdrTransfer(encoder.ColorTrc);
        _targetColorSpace = _isHdr
            ? ColorSpaceHelper.BuildHdrColorSpace(encoder.ColorTrc, encoder.ColorPrimaries)
            : ColorSpaceHelper.BuildTargetColorSpace(encoder.ColorTrc, encoder.ColorPrimaries);

        InitEncodeFilterGraph(encoder);

        videoFrame.Width = VideoSettings.SourceSize.Value.Width;
        videoFrame.Height = VideoSettings.SourceSize.Value.Height;
        videoFrame.Format = _isHdr
            ? (int)AVPixelFormat.AV_PIX_FMT_RGBA64LE
            : (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        videoFrame.ColorPrimaries = encoder.ColorPrimaries;
        videoFrame.ColorTrc = encoder.ColorTrc;
        videoFrame.Colorspace = AVColorSpace.AVCOL_SPC_RGB;
        videoFrame.ColorRange = AVColorRange.AVCOL_RANGE_JPEG;
        videoFrame.AllocateBuffer();
    }

    private unsafe void ConfigureVideoColor(MediaCodecContext context, OutputFormat format)
    {
        context.ColorTrc = (AVColorTransferCharacteristic)(int)VideoSettings.ColorTrc.Value;
        context.ColorPrimaries = (AVColorPrimaries)(int)VideoSettings.ColorPrimaries.Value;
        context.Colorspace = (AVColorSpace)(int)VideoSettings.ColorSpace.Value;
        context.ColorRange = (AVColorRange)(int)VideoSettings.ColorRange.Value;
        // The bitmap color-space mapper falls back to sRGB for unsupported logarithmic curves.
        if (context.ColorTrc is AVColorTransferCharacteristic.AVCOL_TRC_LOG or AVColorTransferCharacteristic.AVCOL_TRC_LOG_SQRT)
            context.ColorTrc = AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1;
        bool hdr = ColorSpaceHelper.IsHdrTransfer(context.ColorTrc);
        bool rgb = (ffmpeg.av_pix_fmt_desc_get(context.PixFmt)->flags & ffmpeg.AV_PIX_FMT_FLAG_RGB) != 0;
        VideoColorTarget target = rgb ? VideoColorTarget.Rgb
            // Every JPEG-family codec follows JFIF, and the mjpeg decoders read them as BT.601.
            : context.CodecId is AVCodecID.AV_CODEC_ID_MJPEG or AVCodecID.AV_CODEC_ID_MJPEGB
                or AVCodecID.AV_CODEC_ID_AMV or AVCodecID.AV_CODEC_ID_LJPEG ? VideoColorTarget.Jpeg
            : context.PixFmt is AVPixelFormat.AV_PIX_FMT_YUVJ420P or AVPixelFormat.AV_PIX_FMT_YUVJ422P
                or AVPixelFormat.AV_PIX_FMT_YUVJ444P or AVPixelFormat.AV_PIX_FMT_YUVJ440P
                or AVPixelFormat.AV_PIX_FMT_YUVJ411P ? VideoColorTarget.FullRangeYCbCr
            : VideoColorTarget.YCbCr;
        VideoColorTags defaults = VideoColorDefaults.Get(target, hdr, RecordsColor(context.CodecId, format));

        // Resolve unspecified values at encode time so saved presets and all callers get the same defaults.
        if (context.ColorTrc == AVColorTransferCharacteristic.AVCOL_TRC_UNSPECIFIED)
            context.ColorTrc = ColorSpaceHelper.ToFFmpeg(defaults.Transfer);
        if (context.ColorPrimaries == AVColorPrimaries.AVCOL_PRI_UNSPECIFIED)
            context.ColorPrimaries = ColorSpaceHelper.ToFFmpeg(defaults.Primaries);
        if (context.Colorspace == AVColorSpace.AVCOL_SPC_UNSPECIFIED)
            context.Colorspace = ColorSpaceHelper.ToFFmpeg(defaults.Matrix);
        if (context.ColorRange == AVColorRange.AVCOL_RANGE_UNSPECIFIED)
            context.ColorRange = ColorSpaceHelper.ToFFmpeg(defaults.Range);
    }

    // Bitstreams with a colour description, or containers that store one for any codec.
    private static bool RecordsColor(AVCodecID codec, OutputFormat format)
        => codec is AVCodecID.AV_CODEC_ID_H264 or AVCodecID.AV_CODEC_ID_HEVC or AVCodecID.AV_CODEC_ID_VVC
            or AVCodecID.AV_CODEC_ID_AV1 or AVCodecID.AV_CODEC_ID_VP9 or AVCodecID.AV_CODEC_ID_MPEG2VIDEO
            or AVCodecID.AV_CODEC_ID_PRORES
            || format.Name is "mov" or "mp4" or "matroska" or "webm";

    public override async ValueTask Encode(IFrameProvider frameProvider, ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        bool encodeVideo = false, encodeAudio = false;
        OutputFormat outputFormat = GuessOutputFormat(OutputFile);
        using (var fs = File.Create(OutputFile))
        using (var muxer = MediaMuxer.Create(fs, outputFormat))
        using (var videoFrame = new MediaFrame())
        using (var audioFrame = new MediaFrame())
        {
            SampleConverter? swr = null;
            var outFormat = muxer.Format;

            var encoders = new List<(MediaEncoder, MediaStream)>();
            (MediaEncoder Encoder, MediaStream Stream)? audioRef = null;
            (MediaEncoder Encoder, MediaStream Stream)? videoRef = null;
            bool headerWritten = false;
            bool encodeCompletedSuccessfully = false;
            try
            {
                if (outFormat.AudioCodec != AVCodecID.AV_CODEC_ID_NONE)
                {
                    ConfigureAudioStream(outFormat, muxer, audioFrame, out var encoder, out var stream, out swr);
                    encoders.Add((encoder, stream));
                    audioRef = (encoder, stream);
                    encodeAudio = true;
                }

                if (outFormat.VideoCodec != AVCodecID.AV_CODEC_ID_NONE)
                {
                    ConfigureVideoStream(outFormat, muxer, videoFrame, out var encoder, out var stream);
                    encoders.Add((encoder, stream));
                    videoRef = (encoder, stream);
                    encodeVideo = true;
                }

                muxer.DumpFormat();
                muxer.WriteHeader();
                headerWritten = true;

                var videoState = new EncodeState();
                var audioState = new EncodeState();
                while ((encodeVideo || encodeAudio) && !cancellationToken.IsCancellationRequested)
                {
                    if (encodeVideo &&
                        (!encodeAudio || ffmpeg.av_compare_ts(videoState.NextPts,
                            videoRef!.Value.Encoder.TimeBase,
                            audioState.NextPts, audioRef!.Value.Encoder.TimeBase) <= 0))
                    {
                        encodeVideo = await WriteVideoFrame(
                            muxer, videoRef!.Value.Encoder, videoRef!.Value.Stream, videoFrame, videoState, frameProvider);
                    }
                    else
                    {
                        encodeAudio = await WriteAudioFrame(
                            muxer, swr!, audioRef!.Value.Encoder, audioRef!.Value.Stream, audioFrame, audioState, sampleProvider);
                    }
                }

                // Report cancellation as OperationCanceledException, not success or a muxer error.
                cancellationToken.ThrowIfCancellationRequested();

                encodeCompletedSuccessfully = true;
            }
            finally
            {
                Exception? muxerFinalizationError = headerWritten ? FinalizeMuxer(muxer, encoders) : null;

                ReleaseEncodingResources(encoders, swr);

                // Surface muxer finalization failures to the caller so a truncated
                // output file is not reported as a successful encode. If the try
                // block already failed, let that original exception propagate
                // instead of overwriting it.
                if (encodeCompletedSuccessfully && muxerFinalizationError is not null)
                {
                    ExceptionDispatchInfo.Capture(muxerFinalizationError).Throw();
                }
            }
        }
    }

    private Exception? FinalizeMuxer(MediaMuxer muxer, List<(MediaEncoder, MediaStream)> encoders)
    {
        try
        {
            muxer.FlushCodecs(encoders.Select(i => i.Item1));
            muxer.WriteTrailer();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize muxer output for {OutputFile}.", OutputFile);
            return ex;
        }
    }

    private void ReleaseEncodingResources(List<(MediaEncoder, MediaStream)> encoders, SampleConverter? swr)
    {
        foreach (var (encoder, _) in encoders)
        {
            DisposeQuietly(encoder, nameof(MediaEncoder));
        }
        DisposeQuietly(swr, nameof(SampleConverter));
        DisposeQuietly(_filterGraph, nameof(MediaFilterGraph));

        _filterGraph = null;
        _bufferSrcCtx = null;
        _bufferSinkCtx = null;
    }

    private void DisposeQuietly(IDisposable? disposable, string resourceName)
    {
        if (disposable is null) return;
        try
        {
            disposable.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose {Resource} during FFmpeg encoding cleanup.", resourceName);
        }
    }

    private static unsafe OutputFormat GuessOutputFormat(string outputFile)
    {
        OutputFormat outputFormat = OutputFormat.GuessFormat(null, outputFile, null);
        // GuessFormat returns a wrapper even when the native format pointer is null.
        if ((AVOutputFormat*)outputFormat == null)
        {
            throw new InvalidOperationException(
                $"Could not determine FFmpeg output format from the file extension: {outputFile}");
        }

        return outputFormat;
    }

    private static async ValueTask<MediaFrame?> GetAudioFrame(MediaFrame frame, SampleConverter swr, EncodeState state,
        ISampleProvider sampleProvider)
    {
        int remaining = (int)Math.Min(frame.NbSamples, sampleProvider.SampleCount - state.NextPts);
        if (remaining <= 0)
            return null;

        using var audio = await sampleProvider.ReadNextSamples(frame.NbSamples);
        if (audio is null)
            return null;

        unsafe
        {
            audio.GetChannelData(0).CopyTo(new Span<float>((void*)frame.Data[0], frame.NbSamples));
            audio.GetChannelData(1).CopyTo(new Span<float>((void*)frame.Data[1], frame.NbSamples));
        }

        var converted = swr.ConvertFrame(frame, out _, out _);
        // The provider pads its last block; only real samples belong to the exported duration.
        converted.NbSamples = Math.Min(converted.NbSamples, remaining);
        converted.Pts = state.NextPts;
        state.NextPts += converted.NbSamples;

        return converted;
    }

    private static async ValueTask<bool> WriteAudioFrame(
        MediaMuxer muxer, SampleConverter swr, MediaEncoder encoder, MediaStream stream,
        MediaFrame src, EncodeState state,
        ISampleProvider sampleProvider)
    {
        var f = await GetAudioFrame(src, swr, state, sampleProvider);
        try
        {
            return WriteFrame(muxer, encoder, stream, f);
        }
        finally
        {
            f?.Dispose();
        }
    }

    private async ValueTask<MediaFrame?> GetVideoFrame(
        MediaFrame srcFrame, EncodeState state, IFrameProvider frameProvider)
    {
        using Bitmap? bitmap = await frameProvider.ReadNextFrame();
        if (bitmap is null)
            return null;

        using (var converted = _isHdr
            // Match both the transfer function and primaries declared by the encoder.
            ? bitmap.Convert(BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, _targetColorSpace!)
            : bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, _targetColorSpace!))
        {
            unsafe
            {
                // FFmpeg aligns each row independently, so its stride can exceed the bitmap's row size.
                FFmpegUtil.CopyPlane(converted.Data, converted.RowBytes,
                    (IntPtr)srcFrame.Data[0], srcFrame.Linesize[0],
                    converted.Width * converted.BytesPerPixel, converted.Height);
            }
        }

        // AVFilter: 画素形式の変換と寸法合わせ
        const int AV_BUFFERSRC_FLAG_KEEP_REF = 8;
        _bufferSrcCtx!.WriteFrame(srcFrame, AV_BUFFERSRC_FLAG_KEEP_REF);

        var filterOutput = new MediaFrame();
        int ret = _bufferSinkCtx!.GetFrame(filterOutput);
        if (ret < 0)
        {
            filterOutput.Dispose();
            return null;
        }

        filterOutput.Pts = state.NextPts;
        state.NextPts += 1;
        return filterOutput;
    }

    private async ValueTask<bool> WriteVideoFrame(
        MediaMuxer muxer, MediaEncoder encoder,
        MediaStream stream, MediaFrame srcFrame,
        EncodeState state, IFrameProvider frameProvider)
    {
        var f = await GetVideoFrame(srcFrame, state, frameProvider);
        try
        {
            return WriteFrame(muxer, encoder, stream, f);
        }
        finally
        {
            f?.Dispose();
        }
    }

    private static unsafe bool WriteFrame(MediaMuxer muxer, MediaEncoder encoder, MediaStream stream,
        MediaFrame? frame)
    {
        foreach (var pkt in encoder.EncodeFrame(frame))
        {
            pkt.StreamIndex = stream.Index;
            // Encoders such as libx264 leave a video packet's duration unset, and the muxer needs it for the last
            // sample, which has no next timestamp to take its length from: a one-frame video would otherwise last
            // zero seconds. Each packet holds one frame, so give it one frame interval in the encoder's time base.
            if (pkt.Duration == 0 && encoder.CodecType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                pkt.Duration = ffmpeg.av_rescale_q(1, ffmpeg.av_inv_q(encoder.Framerate), encoder.TimeBase);
            ffmpeg.av_packet_rescale_ts(pkt, encoder.TimeBase, stream.TimeBase);
            muxer.WritePacket(pkt).ThrowIfError();
        }

        return frame != null;
    }

    // The encoding settings' ThreadCount, read as the decoder reads its own: 0 lets FFmpeg choose, a positive
    // count is capped at the processor count, and the default -1 keeps the automatic count of up to 16.
    internal static int ResolveThreadCount(int threadCount)
        => threadCount == 0 ? 0 : Math.Min(Environment.ProcessorCount, threadCount > 0 ? threadCount : 16);
}

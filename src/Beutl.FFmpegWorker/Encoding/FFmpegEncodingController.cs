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

        int width = VideoSettings.SourceSize.Width;
        int height = VideoSettings.SourceSize.Height;
        var srcPixFmt = _isHdr ? AVPixelFormat.AV_PIX_FMT_RGBA64LE : AVPixelFormat.AV_PIX_FMT_BGRA;
        var fps = VideoSettings.FrameRate;
        var timeBase = new AVRational { num = (int)fps.Denominator, den = (int)fps.Numerator };
        var sar = new AVRational { num = 1, den = 1 };
        var frameRate = new AVRational { num = (int)fps.Numerator, den = (int)fps.Denominator };

        _bufferSrcCtx = _filterGraph.AddVideoSrcFilter(
            bufferSrc, width, height, srcPixFmt, timeBase, sar, frameRate);
        _bufferSinkCtx = _filterGraph.AddVideoSinkFilter(bufferSink, [encoder.PixFmt]);

        // An output of another aspect ratio gets the whole frame with black bars rather than a stretched one.
        (string scaleStr, string? padStr) = FrameFit.GetFilterArguments(
            VideoSettings.SourceSize, VideoSettings.DestinationSize);
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
        var codec = AudioSettings.Codec.Equals(CodecRecord.Default)
            ? MediaCodec.FindEncoder(outFormat.AudioCodec)
            : MediaCodec.FindEncoder(AudioSettings.Codec.Name);
        AVChannelLayout channelLayout = SelectChannelLayout(codec, AudioSettings.Channels);
        int sampleRate = AudioSettings.SampleRate;
        var format = (AVSampleFormat)AudioSettings.Format;
        int bitRate = AudioSettings.Bitrate;
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
            codecContext.ThreadCount = ResolveThreadCount(settings.ThreadCount);

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

    // New settings start with these H.264 options, which most other encoders reject (#2898).
    private static bool IsH264OnlyDefault(AdditionalOption option) =>
        (option.Name, option.Value) is ("profile", "high") or ("preset", "medium");

    private unsafe void ConfigureVideoStream(
        OutputFormat outFormat, MediaMuxer muxer,
        MediaFrame videoFrame, out MediaEncoder encoder, out MediaStream stream)
    {
        var codec = VideoSettings.Codec.Equals(CodecRecord.Default)
            ? MediaCodec.FindEncoder(outFormat.VideoCodec)
            : MediaCodec.FindEncoder(VideoSettings.Codec.Name);
        int width = VideoSettings.DestinationSize.Width;
        int height = VideoSettings.DestinationSize.Height;
        var fps = VideoSettings.FrameRate;
        var format = (AVPixelFormat)VideoSettings.Format;
        int bitRate = VideoSettings.Bitrate;
        bool isH264 = codec.Id == AVCodecID.AV_CODEC_ID_H264;
        var options = new MediaDictionary(VideoSettings.Options
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => isH264 || !IsH264OnlyDefault(item))
            .Select(item => new KeyValuePair<string, string>(item.Name, item.Value)));

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
            codecContext.GopSize = VideoSettings.KeyframeRate;
            codecContext.ThreadCount = ResolveThreadCount(settings.ThreadCount);

            codecContext.ColorPrimaries = (AVColorPrimaries)(int)VideoSettings.ColorPrimaries;
            codecContext.ColorTrc = (AVColorTransferCharacteristic)(int)VideoSettings.ColorTrc;
            codecContext.Colorspace = (AVColorSpace)(int)VideoSettings.ColorSpace;
            codecContext.ColorRange = (AVColorRange)(int)VideoSettings.ColorRange;

            var hwType = HardwareAcceleration.ToHWDeviceType((int)settings.Acceleration);
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

        _isHdr = ColorSpaceHelper.IsHdrTransfer((AVColorTransferCharacteristic)(int)VideoSettings.ColorTrc);
        _targetColorSpace = _isHdr
            ? ColorSpaceHelper.BuildHdrColorSpace((AVColorTransferCharacteristic)(int)VideoSettings.ColorTrc, (AVColorPrimaries)(int)VideoSettings.ColorPrimaries)
            : null;

        InitEncodeFilterGraph(encoder);

        videoFrame.Width = VideoSettings.SourceSize.Width;
        videoFrame.Height = VideoSettings.SourceSize.Height;
        videoFrame.Format = _isHdr
            ? (int)AVPixelFormat.AV_PIX_FMT_RGBA64LE
            : (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        videoFrame.ColorPrimaries = (AVColorPrimaries)(int)VideoSettings.ColorPrimaries;
        videoFrame.ColorTrc = (AVColorTransferCharacteristic)(int)VideoSettings.ColorTrc;
        videoFrame.Colorspace = (AVColorSpace)(int)VideoSettings.ColorSpace;
        videoFrame.ColorRange = (AVColorRange)(int)VideoSettings.ColorRange;
        videoFrame.AllocateBuffer();
    }

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
        using var audio = await sampleProvider.ReadNextSamples(frame.NbSamples);
        if (audio is null)
            return null;

        unsafe
        {
            audio.GetChannelData(0).CopyTo(new Span<float>((void*)frame.Data[0], frame.NbSamples));
            audio.GetChannelData(1).CopyTo(new Span<float>((void*)frame.Data[1], frame.NbSamples));
        }

        var converted = swr.ConvertFrame(frame, out _, out _);
        converted.Pts = state.NextPts;
        state.NextPts += frame.NbSamples;

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
            // Skia: LinearSrgb → ターゲット色空間（例: BT.2020/PQ）
            ? bitmap.Convert(BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, _targetColorSpace!)
            // Skia: LinearSrgb → Bgra8888/Srgb
            : bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb))
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

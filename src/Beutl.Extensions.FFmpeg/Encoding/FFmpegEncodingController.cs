using Beutl.Extensibility;
using Beutl.Logging;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;
using Microsoft.Extensions.Logging;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public class FFmpegEncodingController(string outputFile, FFmpegEncodingSettings settings)
    : EncodingController(outputFile)
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegEncodingController>();
    const int AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;

    private class EncodeState
    {
        public long NextPts { get; set; }
    }

    public override FFmpegVideoEncoderSettings VideoSettings { get; } = new();

    public override FFmpegAudioEncoderSettings AudioSettings { get; } = new();

    private AVHWDeviceType? GetAVHWDeviceType()
    {
        return settings.Acceleration switch
        {
            FFmpegEncodingSettings.AccelerationOptions.Software => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE,
            FFmpegEncodingSettings.AccelerationOptions.VDPAU => AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU,
            FFmpegEncodingSettings.AccelerationOptions.CUDA => AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            FFmpegEncodingSettings.AccelerationOptions.VAAPI => AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
            FFmpegEncodingSettings.AccelerationOptions.DXVA2 => AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
            FFmpegEncodingSettings.AccelerationOptions.QSV => AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
            FFmpegEncodingSettings.AccelerationOptions.VideoToolbox => AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
            FFmpegEncodingSettings.AccelerationOptions.D3D11VA => AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            FFmpegEncodingSettings.AccelerationOptions.DRM => AVHWDeviceType.AV_HWDEVICE_TYPE_DRM,
            FFmpegEncodingSettings.AccelerationOptions.OpenCL => AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL,
            FFmpegEncodingSettings.AccelerationOptions.MediaCodec => AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC,
            FFmpegEncodingSettings.AccelerationOptions.Vulkan => AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
            _ => null
        };
    }

    private void ConfigureAudioStream(
        OutputFormat outFormat, MediaMuxer muxer,
        MediaFrame audioFrame, out MediaEncoder encoder, out MediaStream stream, out SampleConverter swr)
    {
        var codec = AudioSettings.Codec.Equals(CodecRecord.Default)
            ? MediaCodec.FindEncoder(outFormat.AudioCodec)
            : MediaCodec.FindEncoder(AudioSettings.Codec.Name);
        var channelLayout = new AVChannelLayout
        {
            order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
            nb_channels = AudioSettings.Channels,
            u = new AVChannelLayout_u { mask = ffmpeg.AV_CH_LAYOUT_STEREO }
        };
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
            if (supportedFmts.Length == 0 || supportedSampleRates.Length == 0)
            {
                _logger.LogInformation("Supported sample rates: {Rates}", string.Join(", ", supportedSampleRates));
                _logger.LogInformation("Supported sample formats: {Formats}", string.Join(", ", supportedFmts));
                throw new InvalidOperationException("Invalid audio codec");
            }

            if (sampleRate <= 0)
            {
                sampleRate = supportedSampleRates[0];
            }
            else if (supportedSampleRates.All(i => i != sampleRate))
            {
                throw new InvalidOperationException(
                    $"Invalid sample rate.\nSupported sample rates: {string.Join(", ", supportedSampleRates)}");
            }

            if (format == AVSampleFormat.AV_SAMPLE_FMT_NONE)
            {
                format = supportedFmts.First();
            }
            else if (supportedFmts.All(i => i != format))
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
            codecContext.ThreadCount = Math.Min(Environment.ProcessorCount, 16);

            if ((outFormat.Flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                codecContext.Flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }
        });
        stream = muxer.AddStream(encoder);

        int nbsamples = (codec.Capabilities & ffmpeg.AV_CODEC_CAP_VARIABLE_FRAME_SIZE) != 0
            ? 10000
            : encoder.FrameSize;

        swr = new SampleConverter();
        swr.SetOpts(encoder.ChLayout, encoder.SampleRate,
            (AVSampleFormat)AudioSettings.Format, nbsamples);

        // src
        audioFrame.ChLayout = encoder.ChLayout;
        audioFrame.NbSamples = nbsamples;
        audioFrame.Format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLT;
        audioFrame.SampleRate = encoder.SampleRate;
        audioFrame.AllocateBuffer();
    }

    private void ConfigureVideoStream(
        OutputFormat outFormat, MediaMuxer muxer,
        MediaFrame videoFrame, out MediaEncoder encoder, out MediaStream stream, out PixelConverter sws)
    {
        var codec = VideoSettings.Codec.Equals(CodecRecord.Default)
            ? MediaCodec.FindEncoder(outFormat.VideoCodec)
            : MediaCodec.FindEncoder(VideoSettings.Codec.Name);
        int width = VideoSettings.DestinationSize.Width;
        int height = VideoSettings.DestinationSize.Height;
        var fps = VideoSettings.FrameRate;
        var format = (AVPixelFormat)VideoSettings.Format;
        int bitRate = VideoSettings.Bitrate;
        var options = new MediaDictionary(VideoSettings.Options
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .Select(item => new KeyValuePair<string, string>(item.Key, item.Value)));

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
            codecContext.ThreadCount = Math.Min(Environment.ProcessorCount, 16);

            var hwType = GetAVHWDeviceType();
            codecContext.InitHWDeviceContext(hwType);

            if ((outFormat.Flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                codecContext.Flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }
        }, options);
        stream = muxer.AddStream(encoder);

        sws = new PixelConverter();
        sws.SetOpts(VideoSettings.DestinationSize.Width, VideoSettings.DestinationSize.Height, encoder.PixFmt);

        videoFrame.Width = VideoSettings.SourceSize.Width;
        videoFrame.Height = VideoSettings.SourceSize.Height;
        videoFrame.Format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        videoFrame.AllocateBuffer();
    }

    public override async ValueTask Encode(IFrameProvider frameProvider, ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        bool encodeVideo = false, encodeAudio = false;
        using (var fs = File.OpenWrite(OutputFile))
        using (var muxer = MediaMuxer.Create(fs, OutputFormat.GuessFormat(null, OutputFile, null)))
        using (var videoFrame = new MediaFrame())
        using (var audioFrame = new MediaFrame())
        {
            PixelConverter? sws = null;
            SampleConverter? swr = null;
            var outFormat = muxer.Format;

            var encoders = new List<(MediaEncoder, MediaStream)>();
            if (outFormat.AudioCodec != AVCodecID.AV_CODEC_ID_NONE)
            {
                ConfigureAudioStream(outFormat, muxer, audioFrame, out var encoder, out var stream, out swr);
                encoders.Add((encoder, stream));
                encodeAudio = true;
            }

            if (outFormat.VideoCodec != AVCodecID.AV_CODEC_ID_NONE)
            {
                ConfigureVideoStream(outFormat, muxer, videoFrame, out var encoder, out var stream, out sws);
                encoders.Add((encoder, stream));
                encodeVideo = true;
            }

            muxer.DumpFormat();
            muxer.WriteHeader();

            var videoState = new EncodeState();
            var audioState = new EncodeState();
            while ((encodeVideo || encodeAudio) && !cancellationToken.IsCancellationRequested)
            {
                if (encodeVideo &&
                    (!encodeAudio || ffmpeg.av_compare_ts(videoState.NextPts,
                        encoders[1].Item1.TimeBase,
                        audioState.NextPts, encoders[0].Item1.TimeBase) <= 0))
                {
                    encodeVideo = await WriteVideoFrame(
                        muxer, sws!, encoders[1].Item1, encoders[1].Item2, videoFrame, videoState, frameProvider);
                }
                else
                {
                    encodeAudio = await WriteAudioFrame(
                        muxer, swr!, encoders[0].Item1, encoders[0].Item2, audioFrame, audioState, sampleProvider);
                }
            }

            muxer.FlushCodecs(encoders.Select(i => i.Item1));
            muxer.WriteTrailer();
            encoders.ForEach(t => t.Item1.Dispose());
        }
    }

    private static async ValueTask<MediaFrame?> GetAudioFrame(MediaFrame frame, SampleConverter swr, EncodeState state,
        ISampleProvider sampleProvider)
    {
        if (state.NextPts > sampleProvider.SampleCount)
            return null;

        using var pcm = await sampleProvider.Sample(state.NextPts, frame.NbSamples);
        unsafe
        {
            IntPtr size = pcm.NumSamples * pcm.SampleSize;
            Buffer.MemoryCopy((void*)pcm.Data, (void*)frame.Data[0], size, size);
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
        return WriteFrame(muxer, encoder, stream, f);
    }

    private static async ValueTask<MediaFrame?> GetVideoFrame(
        MediaFrame srcFrame, PixelConverter sws, EncodeState state,
        IFrameProvider frameProvider)
    {
        if (state.NextPts > frameProvider.FrameCount)
            return null;

        using var bitmap = await frameProvider.RenderFrame(state.NextPts);
        unsafe
        {
            Buffer.MemoryCopy((void*)bitmap.Data, (void*)srcFrame.Data[0], bitmap.ByteCount, bitmap.ByteCount);
        }

        var o = sws.ConvertFrame(srcFrame);
        o.Pts = state.NextPts;
        state.NextPts += 1;
        return o;
    }

    private static async ValueTask<bool> WriteVideoFrame(
        MediaMuxer muxer, PixelConverter sws, MediaEncoder encoder,
        MediaStream stream, MediaFrame srcFrame,
        EncodeState state, IFrameProvider frameProvider)
    {
        return WriteFrame(muxer, encoder, stream, await GetVideoFrame(srcFrame, sws, state, frameProvider));
    }

    private static unsafe bool WriteFrame(MediaMuxer muxer, MediaEncoder encoder, MediaStream stream,
        MediaFrame? frame)
    {
        foreach (var pkt in encoder.EncodeFrame(frame))
        {
            pkt.StreamIndex = stream.Index;
            // Console.WriteLine(
            //     $"pts:{pkt.Pts} pts_time:{0} dst:{pkt.Dts} dts_time:{0} duration:{pkt.Duration} duration_time:{0} stream_index:{streamIndex}");
            ffmpeg.av_packet_rescale_ts(pkt, encoder.TimeBase, stream.TimeBase);
            muxer.WritePacket(pkt).ThrowIfError();
        }

        return frame != null;
    }
}

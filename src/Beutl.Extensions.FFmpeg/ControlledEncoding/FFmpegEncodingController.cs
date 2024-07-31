using Beutl.Embedding.FFmpeg.Encoding;
using Beutl.Extensibility;
using Beutl.Media.Encoding;
using EmguFFmpeg;
using FFmpeg.AutoGen;
using MediaWriter = EmguFFmpeg.MediaWriter;

namespace Beutl.Embedding.FFmpeg.ControlledEncoding;

public class FFmpegEncodingController(string outputFile, FFmpegEncodingSettings settings)
    : EncodingController(outputFile)
{
    const int AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;

    private class EncodeState
    {
        public long NextPts { get; set; }
    }

    public override FFmpegVideoEncoderSettings VideoSettings { get; } = new();

    public override FFmpegAudioEncoderSettings AudioSettings { get; } = new();

    private AVHWDeviceType GetAVHWDeviceType(MediaCodec codec, AVPixelFormat pixelFormat)
    {
        return settings.Acceleration switch
        {
            FFmpegEncodingSettings.AccelerationOptions.Software => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE,
            FFmpegEncodingSettings.AccelerationOptions.D3D11VA => AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            FFmpegEncodingSettings.AccelerationOptions.DXVA2 => AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
            FFmpegEncodingSettings.AccelerationOptions.QSV => AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
            FFmpegEncodingSettings.AccelerationOptions.CUVID => AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            FFmpegEncodingSettings.AccelerationOptions.CUDA => AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            FFmpegEncodingSettings.AccelerationOptions.VDPAU => AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU,
            FFmpegEncodingSettings.AccelerationOptions.VAAPI => AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
            FFmpegEncodingSettings.AccelerationOptions.LibMFX => AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
            _ => codec.SupportedHardware
                .First(i => (i.methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) == 0 && i.pix_fmt == pixelFormat)
                .device_type
        };
    }

    private void ConfigureAudioStream(
        OutFormat outFormat, MediaWriter writer,
        AudioFrame audioFrame, out MediaEncoder encoder, out MediaStream stream, out SampleConverter swr)
    {
        var audioCodec = AudioSettings.Codec == FFmpegAudioEncoderSettings.AudioCodec.Default
            ? outFormat.AudioCodec
            : (AVCodecID)AudioSettings.Codec;
        var channelLayout = new AVChannelLayout
        {
            order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
            nb_channels = AudioSettings.Channels,
            u = new AVChannelLayout_u { mask = ffmpeg.AV_CH_LAYOUT_STEREO }
        };
        int sampleRate = AudioSettings.SampleRate;
        var format = (AVSampleFormat)AudioSettings.Format;
        int bitRate = AudioSettings.Bitrate;
        encoder = MediaEncoder.CreateEncode(audioCodec, outFormat.Flags, codec =>
        {
            unsafe
            {
                AVCodecContext* codecContext = codec;
                if (channelLayout.nb_channels <= 0 || sampleRate <= 0 || bitRate < 0)
                    throw new FFmpegException(FFmpegException.NonNegative);
                if (!codec.SupportedSampelFmts.Any() || !codec.SupportedSampleRates.Any())
                    throw new FFmpegException(FFmpegException.NotSupportCodecId);

                if (sampleRate <= 0)
                {
                    sampleRate = codec.SupportedSampleRates.First();
                }
                else if (codec.SupportedSampleRates.All(i => i != sampleRate))
                {
                    throw new FFmpegException(FFmpegException.NotSupportSampleRate);
                }

                if (format == AVSampleFormat.AV_SAMPLE_FMT_NONE)
                {
                    format = codec.SupportedSampelFmts.First();
                }
                else if (codec.SupportedSampelFmts.All(i => i != format))
                {
                    throw new FFmpegException(FFmpegException.NotSupportFormat);
                }

                if (codec.SupportedChannelLayout.Any()
                    && codec.SupportedChannelLayout.All(i => i != channelLayout.u.mask))
                {
                    throw new FFmpegException(FFmpegException.NotSupportChLayout);
                }

                codecContext->sample_rate = sampleRate;
                codecContext->ch_layout = channelLayout;
                codecContext->sample_fmt = format;
                codecContext->bit_rate = bitRate;
                codecContext->time_base = new AVRational { num = 1, den = sampleRate };
                codecContext->thread_count = Math.Min(Environment.ProcessorCount, 16);
            }
        });
        stream = writer.AddStream(encoder);

        int nbsamples = (encoder.AVCodec.capabilities & ffmpeg.AV_CODEC_CAP_VARIABLE_FRAME_SIZE) != 0
            ? 10000
            : encoder.AVCodecContext.frame_size;

        swr = new SampleConverter((AVSampleFormat)AudioSettings.Format, AudioSettings.Channels, nbsamples,
            encoder.AVCodecContext.sample_rate);

        // src
        audioFrame.Init(
            encoder.AVCodecContext.ch_layout,
            nbsamples,
            AVSampleFormat.AV_SAMPLE_FMT_FLT,
            44100);
    }

    private void ConfigureVideoStream(
        OutFormat outFormat, MediaWriter writer,
        VideoFrame videoFrame, out MediaEncoder encoder, out MediaStream stream, out PixelConverter sws)
    {
        var videoCodec = VideoSettings.Codec == FFmpegVideoEncoderSettings.VideoCodec.Default
            ? outFormat.VideoCodec
            : (AVCodecID)VideoSettings.Codec;
        int width = VideoSettings.DestinationSize.Width;
        int height = VideoSettings.DestinationSize.Height;
        var fps = VideoSettings.FrameRate;
        var format = VideoSettings.Format;
        int bitRate = VideoSettings.Bitrate;
        var options = new MediaDictionary
        {
            { "preset", VideoSettings.Preset },
            { "crf", VideoSettings.Crf.ToString() },
            { "profile", VideoSettings.Profile },
            { "level", "4.0" },
        };
        encoder = MediaEncoder.CreateEncode(videoCodec, outFormat.Flags, codec =>
        {
            unsafe
            {
                AVCodecContext* codecContext = codec;
                if (width <= 0 || height <= 0 || fps.ToDouble() <= 0 || bitRate < 0)
                    throw new FFmpegException(FFmpegException.NonNegative);
                if (!codec.SupportedPixelFmts.Any())
                    throw new FFmpegException(FFmpegException.NotSupportCodecId);

                if (format == AVPixelFormat.AV_PIX_FMT_NONE)
                    format = codec.SupportedPixelFmts.First();
                else if (codec.SupportedPixelFmts.All(i => i != format))
                    throw new FFmpegException(FFmpegException.NotSupportFormat);
                codecContext->width = width;
                codecContext->height = height;
                codecContext->time_base =
                    new AVRational { num = (int)fps.Denominator, den = (int)fps.Numerator };
                codecContext->framerate =
                    new AVRational { num = (int)fps.Numerator, den = (int)fps.Denominator };
                codecContext->pix_fmt = format;
                codecContext->bit_rate = bitRate;
                codecContext->gop_size = VideoSettings.KeyframeRate;
                codecContext->thread_count = Math.Min(Environment.ProcessorCount, 16);

                var hwType = GetAVHWDeviceType(codec, format);
                if (hwType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE && codecContext->codec != null &&
                    codecContext->hw_device_ctx == null)
                {
                    if (codec.SupportedHardware.Select(i => (AVCodecHWConfig?)i)
                            .FirstOrDefault(i =>
                                (i!.Value.device_type == hwType) &&
                                (i.Value.methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) != 0) is { } hWConfig)
                    {
                        ffmpeg.av_hwdevice_ctx_create(
                            &codecContext->hw_device_ctx, hWConfig.device_type, null, null, 0).ThrowIfError();
                        codecContext->get_format = (AVCodecContext_get_format)((_, pix_fmt) =>
                        {
                            while (*pix_fmt != AVPixelFormat.AV_PIX_FMT_NONE)
                            {
                                if (*pix_fmt == hWConfig.pix_fmt)
                                {
                                    return *pix_fmt;
                                }

                                pix_fmt++;
                            }

                            return AVPixelFormat.AV_PIX_FMT_NONE;
                        });
                    }
                }
            }
        }, options);
        stream = writer.AddStream(encoder);

        sws = new PixelConverter(encoder.AVCodecContext.pix_fmt,
            VideoSettings.DestinationSize.Width, VideoSettings.DestinationSize.Height);

        videoFrame.Init(
            VideoSettings.SourceSize.Width, VideoSettings.SourceSize.Height,
            AVPixelFormat.AV_PIX_FMT_BGRA);
    }

    public override async ValueTask Encode(IFrameProvider frameProvider, ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        bool encodeVideo = false, encodeAudio = false;
        using (var writer = new MediaWriter(OutputFile))
        using (var videoFrame = new VideoFrame())
        using (var audioFrame = new AudioFrame())
        {
            PixelConverter? sws = null;
            SampleConverter? swr = null;
            var outFormat = writer.Format;

            var encoders = new List<(MediaEncoder, MediaStream)>();
            if (outFormat.AudioCodec != AVCodecID.AV_CODEC_ID_NONE)
            {
                ConfigureAudioStream(outFormat, writer, audioFrame, out var encoder, out var stream, out swr);
                encoders.Add((encoder, stream));
                encodeAudio = true;
            }

            if (outFormat.VideoCodec != AVCodecID.AV_CODEC_ID_NONE)
            {
                ConfigureVideoStream(outFormat, writer, videoFrame, out var encoder, out var stream, out sws);
                encoders.Add((encoder, stream));
                encodeVideo = true;
            }

            writer.DumpFormat();
            writer.Initialize();

            var videoState = new EncodeState();
            var audioState = new EncodeState();
            while (encodeVideo || encodeAudio)
            {
                if (encodeVideo &&
                    (!encodeAudio || ffmpeg.av_compare_ts(videoState.NextPts, encoders[1].Item1.AVCodecContext.time_base,
                        audioState.NextPts, encoders[0].Item1.AVCodecContext.time_base) <= 0))
                {
                    encodeVideo = await WriteVideoFrame(
                        writer, sws!, encoders[1].Item1, encoders[1].Item2, videoFrame, videoState, frameProvider);
                }
                else
                {
                    encodeAudio = await WriteAudioFrame(
                        writer, swr!, encoders[0].Item1, encoders[0].Item2, audioFrame, audioState, sampleProvider);
                }
            }

            // oc.FlushCodecs(encoders);
            // oc.WriteTrailer();
            writer.FlushMuxer();
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
        MediaWriter writer, SampleConverter swr, MediaEncoder encoder, MediaStream stream,
        MediaFrame src, EncodeState state,
        ISampleProvider sampleProvider)
    {
        var f = await GetAudioFrame(src, swr, state, sampleProvider);
        return WriteFrame(writer, encoder, stream, f);
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
        MediaWriter writer, PixelConverter sws, MediaEncoder encoder,
        MediaStream stream, MediaFrame srcFrame,
        EncodeState state, IFrameProvider frameProvider)
    {
        return WriteFrame(writer, encoder, stream, await GetVideoFrame(srcFrame, sws, state, frameProvider));
    }

    private static unsafe bool WriteFrame(MediaWriter writer, MediaEncoder encoder, MediaStream stream, MediaFrame? frame)
    {
        foreach (var pkt in encoder.EncodeFrame(frame))
        {
            pkt.StreamIndex = stream.Index;
            // Console.WriteLine(
            //     $"pts:{pkt.Pts} pts_time:{0} dst:{pkt.Dts} dts_time:{0} duration:{pkt.Duration} duration_time:{0} stream_index:{streamIndex}");
            ffmpeg.av_packet_rescale_ts(pkt, encoder.AVCodecContext.time_base, stream.TimeBase);
            writer.WritePacket(pkt);
        }

        return frame != null;
    }
}

using Beutl.Embedding.FFmpeg.Encoding;
using Beutl.Extensibility;
using EmguFFmpeg;
using FFmpeg.AutoGen;

namespace Beutl.Embedding.FFmpeg.ControlledEncoding;

public class FFmpegEncodingController(string outputFile) : EncodingController(outputFile)
{
    private class Parames
    {
        public double t { get; set; }
        public double tincr { get; set; }
        public double tincr2 { get; set; }
        public long nextPts { get; set; }
    }

    public override FFmpegVideoEncoderSettings VideoSettings { get; } = new();

    public override FFmpegAudioEncoderSettings AudioSettings { get; } = new();

    public override async ValueTask Encode(IFrameProvider frameProvider, ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        bool encode_video = false, encode_audio = false;
        using (var oc = new MediaWriter(OutputFile))
        using (var vtmpframe = new VideoFrame())
        using (var atmpframe = new AudioFrame())
        {
            PixelConverter? sws = null;
            SampleConverter? swr = null;
            var fmt = oc.Format;

            var encoders = new List<(MediaEncoder, MediaStream)>();
            /* Add the audio and video streams using the default format codecs
             * and initialize the codecs. */
            if (fmt.AudioCodec != AVCodecID.AV_CODEC_ID_NONE)
            {
                var audioCodec = AudioSettings.Codec == FFmpegAudioEncoderSettings.AudioCodec.Default
                    ? fmt.AudioCodec
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
                var encoder = MediaEncoder.CreateEncode(audioCodec, fmt.Flags, codec =>
                {
                    unsafe
                    {
                        AVCodecContext* pCodecContext = codec;
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

                        pCodecContext->sample_rate = sampleRate;
                        pCodecContext->ch_layout = channelLayout;
                        pCodecContext->sample_fmt = format;
                        pCodecContext->bit_rate = bitRate;
                        pCodecContext->time_base = new AVRational { num = 1, den = sampleRate };
                        pCodecContext->thread_count = Environment.ProcessorCount;
                    }
                });
                var stream = oc.AddStream(encoder);
                encoders.Add((encoder, stream));
                /* set resampler context options */
                encode_audio = true;

                int nbsamples = (encoder.AVCodec.capabilities & ffmpeg.AV_CODEC_CAP_VARIABLE_FRAME_SIZE) != 0
                    ? 10000
                    : encoder.AVCodecContext.frame_size;

                swr = new SampleConverter((AVSampleFormat)AudioSettings.Format, AudioSettings.Channels, nbsamples,
                    encoder.AVCodecContext.sample_rate);

                // src
                atmpframe.Init(
                    encoder.AVCodecContext.ch_layout,
                    nbsamples,
                    AVSampleFormat.AV_SAMPLE_FMT_FLT,
                    44100);
            }

            if (fmt.VideoCodec != AVCodecID.AV_CODEC_ID_NONE)
            {
                var videoCodec = VideoSettings.Codec == FFmpegVideoEncoderSettings.VideoCodec.Default
                    ? fmt.VideoCodec
                    : (AVCodecID)VideoSettings.Codec;
                var width = VideoSettings.DestinationSize.Width;
                var height = VideoSettings.DestinationSize.Height;
                var fps = VideoSettings.FrameRate;
                var format = VideoSettings.Format;
                var bitRate = VideoSettings.Bitrate;
                var encoder = MediaEncoder.CreateEncode(videoCodec, fmt.Flags, codec =>
                {
                    unsafe
                    {
                        AVCodecContext* pCodecContext = codec;
                        if (width <= 0 || height <= 0 || fps.ToDouble() <= 0 || bitRate < 0)
                            throw new FFmpegException(FFmpegException.NonNegative);
                        if (!codec.SupportedPixelFmts.Any())
                            throw new FFmpegException(FFmpegException.NotSupportCodecId);

                        if (format == AVPixelFormat.AV_PIX_FMT_NONE)
                            format = codec.SupportedPixelFmts.First();
                        else if (codec.SupportedPixelFmts.All(i => i != format))
                            throw new FFmpegException(FFmpegException.NotSupportFormat);
                        pCodecContext->width = width;
                        pCodecContext->height = height;
                        pCodecContext->time_base =
                            new AVRational { num = (int)fps.Denominator, den = (int)fps.Numerator };
                        pCodecContext->framerate =
                            new AVRational { num = (int)fps.Numerator, den = (int)fps.Denominator };
                        pCodecContext->pix_fmt = format;
                        pCodecContext->bit_rate = bitRate;
                    }
                });
                var stream = oc.AddStream(encoder);
                encoders.Add((encoder, stream));
                encode_video = true;

                sws = new PixelConverter(encoder.AVCodecContext.pix_fmt,
                    VideoSettings.DestinationSize.Width, VideoSettings.DestinationSize.Height);

                // src
                vtmpframe.Init(
                    VideoSettings.SourceSize.Width, VideoSettings.SourceSize.Height,
                    AVPixelFormat.AV_PIX_FMT_BGRA);
            }

            oc.DumpFormat();
            oc.Initialize();

            var vp = new Parames();
            var ap = new Parames();
            while (encode_video || encode_audio)
            {
                /* select the stream to encode */
                if (encode_video &&
                    (!encode_audio || ffmpeg.av_compare_ts(vp.nextPts, encoders[1].Item1.AVCodecContext.time_base,
                        ap.nextPts, encoders[0].Item1.AVCodecContext.time_base) <= 0))
                {
                    encode_video = await WriteVideoFrame(
                        oc, sws, encoders[1].Item1, encoders[1].Item2, vtmpframe, vp, frameProvider);
                }
                else
                {
                    encode_audio = await WriteAudioFrame(
                        oc, swr, encoders[0].Item1, encoders[0].Item2, atmpframe, ap, sampleProvider);
                }
            }

            // oc.FlushCodecs(encoders);
            // oc.WriteTrailer();
            oc.FlushMuxer();
            encoders.ForEach(t => t.Item1.Dispose());
        }
    }

    private static async ValueTask<MediaFrame?> GetAudioFrame(MediaFrame frame, SampleConverter swr, Parames ap,
        ISampleProvider sampleProvider)
    {
        if (ap.nextPts > sampleProvider.SampleCount)
            return null;

        using var pcm = await sampleProvider.Sample(ap.nextPts, frame.NbSamples);
        unsafe
        {
            IntPtr size = pcm.NumSamples * pcm.SampleSize;
            Buffer.MemoryCopy((void*)pcm.Data, (void*)frame.Data[0], size, size);
        }

        var converted = swr.ConvertFrame(frame, out _, out _);
        converted.Pts = ap.nextPts;
        ap.nextPts += frame.NbSamples;

        return converted;
    }

    private static async ValueTask<bool> WriteAudioFrame(
        MediaWriter oc, SampleConverter swr, MediaEncoder encoder, MediaStream stream,
        MediaFrame src, Parames ap,
        ISampleProvider sampleProvider)
    {
        var f = await GetAudioFrame(src, swr, ap, sampleProvider);
        return WriteFrame(oc, encoder, stream, f);
    }

    private static async ValueTask<MediaFrame?> GetVideoFrame(
        MediaFrame src, PixelConverter sws, Parames vp,
        IFrameProvider frameProvider)
    {
        if (vp.nextPts > frameProvider.FrameCount)
            return null;

        using var bitmap = await frameProvider.RenderFrame(vp.nextPts);
        unsafe
        {
            Buffer.MemoryCopy((void*)bitmap.Data, (void*)src.Data[0], bitmap.ByteCount, bitmap.ByteCount);
        }

        var o = sws.ConvertFrame(src);
        o.Pts = vp.nextPts;
        vp.nextPts += 1;
        return o;
    }

    private static async ValueTask<bool> WriteVideoFrame(
        MediaWriter oc, PixelConverter sws, MediaEncoder encoder,
        MediaStream stream, MediaFrame src,
        Parames vp, IFrameProvider frameProvider)
    {
        return WriteFrame(oc, encoder, stream, await GetVideoFrame(src, sws, vp, frameProvider));
    }

    private static unsafe bool WriteFrame(MediaWriter oc, MediaEncoder encoder, MediaStream stream, MediaFrame? frame)
    {
        foreach (var pkt in encoder.EncodeFrame(frame))
        {
            pkt.StreamIndex = stream.Index;
            // Console.WriteLine(
            //     $"pts:{pkt.Pts} pts_time:{0} dst:{pkt.Dts} dts_time:{0} duration:{pkt.Duration} duration_time:{0} stream_index:{streamIndex}");
            ffmpeg.av_packet_rescale_ts(pkt, encoder.AVCodecContext.time_base, stream.TimeBase);
            oc.WritePacket(pkt);
        }

        return frame != null;
    }
}

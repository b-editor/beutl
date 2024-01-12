using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;

using FFmpeg.AutoGen;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed unsafe class FFmpegWriter : MediaWriter
{
    private readonly AVFormatContext* _formatContext;
    private AVCodec* _videoCodec;
    private AVCodecID _audioCodecId;
    private AVStream* _videoStream;
    private AVCodecContext* _videoCodecContext;
    private AVFrame* _videoFrame;
    private AVPacket* _videoPacket;
    private SwsContext* _swsContext;
    private long _videoNowFrame;
    private AVDictionary* _dictionary;
    private AVSampleFormat _sampleFmt;
    private int _sampleCount;
    private readonly FileStream _pcmStream;
    private readonly string _outputFile;
    private readonly string _pcmfile;
    private int _inputSampleRate;
    private readonly string _formatName;

    private static bool TryGetEnum<TEnum>(JsonObject? jobj, string key, out TEnum result)
        where TEnum : struct
    {
        result = default;
        return jobj?.TryGetPropertyValue(key, out JsonNode? node) == true
            && node is JsonValue value
            && value.TryGetValue(out string? str)
            && Enum.TryParse(str, out result);
    }

    private static bool TryGetInt(JsonObject? jobj, string key, out int result)
    {
        result = default;
        return jobj?.TryGetPropertyValue(key, out JsonNode? node) == true
            && node is JsonValue value
            && value.TryGetValue(out result);
    }

    private static bool TryGetString(JsonObject? jobj, string key, [NotNullWhen(true)] out string? result)
    {
        result = default;
        return jobj?.TryGetPropertyValue(key, out JsonNode? node) == true
            && node is JsonValue value
            && value.TryGetValue(out result);
    }

    public FFmpegWriter(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
        : base(videoConfig, audioConfig)
    {
        try
        {
            _outputFile = file;
            AVOutputFormat* format = ffmpeg.av_guess_format(null, Path.GetFileName(file), null);
            if (format == null)
                throw new Exception("av_guess_format failed");

            _formatContext = ffmpeg.avformat_alloc_context();
            _formatContext->oformat = format;

            int bufferSize = 1024;
            Span<byte> buffer = new byte[bufferSize];
            int length = 0;
            while (format->name[length] != 0 && length < bufferSize)
            {
                buffer[length] = format->name[length];
                length++;
            }

            _formatName = System.Text.Encoding.UTF8.GetString(buffer.Slice(0, length));

            CreateVideoStream(format);
            InitSwsContext();
            CreateVideoFrame();

            ReadAudioConfig(format);

            ffmpeg.avio_open(&_formatContext->pb, file, ffmpeg.AVIO_FLAG_WRITE)
                .ThrowIfError("avio_open failed");

            ffmpeg.avformat_write_header(_formatContext, null)
                .ThrowIfError("avformat_write_header faild");

            _pcmfile = Path.GetTempFileName();
            _pcmStream = new FileStream(_pcmfile, FileMode.Create);
        }
        catch
        {
            throw;
        }
    }

    public override long NumberOfFrames => _videoNowFrame;

    public override long NumberOfSamples { get; }

    public override bool AddAudio(IPcm sound)
    {
        if (_inputSampleRate == 0)
        {
            _inputSampleRate = sound.SampleRate;
        }

        if (_inputSampleRate != sound.SampleRate)
        {
            throw new InvalidOperationException("Invalid SampleRate");
        }

        using Pcm<Stereo32BitFloat> buffer = sound.Convert<Stereo32BitFloat>();

        Span<byte> bytes = MemoryMarshal.AsBytes(buffer.DataSpan);
        byte[] bytesArray = bytes.ToArray();
        _pcmStream.Write(bytesArray);

        _sampleCount += sound.NumSamples;

        return true;
    }

    public override bool AddVideo(IBitmap image)
    {
        if (image.PixelType != typeof(Bgra8888))
            throw new InvalidOperationException("Unsupported pixel type.");

        UpdateSwsContext(new PixelSize(image.Width, image.Height));

        int output_linesize = image.Width * 4;
        byte*[] src_data = [(byte*)image.Data, null, null, null];
        int[] src_linesize = [output_linesize, 0, 0, 0];
        ffmpeg.sws_scale(
            _swsContext,
            src_data,
            src_linesize,
            0,
            image.Height,
            _videoFrame->data,
            _videoFrame->linesize);

        _videoFrame->pts = _videoNowFrame++;
        //_videoFrame->key_frame = 0;
        _videoFrame->flags &= ~ffmpeg.AV_FRAME_FLAG_KEY;
        _videoFrame->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;

        PushFrame(_videoCodecContext, _videoStream, _videoFrame, _videoPacket);

        return true;
    }

    private void PushFrame(AVCodecContext* codecContext, AVStream* stream, AVFrame* frame, AVPacket* packet)
    {
        ffmpeg.avcodec_send_frame(codecContext, frame)
            .ThrowIfError("avcodec_send_frame failed");

        while (ffmpeg.avcodec_receive_packet(codecContext, packet) == 0)
        {
            ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, stream->time_base);

            packet->stream_index = stream->index;

            ffmpeg.av_interleaved_write_frame(_formatContext, packet)
                .ThrowIfError("av_interleaved_write_frame failed");
        }

        ffmpeg.av_packet_unref(packet);
    }

    private static void FreePacket(ref AVPacket* packet)
    {
        AVPacket* local = packet;
        ffmpeg.av_packet_free(&local);
        packet = local;
    }

    private static void CloseCodec(ref AVCodecContext* codecContext)
    {
        ffmpeg.avcodec_close(codecContext);
        ffmpeg.av_free(codecContext);

        codecContext = null;
    }

    private static void FreeFrame(ref AVFrame* frame)
    {
        AVFrame* local = frame;
        ffmpeg.av_frame_free(&local);
        frame = local;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        CloseVideo();
        CloseAudio();
    }

    private void CloseVideo()
    {
        // Close video
        FlushEncoder(_videoCodecContext, _videoStream, _videoPacket);
        FreePacket(ref _videoPacket);
        CloseCodec(ref _videoCodecContext);
        FreeFrame(ref _videoFrame);

        ffmpeg.sws_freeContext(_swsContext);

        ffmpeg.av_write_trailer(_formatContext).ThrowIfError("av_write_trailer failed");
        ffmpeg.avio_close(_formatContext->pb).ThrowIfError("avio_close failed");

        ffmpeg.avformat_free_context(_formatContext);

        fixed (AVDictionary** dictionary = &_dictionary)
        {
            ffmpeg.av_dict_free(dictionary);
        }
        _dictionary = null;
    }

    private void CloseAudio()
    {
        _pcmStream.Dispose();
        string ffmpegPath = FFmpegLoader.GetExecutable();

        string audiofile = Path.GetTempFileName();
        string sampleFormat = ffmpeg.av_get_sample_fmt_name(_sampleFmt);

        string codec = ffmpeg.avcodec_get_name(_audioCodecId);
        int sampleRate = AudioConfig.SampleRate;
        int channels = AudioConfig.Channels;
        int bitrate = AudioConfig.Bitrate;
        using (Process process1 = Process.Start(new ProcessStartInfo(
            ffmpegPath,
            $"-nostdin -f f32le -ar {_inputSampleRate} -ac 2 -i \"{_pcmfile}\" " +
            $"-sample_fmt {sampleFormat} -ar {sampleRate} -ac {channels} -ab {bitrate} -f {_formatName} -c {codec} -y \"{audiofile}\"")
        {
            CreateNoWindow = true,
            RedirectStandardError = true
        })!)
        {
            process1.WaitForExit();
            CheckProcessError(process1);
        }

        string tmpvideo = Path.ChangeExtension(Path.GetTempFileName(), Path.GetExtension(_outputFile));

        File.Copy(_outputFile, tmpvideo);
        File.Delete(_outputFile);

        using (Process process2 = Process.Start(new ProcessStartInfo(
            ffmpegPath,
            $"-nostdin -i \"{tmpvideo}\" -i \"{audiofile}\" -c:v copy -c:a copy -map 0:v:0 -map 1:a:0 \"{_outputFile}\"")
        {
            CreateNoWindow = true,
            //Todo: 何故かWaitForExitで終了しなくなる
            //RedirectStandardError = true
        })!)
        {
            process2.WaitForExit();
            CheckProcessError(process2);
        }

        File.Delete(audiofile);
        File.Delete(tmpvideo);
        File.Delete(_pcmfile);
    }

    private static void CheckProcessError(Process process)
    {
        if (process.ExitCode != 0)
        {
            throw new Exception($"FFmpeg exited with exit code {process.ExitCode}.\n\n{process.StandardError.ReadToEnd()}");
        }
    }

    private void FlushEncoder(AVCodecContext* codecContext, AVStream* stream, AVPacket* packet)
    {
        while (true)
        {
            ffmpeg.avcodec_send_frame(codecContext, null);

            if (ffmpeg.avcodec_receive_packet(codecContext, packet) == 0)
            {
                ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, stream->time_base);
                packet->stream_index = stream->index;
                ffmpeg.av_interleaved_write_frame(_formatContext, packet);
            }
            else
            {
                break;
            }

            ffmpeg.av_packet_unref(packet);
        }
    }

    private void CreateVideoStream(AVOutputFormat* format)
    {
        var codecOptions = VideoConfig.CodecOptions as JsonObject;
        AVCodecID codecId = format->video_codec;

        if (TryGetString(codecOptions, "Codec", out string? codecStr))
        {
            AVCodecDescriptor* desc = ffmpeg.avcodec_descriptor_get_by_name(codecStr);
            if (desc != null
                && desc->type == AVMediaType.AVMEDIA_TYPE_VIDEO
                && desc->id != AVCodecID.AV_CODEC_ID_NONE)
            {
                codecId = desc->id;
            }
        }

        _videoCodec = ffmpeg.avcodec_find_encoder(codecId);
        if (_videoCodec == null)
            throw new Exception("avcodec_find_encoder failed");

        if (_videoCodec->type != AVMediaType.AVMEDIA_TYPE_VIDEO)
            throw new Exception($"'{codecId}' is not for video.");

        _videoStream = ffmpeg.avformat_new_stream(_formatContext, _videoCodec);
        if (_videoStream == null)
            throw new Exception("avformat_new_stream failed");

        int framerateDen = (int)VideoConfig.FrameRate.Denominator;
        int framerateNum = (int)VideoConfig.FrameRate.Numerator;
        _videoStream->time_base.num = framerateDen;
        _videoStream->time_base.den = framerateNum;
        _videoStream->r_frame_rate.num = framerateNum;
        _videoStream->r_frame_rate.den = framerateDen;

        _videoCodecContext = ffmpeg.avcodec_alloc_context3(_videoCodec);
        if (_videoCodecContext == null)
            throw new Exception("avcodec_alloc_context3 failed");

        AVPixelFormat videoPixFmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
        if (TryGetString(codecOptions, "Format", out string? fmtStr))
        {
            AVPixelFormat pixFmt = ffmpeg.av_get_pix_fmt(fmtStr);
            if (pixFmt != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                videoPixFmt = pixFmt;
            }
        }

        _videoStream->codecpar->codec_id = codecId;
        _videoStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _videoStream->codecpar->width = VideoConfig.DestinationSize.Width;
        _videoStream->codecpar->height = VideoConfig.DestinationSize.Height;
        _videoStream->codecpar->format = (int)videoPixFmt;
        _videoStream->codecpar->bit_rate = VideoConfig.Bitrate;

        ffmpeg.avcodec_parameters_to_context(_videoCodecContext, _videoStream->codecpar)
            .ThrowIfError("avcodec_parameters_to_context failed");

        _videoCodecContext->time_base = _videoStream->time_base;
        _videoCodecContext->framerate = _videoStream->r_frame_rate;
        _videoCodecContext->gop_size = VideoConfig.KeyframeRate;
        _videoCodecContext->thread_count = Math.Min(Environment.ProcessorCount, 16);

        AVDictionary* dictionary = null;
        string preset = TryGetString(codecOptions, "Preset", out string? presetStr) ? presetStr : "medium";
        string crf = TryGetString(codecOptions, "Crf", out string? crfStr) ? crfStr : "22";
        string profile = TryGetString(codecOptions, "Profile", out string? profileStr) ? profileStr : "high";
        string level = TryGetString(codecOptions, "Level", out string? levelStr) ? levelStr : "4.0";
        ffmpeg.av_dict_set(&dictionary, "preset", preset, 0);
        ffmpeg.av_dict_set(&dictionary, "crf", crf, 0);
        ffmpeg.av_dict_set(&dictionary, "profile", profile, 0);
        ffmpeg.av_dict_set(&dictionary, "level", level, 0);

        ffmpeg.avcodec_open2(_videoCodecContext, _videoCodec, &dictionary)
            .ThrowIfError("avcodec_open2 failed");

        ffmpeg.avcodec_parameters_from_context(_videoStream->codecpar, _videoCodecContext)
            .ThrowIfError("avcodec_parameters_from_context failed");

        if ((_formatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
        {
            _videoCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
        }
    }

    private void ReadAudioConfig(AVOutputFormat* format)
    {
        var codecOptions = AudioConfig.CodecOptions as JsonObject;

        _audioCodecId = format->audio_codec;
        if (TryGetString(codecOptions, "Codec", out string? codecStr))
        {
            AVCodecDescriptor* desc = ffmpeg.avcodec_descriptor_get_by_name(codecStr);
            if (desc != null
                && desc->type == AVMediaType.AVMEDIA_TYPE_AUDIO
                && desc->id == AVCodecID.AV_CODEC_ID_NONE)
            {
                _audioCodecId = desc->id;
            }
        }

        if (TryGetString(codecOptions, "Format", out string? formatStr))
        {
            AVSampleFormat sformat = ffmpeg.av_get_sample_fmt(formatStr);
            if (sformat != AVSampleFormat.AV_SAMPLE_FMT_NONE)
            {
                _sampleFmt = sformat;
            }
        }
    }

    private void CreateVideoFrame()
    {
        _videoFrame = ffmpeg.av_frame_alloc();
        _videoFrame->width = _videoCodecContext->width;
        _videoFrame->height = _videoCodecContext->height;
        _videoFrame->format = (int)_videoCodecContext->pix_fmt;

        ffmpeg.av_frame_get_buffer(_videoFrame, 32)
            .ThrowIfError("av_frame_get_buffer failed");

        _videoPacket = ffmpeg.av_packet_alloc();
        _videoPacket->stream_index = -1;
    }

    private static AVSampleFormat ToSampleFormat(IPcm pcm, bool planar = true)
    {
        if (planar)
        {
            return pcm.SampleType.Name switch
            {
                nameof(Stereo32BitFloat)
                    or nameof(Monaural32BitFloat) => AVSampleFormat.AV_SAMPLE_FMT_FLTP,
                nameof(Stereo16BitInteger)
                    or nameof(Monaural16BitInteger) => AVSampleFormat.AV_SAMPLE_FMT_S16P,
                nameof(Stereo32BitInteger)
                    or nameof(Monaural32BitInteger) => AVSampleFormat.AV_SAMPLE_FMT_S32P,
                _ => throw new NotImplementedException()
            };
        }
        else
        {
            return pcm.SampleType.Name switch
            {
                nameof(Stereo32BitFloat)
                    or nameof(Monaural32BitFloat) => AVSampleFormat.AV_SAMPLE_FMT_FLT,
                nameof(Stereo16BitInteger)
                    or nameof(Monaural16BitInteger) => AVSampleFormat.AV_SAMPLE_FMT_S16,
                nameof(Stereo32BitInteger)
                    or nameof(Monaural32BitInteger) => AVSampleFormat.AV_SAMPLE_FMT_S32,
                _ => throw new NotImplementedException()
            };
        }
    }

    private static AVFrame* CreateAudioFrame(IPcm pcm, long presentationTimestamp, long decodingTimestamp)
    {
        AVFrame* frame = ffmpeg.av_frame_alloc();

        frame->sample_rate = pcm.SampleRate;
        ffmpeg.av_channel_layout_default(&frame->ch_layout, pcm.NumChannels);

        frame->nb_samples = pcm.NumSamples;

        AVSampleFormat sampleFormat = ToSampleFormat(pcm, false);
        frame->format = (int)sampleFormat;

        frame->pts = presentationTimestamp;
        frame->pkt_dts = decodingTimestamp;

        int len = (int)(pcm.NumSamples * pcm.SampleSize * pcm.NumChannels);
        ffmpeg.avcodec_fill_audio_frame(frame, pcm.NumChannels, sampleFormat, (byte*)pcm.Data, len, 0);

        return frame;
    }

    private AVFrame* CreateAudioFrame(int numSamples, long presentationTimestamp, long decodingTimestamp)
    {
        AVFrame* frame = ffmpeg.av_frame_alloc();

        frame->sample_rate = AudioConfig.SampleRate;
        ffmpeg.av_channel_layout_default(&frame->ch_layout, AudioConfig.Channels);

        frame->format = (int)_sampleFmt;
        frame->nb_samples = numSamples;

        frame->pts = presentationTimestamp;
        frame->pkt_dts = decodingTimestamp;

        ffmpeg.av_frame_get_buffer(frame, 0)
            .ThrowIfError("av_frame_get_buffer failed");

        return frame;
    }

    private void InitSwsContext()
    {
        int scaleMode = VideoConfig.SourceSize == VideoConfig.DestinationSize ? ffmpeg.SWS_POINT : ffmpeg.SWS_BICUBIC;
        _swsContext = ffmpeg.sws_getContext(
            VideoConfig.SourceSize.Width,
            VideoConfig.SourceSize.Height,
            AVPixelFormat.AV_PIX_FMT_BGRA,
            _videoCodecContext->width,
            _videoCodecContext->height,
            _videoCodecContext->pix_fmt,
            // scaling_algorithm
            scaleMode,
            null,
            null,
            null);

        if (_swsContext == null)
            throw new Exception("sws_getContext failed");
    }

    private void UpdateSwsContext(PixelSize sourceSize)
    {
        int scaleMode = sourceSize == VideoConfig.DestinationSize ? ffmpeg.SWS_POINT : ffmpeg.SWS_BICUBIC;
        _swsContext = ffmpeg.sws_getCachedContext(
            _swsContext,
            sourceSize.Width,
            sourceSize.Height,
            AVPixelFormat.AV_PIX_FMT_BGRA,
            _videoCodecContext->width,
            _videoCodecContext->height,
            _videoCodecContext->pix_fmt,
            scaleMode,
            null,
            null,
            null);
    }
}

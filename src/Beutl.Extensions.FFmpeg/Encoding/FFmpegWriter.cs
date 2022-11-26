using System.Diagnostics;
using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Encoding;
using Beutl.Media.Pixel;

using FFmpeg.AutoGen;

namespace Beutl.Extensions.FFmpeg.Encoding;

public sealed unsafe class FFmpegWriter : MediaWriter
{
    private readonly AVFormatContext* _formatContext;
    private AVCodec* _videoCodec;
    private AVCodec* _audioCodec;
    private AVStream* _videoStream;
    private AVStream* _audioStream;
    private AVCodecContext* _videoCodecContext;
    private AVCodecContext* _audioCodecContext;
    private AVFrame* _videoFrame;
    private AVFrame* _audioFrame;
    private AVPacket* _videoPacket;
    private AVPacket* _audioPacket;
    private SwsContext* _swsContext;
    private SwrContext* _swrContext;
    private long _videoNowFrame;
    private long _audioNowFrame;
    private AVDictionary* _dictionary;

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

    public FFmpegWriter(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
        : base(videoConfig, audioConfig)
    {
        try
        {
            AVOutputFormat* format = ffmpeg.av_guess_format(null, Path.GetFileName(file), null);
            if (format == null)
                throw new Exception("av_guess_format failed");

            _formatContext = ffmpeg.avformat_alloc_context();
            _formatContext->oformat = format;

            CreateVideoStream(format);
            InitSwsContext();
            CreateVideoFrame();

            CreateAudioStream(format, out var sampleFmt);
            CreateAudioFrame(sampleFmt);

            if (ffmpeg.avio_open(&_formatContext->pb, file, ffmpeg.AVIO_FLAG_WRITE) < 0)
                throw new Exception("avio_open failed");

            if (ffmpeg.avformat_write_header(_formatContext, null) < 0)
                throw new Exception("avformat_write_header faild");
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
        if (sound.SampleType != typeof(Stereo32BitFloat))
            throw new InvalidOperationException("Unsupported sample type.");

        UpdateSwrContext(AVSampleFormat.AV_SAMPLE_FMT_FLTP, 2, sound.SampleRate);
        byte* src_data = (byte*)sound.Data;

        fixed (byte** audioframedata = (byte*[])_audioFrame->data)
        {
            var _samplesReturn = ffmpeg.swr_convert(
                s: _swrContext,
                @out: audioframedata,
                out_count: _audioFrame->nb_samples,
                @in: &src_data,
                in_count: sound.NumSamples);

            Debug.Assert((IntPtr)src_data == sound.Data);
        }

        _audioFrame->pts = _audioNowFrame++;

        PushFrame(_audioCodecContext, _audioStream, _audioFrame, _audioPacket);

        return true;
    }

    public override bool AddVideo(IBitmap image)
    {
        if (image.PixelType != typeof(Bgra8888))
            throw new InvalidOperationException("Unsupported pixel type.");

        UpdateSwsContext(new PixelSize(image.Width, image.Height));

        int output_linesize = image.Width * 4;
        byte*[] src_data = { (byte*)image.Data, null, null, null };
        int[] src_linesize = { output_linesize, 0, 0, 0 };
        ffmpeg.sws_scale(
            _swsContext,
            src_data,
            src_linesize,
            0,
            image.Height,
            _videoFrame->data,
            _videoFrame->linesize);

        _videoFrame->pts = _videoNowFrame++;
        _videoFrame->key_frame = 0;
        _videoFrame->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;

        PushFrame(_videoCodecContext, _videoStream, _videoFrame, _videoPacket);

        return true;
    }

    private void PushFrame(AVCodecContext* codecContext, AVStream* stream, AVFrame* frame, AVPacket* packet)
    {
        if (ffmpeg.avcodec_send_frame(codecContext, frame) != 0)
            throw new Exception("avcodec_send_frame failed");

        while (ffmpeg.avcodec_receive_packet(codecContext, packet) == 0)
        {
            ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, stream->time_base);

            packet->stream_index = stream->index;

            if (ffmpeg.av_interleaved_write_frame(_formatContext, packet) != 0)
            {
                throw new Exception("av_interleaved_write_frame failed");
            }
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

        // Close video
        FlushEncoder(_videoCodecContext, _videoStream, _videoPacket);
        FreePacket(ref _videoPacket);
        CloseCodec(ref _videoCodecContext);
        FreeFrame(ref _videoFrame);

        // Close audio
        FlushEncoder(_audioCodecContext, _audioStream, _audioPacket);
        FreePacket(ref _audioPacket);
        CloseCodec(ref _audioCodecContext);
        FreeFrame(ref _audioFrame);

        ffmpeg.sws_freeContext(_swsContext);

        fixed (SwrContext** ptr = &_swrContext)
        {
            ffmpeg.swr_free(ptr);
        }

        ffmpeg.av_write_trailer(_formatContext);
        ffmpeg.avio_close(_formatContext->pb);

        ffmpeg.avformat_free_context(_formatContext);

        fixed (AVDictionary** dictionary = &_dictionary)
        {
            ffmpeg.av_dict_free(dictionary);
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
        AVCodecID codecId = TryGetEnum(codecOptions, "Codec", out AVCodecID codecId1) && codecId1 != AVCodecID.AV_CODEC_ID_NONE
            ? codecId1
            : format->video_codec;

        _videoCodec = ffmpeg.avcodec_find_encoder(codecId);
        if (_videoCodec == null)
            throw new Exception("avcodec_find_encoder failed");

        if (_videoCodec->type != AVMediaType.AVMEDIA_TYPE_VIDEO)
            throw new Exception($"{codecId}は動画用ではありません。");

        _videoStream = ffmpeg.avformat_new_stream(_formatContext, _videoCodec);
        if (_videoStream == null)
            throw new Exception("avformat_new_stream failed");

        var framerateDen = (int)VideoConfig.FrameRate.Denominator;
        var framerateNum = (int)VideoConfig.FrameRate.Numerator;
        _videoStream->time_base.num = framerateDen;
        _videoStream->time_base.den = framerateNum;
        _videoStream->r_frame_rate.num = framerateNum;
        _videoStream->r_frame_rate.den = framerateDen;

        _videoCodecContext = ffmpeg.avcodec_alloc_context3(_videoCodec);
        if (_videoCodecContext == null)
            throw new Exception("avcodec_alloc_context3 failed");

        AVPixelFormat videoPixFmt = TryGetEnum(codecOptions, "Format", out AVPixelFormat videoPixFmt1)
            ? videoPixFmt1
            : AVPixelFormat.AV_PIX_FMT_YUV420P;

        _videoStream->codecpar->codec_id = codecId;
        _videoStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _videoStream->codecpar->width = VideoConfig.DestinationSize.Width;
        _videoStream->codecpar->height = VideoConfig.DestinationSize.Height;
        _videoStream->codecpar->format = (int)videoPixFmt;
        _videoStream->codecpar->bit_rate = VideoConfig.Bitrate;

        if (ffmpeg.avcodec_parameters_to_context(_videoCodecContext, _videoStream->codecpar) < 0)
            throw new Exception("avcodec_parameters_to_context failed");

        _videoCodecContext->time_base = _videoStream->time_base;
        _videoCodecContext->framerate = _videoStream->r_frame_rate;
        _videoCodecContext->gop_size = VideoConfig.KeyframeRate;

        AVDictionary* dictionary = null;
        ffmpeg.av_dict_set(&dictionary, "preset", "medium", 0);
        ffmpeg.av_dict_set(&dictionary, "crf", "22", 0);
        ffmpeg.av_dict_set(&dictionary, "profile", "high", 0);
        ffmpeg.av_dict_set(&dictionary, "level", "4.0", 0);

        if (ffmpeg.avcodec_open2(_videoCodecContext, _videoCodec, &dictionary) < 0)
            throw new Exception("avcodec_open2 failed");

        if (ffmpeg.avcodec_parameters_from_context(_videoStream->codecpar, _videoCodecContext) < 0)
            throw new Exception("avcodec_parameters_from_context failed");

        if ((_formatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
        {
            _videoCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
        }
    }

    private void CreateAudioStream(AVOutputFormat* format, out AVSampleFormat sampleFmt)
    {
        var codecOptions = AudioConfig.CodecOptions as JsonObject;
        AVCodecID codecId = TryGetEnum(codecOptions, "Codec", out AVCodecID codecId1) && codecId1 != AVCodecID.AV_CODEC_ID_NONE
            ? codecId1
            : format->audio_codec;

        _audioCodec = ffmpeg.avcodec_find_encoder(codecId);
        if (_audioCodec == null)
            throw new Exception("avcodec_find_encoder failed");

        if (_audioCodec->type != AVMediaType.AVMEDIA_TYPE_AUDIO)
            throw new Exception($"{codecId}は音声用ではありません。");

        _audioStream = ffmpeg.avformat_new_stream(_formatContext, _audioCodec);
        if (_audioStream == null)
            throw new Exception("avformat_new_stream failed");

        _audioCodecContext = ffmpeg.avcodec_alloc_context3(_audioCodec);
        if (_audioCodecContext == null)
            throw new Exception("avcodec_alloc_context3 failed");

        sampleFmt = TryGetEnum(codecOptions, "Format", out AVSampleFormat sampleFmt1)
            ? sampleFmt1
            : AVSampleFormat.AV_SAMPLE_FMT_FLTP;

        int frameSize = TryGetInt(codecOptions, "SamplesPerFrame", out int frameSize1)
            ? frameSize1
            : 2205;

        _audioStream->codecpar->codec_id = codecId;
        _audioStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
        _audioStream->codecpar->sample_rate = AudioConfig.SampleRate;
        _audioStream->codecpar->frame_size = frameSize;
        _audioStream->codecpar->format = (int)sampleFmt;
        _audioStream->codecpar->bit_rate = AudioConfig.Bitrate;
        ffmpeg.av_channel_layout_default(&_audioStream->codecpar->ch_layout, AudioConfig.Channels);

        if (ffmpeg.avcodec_parameters_to_context(_audioCodecContext, _audioStream->codecpar) < 0)
            throw new Exception("avcodec_parameters_to_context failed");

        _audioCodecContext->time_base.num = frameSize;
        _audioCodecContext->time_base.den = AudioConfig.SampleRate;

        if (ffmpeg.avcodec_open2(_audioCodecContext, _audioCodec, null) < 0)
            throw new Exception("avcodec_open2 failed");

        if (ffmpeg.avcodec_parameters_from_context(_audioStream->codecpar, _audioCodecContext) < 0)
            throw new Exception("avcodec_parameters_from_context failed");

        if ((_formatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
        {
            _audioCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
        }
    }

    private void CreateVideoFrame()
    {
        _videoFrame = ffmpeg.av_frame_alloc();
        _videoFrame->width = _videoCodecContext->width;
        _videoFrame->height = _videoCodecContext->height;
        _videoFrame->format = (int)_videoCodecContext->pix_fmt;

        if (ffmpeg.av_frame_get_buffer(_videoFrame, 32) < 0)
            throw new Exception("av_frame_get_buffer failed");

        _videoPacket = ffmpeg.av_packet_alloc();
        _videoPacket->stream_index = -1;
    }

    private void CreateAudioFrame(AVSampleFormat sampleFmt)
    {
        _audioFrame = ffmpeg.av_frame_alloc();

        _audioFrame->sample_rate = AudioConfig.SampleRate;
        _audioFrame->ch_layout = _audioCodecContext->ch_layout;
        _audioFrame->format = (int)_audioCodecContext->sample_fmt;
        _audioFrame->nb_samples = _audioCodecContext->frame_size;

        _audioFrame->pts = 0;
        _audioFrame->pkt_dts = 0;

        if (ffmpeg.av_frame_get_buffer(_audioFrame, 32) < 0)
            throw new Exception("av_frame_get_buffer failed");

        _audioPacket = ffmpeg.av_packet_alloc();
        _audioPacket->stream_index = -1;
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

    private void UpdateSwrContext(AVSampleFormat sampleFmt, int channels, int sampleRate)
    {
        fixed (SwrContext** swrContext = &_swrContext)
        {
            AVChannelLayout layout = default;
            ffmpeg.av_channel_layout_default(&layout, channels);

            if (ffmpeg.swr_alloc_set_opts2(
                swrContext,
                &_audioCodecContext->ch_layout,
                _audioCodecContext->sample_fmt,
                _audioCodecContext->sample_rate,
                &layout,
                sampleFmt,
                sampleRate,
                0,
                null) < 0)
            {
                throw new Exception("swr_alloc_set_opts2 failed");
            }

            if (ffmpeg.swr_init(_swrContext) < 0)
            {
                throw new Exception("swr_init error.");
            }
        }
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

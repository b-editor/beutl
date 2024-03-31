using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Pixel;

using FFmpeg.AutoGen;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed unsafe class FFmpegInprocessVideoWriter : IFFmpegVideoWriter
{
    private readonly string _outputFile;
    private readonly AVFormatContext* _formatContext;
    private AVCodec* _videoCodec;
    private AVStream* _videoStream;
    private AVCodecContext* _videoCodecContext;
    private AVFrame* _videoFrame;
    private AVPacket* _videoPacket;
    private SwsContext* _swsContext;
    private AVDictionary* _dictionary;

    public FFmpegInprocessVideoWriter(string file, FFmpegVideoEncoderSettings videoConfig)
    {
        try
        {
            _outputFile = file;
            VideoConfig = videoConfig;
            AVOutputFormat* format = ffmpeg.av_guess_format(null, Path.GetFileName(file), null);
            if (format == null)
                throw new Exception("av_guess_format failed");

            _formatContext = ffmpeg.avformat_alloc_context();
            _formatContext->oformat = format;

            CreateVideoStream(format);
            InitSwsContext();
            CreateVideoFrame();

            ffmpeg.avio_open(&_formatContext->pb, file, ffmpeg.AVIO_FLAG_WRITE)
                .ThrowIfError("avio_open failed");

            ffmpeg.avformat_write_header(_formatContext, null)
                .ThrowIfError("avformat_write_header faild");
        }
        catch
        {
            throw;
        }
    }

    public long NumberOfFrames { get; private set; }

    public FFmpegVideoEncoderSettings VideoConfig { get; }

    public bool AddVideo(IBitmap image)
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

        _videoFrame->pts = NumberOfFrames++;
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

    public void Dispose()
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
        AVCodecID codecId = format->video_codec;

        AVCodecDescriptor* desc = ffmpeg.avcodec_descriptor_get((AVCodecID)VideoConfig.Codec);
        if (desc != null
            && desc->type == AVMediaType.AVMEDIA_TYPE_VIDEO
            && desc->id != AVCodecID.AV_CODEC_ID_NONE)
        {
            codecId = desc->id;
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
        if (VideoConfig.Format != AVPixelFormat.AV_PIX_FMT_NONE)
        {
            videoPixFmt = VideoConfig.Format;
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
        string preset = VideoConfig.Preset;
        string crf = VideoConfig.Crf.ToString();
        string profile = VideoConfig.Profile;
        string level = "4.0";
        // string level = JsonHelper.TryGetString(codecOptions, "Level", out string? levelStr) ? levelStr : "4.0";
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

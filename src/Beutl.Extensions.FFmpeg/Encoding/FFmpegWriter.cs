using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Media;
using Beutl.Media.Audio;
using Beutl.Media.Encoding;

using FFmpeg.AutoGen;

namespace Beutl.Extensions.FFmpeg.Encoding;

public sealed unsafe class FFmpegWriter : MediaWriter
{
    private readonly AVFormatContext* _formatContext;
    private AVCodec* _videoCodec;
    private AVStream* _videoStream;
    private AVCodecContext* _videoCodecContext;
    private AVFrame* _videoFrame;
    private AVPacket* _videoPacket;
    private SwsContext* _swsContext;

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

            AVCodecID videoCodecId = videoConfig.CodecOptions.TryGetValue("Codec", out var videoCodecObj)
                && videoCodecObj is AVCodecID videoCodecId1
                ? videoCodecId1
                : ffmpeg.av_guess_codec(format, null, Path.GetFileName(file), null, AVMediaType.AVMEDIA_TYPE_VIDEO);
            
            _videoCodec = ffmpeg.avcodec_find_encoder(videoCodecId);
            if (_videoCodec == null)
                throw new Exception("avcodec_find_encoder failed");

            if (_videoCodec->type != AVMediaType.AVMEDIA_TYPE_VIDEO)
                throw new Exception($"{videoCodecId}は動画用ではありません。");


            _videoStream = ffmpeg.avformat_new_stream(_formatContext, _videoCodec);
            if (_videoStream == null)
                throw new Exception("avformat_new_stream failed");

            var framerate = new AVRational()
            {
                den = (int)videoConfig.FrameRate.Denominator,
                num = (int)videoConfig.FrameRate.Numerator,
            };
            var timebase = new AVRational()
            {
                num = (int)videoConfig.FrameRate.Denominator,
                den = (int)videoConfig.FrameRate.Numerator,
            };
            _videoStream->time_base = timebase;
            _videoStream->r_frame_rate = framerate;

            _videoCodecContext = ffmpeg.avcodec_alloc_context3(_videoCodec);
            if (_videoCodecContext == null)
                throw new Exception("avcodec_alloc_context3 failed");

            AVPixelFormat videoPixFmt = videoConfig.CodecOptions.TryGetValue("Format", out var videoPixFmtObj)
                && videoCodecObj is AVPixelFormat videoPixFmt1
                ? videoPixFmt1
                : AVPixelFormat.AV_PIX_FMT_YUV420P;

            _videoStream->codecpar->codec_id = videoCodecId;
            _videoStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
            _videoStream->codecpar->width = videoConfig.DestinationSize.Width;
            _videoStream->codecpar->height = videoConfig.DestinationSize.Height;
            _videoStream->codecpar->format = (int)videoPixFmt;
            _videoStream->codecpar->bit_rate = videoConfig.Bitrate;

            if (ffmpeg.avcodec_parameters_to_context(_videoCodecContext, _videoStream->codecpar) < 0)
                throw new Exception("avcodec_parameters_to_context failed");

            _videoCodecContext->time_base = _videoStream->time_base;
            _videoCodecContext->framerate = _videoStream->r_frame_rate;
            _videoCodecContext->gop_size = videoConfig.KeyframeRate;

            if (ffmpeg.avcodec_open2(_videoCodecContext, _videoCodec, null) < 0)
                throw new Exception("avcodec_parameters_to_context failed");

            _swsContext = ffmpeg.sws_getContext(
                videoConfig.SourceSize.Width,
                videoConfig.SourceSize.Height,
                AVPixelFormat.AV_PIX_FMT_BGRA,
                _videoCodecContext->width,
                _videoCodecContext->height,
                _videoCodecContext->pix_fmt,
                // scaling_algorithm
                ffmpeg.SWS_BICUBIC,
                null,
                null,
                null);

            if (_swsContext == null)
                throw new Exception("sws_getContext failed");

            _videoFrame = ffmpeg.av_frame_alloc();
            _videoFrame->width = videoConfig.DestinationSize.Width;
            _videoFrame->height = videoConfig.DestinationSize.Height;
            _videoFrame->format = (int)videoPixFmt;

            if (ffmpeg.av_frame_get_buffer(_videoFrame, 32) < 0)
                throw new Exception("av_frame_get_buffer failed");


            _videoPacket = ffmpeg.av_packet_alloc();

        }
        catch
        {

        }
    }

    public override long NumberOfFrames { get; }

    public override long NumberOfSamples { get; }

    public override bool AddAudio(ISound sound) => throw new NotImplementedException();

    public override bool AddVideo(IBitmap image) => throw new NotImplementedException();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}

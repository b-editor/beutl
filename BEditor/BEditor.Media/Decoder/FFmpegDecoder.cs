using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoder
{
    public unsafe class FFmpegDecoder : IVideoDecoder
    {
        private AVFormatContext* format_context;
        private AVStream* video_stream;
        private AVCodec* codec;
        private AVCodecContext* codec_context;
        
        static FFmpegDecoder()
        {
            ffmpeg.RootPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            ffmpeg.av_register_all();
        }
        public FFmpegDecoder(string filename)
        {
            AVFormatContext* format_context = null;

            if (ffmpeg.avformat_open_input(&format_context, filename, null, null) != 0) throw new Exception();

            // find video stream
            AVStream* video_stream = null;
            for (int i = 0; i < (int)format_context->nb_streams; ++i)
            {
                if (format_context->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    video_stream = format_context->streams[i];
                    break;
                }
            }

            if (video_stream is null) throw new Exception("Video Streamが見つかりませんでした");

            // find decoder
            AVCodec* codec = ffmpeg.avcodec_find_decoder(video_stream->codecpar->codec_id);
            if (codec is null) throw new NotSupportedException();

            // alloc codec context
            AVCodecContext* codec_context = ffmpeg.avcodec_alloc_context3(codec);
            if (codec_context is null) throw new Exception("avcodec_alloc_context3 failed");

            // open codec
            if (ffmpeg.avcodec_parameters_to_context(codec_context, video_stream->codecpar) < 0)
                throw new Exception("avcodec_parameters_to_context failed");

            if (ffmpeg.avcodec_open2(codec_context, codec, null) != 0)
                throw new Exception("avcodec_open2 failed");

            this.format_context = format_context;
            this.video_stream = video_stream;
            this.codec = codec;
            this.codec_context = codec_context;
        }

        public int Fps { get; }
        public Frame FrameCount => codec_context->frame_number;
        public int Width => codec_context->width;
        public int Height => codec_context->height;
        public bool IsDisposed { get; private set; }
        //private readonly List<AVFrame> aVFrames = new();


        public void Dispose()
        {
            if (IsDisposed) return;

            fixed (AVFormatContext** format_context = &this.format_context)
            fixed (AVCodecContext** codec_context = &this.codec_context)
            {
                ffmpeg.avformat_close_input(format_context);
                ffmpeg.avcodec_close(*codec_context);

                ffmpeg.avformat_free_context(*format_context);
                ffmpeg.avcodec_free_context(codec_context);
            }

            IsDisposed = true;
        }
        public Image<BGRA32> Read(Frame frame)
        {
            var avframe = ffmpeg.av_frame_alloc();
            var framergba = ffmpeg.av_frame_alloc();
            AVPacket packet = new AVPacket();
            int count = 0;

            var bytes = ffmpeg.avpicture_get_size(AVPixelFormat.AV_PIX_FMT_BGRA, Width, Height);
            var buffer = (byte*)ffmpeg.av_malloc((ulong)(bytes * sizeof(byte)));
            ffmpeg.avpicture_fill((AVPicture*)framergba, buffer, AVPixelFormat.AV_PIX_FMT_BGRA, Width, Height);

            while (ffmpeg.av_read_frame(format_context, &packet) == 0)
            {
                if (packet.stream_index == video_stream->index)
                {
                    if (ffmpeg.avcodec_send_packet(codec_context, &packet) != 0)
                    {
                        //printf("avcodec_send_packet failed\n");
                    }
                    while (ffmpeg.avcodec_receive_frame(codec_context, avframe) == 0)
                    {
                        if (count == frame)
                        {
                            var img = new Image<BGRA32>(Width, Height);
                            var convert_ctx = ffmpeg.sws_getContext(
                                Width, Height,
                                codec_context->pix_fmt,
                                Width, Height,
                                AVPixelFormat.AV_PIX_FMT_BGRA,
                                ffmpeg.SWS_BICUBIC,
                                null, null, null);

                            ffmpeg.sws_scale(convert_ctx, avframe->data, avframe->linesize, 0, Height, framergba->data, framergba->linesize);

                            fixed (BGRA32* dst = img.Data)
                            {
                                Buffer.MemoryCopy(framergba->data[0], dst, img.DataSize, img.DataSize);
                            }

                            return img;
                        }

                        count++;
                    }
                }
                ffmpeg.av_packet_unref(&packet);
            }

            return new Image<BGRA32>(Width, Height);
        }
    }
}

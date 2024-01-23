using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;

using FFmpeg.AutoGen;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

public sealed unsafe class FFmpegReader : MediaReader
{
    private static readonly byte* s_swr_buf = (byte*)NativeMemory.AllocZeroed((nuint)(2048 * sizeof(Stereo32BitFloat)));
    private static readonly AVRational s_time_base = new() { num = 1, den = ffmpeg.AV_TIME_BASE };

#pragma warning disable IDE1006 // 命名スタイル
    private static readonly AVChannelLayout AV_CHANNEL_LAYOUT_STEREO = new()
#pragma warning restore IDE1006 // 命名スタイル
    {
        order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
        nb_channels = 2,
        u = new AVChannelLayout_u
        {
            mask = ffmpeg.AV_CH_LAYOUT_STEREO
        }
    };

    private readonly MediaOptions _options;
    private readonly FFmpegDecodingSettings _settings;
    private readonly string _file;
    private readonly AudioStreamInfo? _audioInfo;
    private readonly VideoStreamInfo? _videoInfo;
    private readonly AVFormatContext* _formatContext;
    private readonly AVStream* _audioStream;
    private readonly AVStream* _videoStream;
    private bool _hasVideo;
    private bool _hasAudio;
    private AVCodec* _videoCodec;
    private AVCodec* _audioCodec;
    private AVCodecContext* _videoCodecContext;
    private AVCodecContext* _audioCodecContext;
    private AVFrame* _videoFrame;
    private AVFrame* _audioFrame;
    private AVPacket* _videoPacket;
    private AVPacket* _audioPacket;
    private SwsContext* _swsContext;
    private SwrContext* _swrContext;
    private SwrContext* _localSwrContext;
    private long _audioNowTimestamp;
    private long _audioNextTimestamp;
    private long _videoNowFrame;
    private int _samplesReturn;
    private bool _audioSeek;
    private double _videoTimeBaseDouble;
    private double _videoAvgFrameRateDouble;

    public FFmpegReader(string file, MediaOptions options, FFmpegDecodingSettings settings)
    {
        _file = file;
        _options = options;
        _settings = settings;
        try
        {
            fixed (AVFormatContext** fmtctx = &_formatContext)
            {
                if (ffmpeg.avformat_open_input(fmtctx, file, null, null) != 0)
                {
                    throw new Exception("avformat_open_input failed");
                }
            }

            // ストリームを探す
            if (ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
            {
                throw new Exception("avformat_find_stream_info failed");
            }

            bool loadAudio = options.StreamsToLoad.HasFlag(MediaMode.Audio);
            bool loadVideo = options.StreamsToLoad.HasFlag(MediaMode.Video);
            for (int i = 0; i < (int)_formatContext->nb_streams; ++i)
            {
                if (loadVideo && !HasVideo && _formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    _videoStream = _formatContext->streams[i];
                    _hasVideo = true;
                }
                if (loadAudio && !HasAudio && _formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    _audioStream = _formatContext->streams[i];
                    _hasAudio = true;
                }
            }

            if (_videoStream == null && _audioStream == null)
            {
                return;
            }

            ConfigureVideoStream();
            ConfigureAudioStream();

            if (!HasVideo && !HasAudio)
            {
                return;
            }

            if (HasVideo)
            {
                GrabVideo();

                _videoInfo = new VideoStreamInfo(
                    new string((sbyte*)_videoCodec->long_name),
                    _videoStream->nb_frames,
                    new PixelSize(_videoCodecContext->width, _videoCodecContext->height),
                    new Rational(_videoStream->avg_frame_rate.num, _videoStream->avg_frame_rate.den))
                {
                    Duration = new Rational(_formatContext->duration, ffmpeg.AV_TIME_BASE)
                };
            }

            if (HasAudio)
            {
                // Todo: 検証
                //GrabAudio();
                _audioInfo = new AudioStreamInfo(
                    new string((sbyte*)_audioCodec->name),
                    new Rational(_formatContext->duration, ffmpeg.AV_TIME_BASE),
                    _audioCodecContext->sample_rate,
                    _audioCodecContext->ch_layout.nb_channels);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public override VideoStreamInfo VideoInfo => _videoInfo ?? throw new Exception("The stream does not exist.");

    public override AudioStreamInfo AudioInfo => _audioInfo ?? throw new Exception("The stream does not exist.");

    public override bool HasVideo => _hasVideo;

    public override bool HasAudio => _hasAudio;

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? result)
    {
        if (!(start >= _audioNowTimestamp && start < _audioNextTimestamp))
        {
            GrabAudio();

            if (!(start >= _audioNowTimestamp && start < _audioNextTimestamp))
            {
                SeekAudio(start);
            }
        }

        var sound = new Pcm<Stereo32BitFloat>(AudioInfo.SampleRate, length);
        int decoded = 0;
        bool need_grab = false;
        fixed (Stereo32BitFloat* buf = sound.DataSpan)
        fixed (AVChannelLayout* outChLayout = &AV_CHANNEL_LAYOUT_STEREO)
        fixed (SwrContext** swrCtx = &_swrContext)
        {
            while (!need_grab || GrabAudio())
            {
                // Todo: 検証 (GenerateSwrContextメソッドを作り、ConfigureAudioStreamで呼び出す)
                if (_swrContext == null)
                {
                    if (ffmpeg.swr_alloc_set_opts2(
                        swrCtx,
                        outChLayout,
                        AVSampleFormat.AV_SAMPLE_FMT_FLT,
                        AudioInfo.SampleRate,
                        &_audioCodecContext->ch_layout,
                        (AVSampleFormat)_audioFrame->format,
                        _audioFrame->sample_rate,
                        0,
                        null) < 0)
                    {
                        Debug.WriteLine("swr_alloc_set_opts2 error.");
                        break;
                    }

                    if (ffmpeg.swr_init(_swrContext) < 0)
                    {
                        Debug.WriteLine("swr_init error.");
                        break;
                    }
                }

                fixed (byte** swr_buf_ = &s_swr_buf)
                fixed (byte** audioframedata = (byte*[])_audioFrame->data)
                {
                    _samplesReturn = ffmpeg.swr_convert(
                        s: _swrContext,
                        @out: swr_buf_,
                        out_count: _audioFrame->nb_samples,
                        @in: audioframedata,
                        in_count: _audioFrame->nb_samples);
                }

                if (_samplesReturn < 0)
                {
                    Debug.WriteLine("swr_convert error.\n");
                    sound?.Dispose();
                    result = null;
                    return false;
                }

                int skip = 0;
                if ((int)_audioNowTimestamp < start)
                {
                    skip = start - (int)_audioNowTimestamp;
                }

                int len = _samplesReturn - skip;
                if (decoded + len > length)
                {
                    len = length - decoded;
                }

                if (len > 0)
                {
                    int size = sizeof(Stereo32BitFloat);
                    Buffer.MemoryCopy(s_swr_buf + (skip * size), ((byte*)buf) + (decoded * size), len * size, len * size);
                    decoded += len;
                }

                if (decoded >= length || len <= 0)
                {
                    result = Resample(sound);
                    sound.Dispose();
                    return result != null;
                }

                need_grab = skip + len >= _audioFrame->nb_samples;
            }

            //sound?.Dispose();
            //sound = null;
            //return false;
            result = Resample(sound);
            sound.Dispose();
            return result != null;
        }
    }

    private Pcm<Stereo32BitFloat>? Resample(Pcm<Stereo32BitFloat> pcm)
    {
        if (_localSwrContext == null)
        {
            fixed (AVChannelLayout* outChLayout = &AV_CHANNEL_LAYOUT_STEREO)
            fixed (SwrContext** swrCtx = &_localSwrContext)
            {
                if (ffmpeg.swr_alloc_set_opts2(
                    swrCtx,
                    outChLayout,
                    AVSampleFormat.AV_SAMPLE_FMT_FLT,
                    _options.SampleRate,
                    outChLayout,
                    AVSampleFormat.AV_SAMPLE_FMT_FLT,
                    pcm.SampleRate,
                    0,
                    null) < 0)
                {
                    Debug.WriteLine("swr_alloc_set_opts2 error.");
                    return null;
                }

                if (ffmpeg.swr_init(_localSwrContext) < 0)
                {
                    Debug.WriteLine("swr_init error.");
                    return null;
                }
            }
        }

        int bits = sizeof(Stereo32BitFloat) * 8;
        int size = (int)(_options.SampleRate * bits * pcm.DurationRational.ToDouble() / bits);
        var result = new Pcm<Stereo32BitFloat>(_options.SampleRate, size);

        byte* inputData = (byte*)pcm.Data;
        byte* outputData = (byte*)result.Data;

        int _samplesReturn = ffmpeg.swr_convert(
            s: _localSwrContext,
            @out: &outputData,
            out_count: result.NumSamples,
            @in: &inputData,
            in_count: pcm.NumSamples);

        if (_samplesReturn < 0)
        {
            Debug.WriteLine("swr_convert error.\n");
            result?.Dispose();
            result = null;
            return null;
        }

        return result;
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        if (_videoStream == null)
        {
            image = null;
            return false;
        }

        long skip = frame - _videoNowFrame;
        if (skip > 100 || skip < 0)
        {
            SeekVideo(frame);
            skip = 0;
        }

        for (int i = 0; i < skip; i++)
        {
            if (!GrabVideo())
            {
                image = null;
                return false;
            }
        }

        int width = _videoFrame->width;
        int height = _videoFrame->height;
        int output_linesize = width * 4;
        int output_size = output_linesize * height;

        int output_rowsize = _videoFrame->width * 4;

        var bmp = new Bitmap<Bgra8888>(width, height);
        Bgra8888* buf = (Bgra8888*)bmp.Data;
        byte*[] dst_data = [(byte*)buf, null, null, null];
        int[] dst_linesize = [output_linesize, 0, 0, 0];
        byte*[] src_data = _videoFrame->data;
        int[] src_linesize = _videoFrame->linesize;

        output_size = ffmpeg.sws_scale(
            _swsContext,
            src_data,
            src_linesize,
            0,
            height,
            dst_data, dst_linesize);

        image = bmp;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        fixed (AVFormatContext** fctxt = &_formatContext)
        {
            ffmpeg.avformat_close_input(fctxt);
        }

        if (_swrContext != null)
        {
            fixed (SwrContext** swr = &_swrContext)
            {
                ffmpeg.swr_free(swr);
            }

            _swrContext = null;
        }

        if (_localSwrContext != null)
        {
            fixed (SwrContext** swr = &_localSwrContext)
            {
                ffmpeg.swr_free(swr);
            }

            _localSwrContext = null;
        }

        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        if (HasVideo)
        {
            ffmpeg.av_frame_unref(_videoFrame);

            fixed (AVFrame** f = &_videoFrame)
            {
                ffmpeg.av_frame_free(f);
            }

            fixed (AVCodecContext** a = &_videoCodecContext)
            {
                ffmpeg.avcodec_free_context(a);
            }

            ffmpeg.av_packet_unref(_videoPacket);
        }

        if (HasAudio)
        {
            ffmpeg.av_frame_unref(_audioFrame);
            fixed (AVFrame** f = &_audioFrame)
            {
                ffmpeg.av_frame_free(f);
            }

            fixed (AVCodecContext** a = &_audioCodecContext)
            {
                ffmpeg.avcodec_free_context(a);
            }

            fixed (AVPacket** pkt = &_audioPacket)
            {
                ffmpeg.av_packet_free(pkt);
            }
        }
    }

    private bool GrabAudio()
    {
        ffmpeg.av_frame_unref(_audioFrame);

        //複数フレームが含まれる場合があるので残っていればデコード
        if (ffmpeg.avcodec_receive_frame(_audioCodecContext, _audioFrame) == 0)
        {
            if (_audioSeek)
            {
                _audioNowTimestamp = (long)(((_audioFrame->pts) * ffmpeg.av_q2d(_audioStream->time_base) - (_formatContext->start_time * ffmpeg.av_q2d(s_time_base))) * _audioCodecContext->sample_rate);
                _audioSeek = false;
                _audioNowTimestamp -= _audioNowTimestamp % _audioFrame->nb_samples;
            }
            else
            {
                _audioNowTimestamp = _audioNextTimestamp;
            }

            _audioNextTimestamp = _audioNowTimestamp + _audioFrame->nb_samples;
            //メモリ解放
            ffmpeg.av_packet_unref(_audioPacket);

            return true;
        }

        while (ffmpeg.av_read_frame(_formatContext, _audioPacket) == 0)
        {
            if (_audioPacket->stream_index == _audioStream->index)
            {
                if (ffmpeg.avcodec_send_packet(_audioCodecContext, _audioPacket) != 0)
                {
                    Debug.WriteLine("avcodec_send_packet failed");
                    ffmpeg.av_packet_unref(_audioPacket);
                    return false;
                }

                if (ffmpeg.avcodec_receive_frame(_audioCodecContext, _audioFrame) == 0)
                {
                    if (_audioSeek)
                    {
                        _audioNowTimestamp = (long)(((_audioFrame->pts) * ffmpeg.av_q2d(_audioStream->time_base) - (_formatContext->start_time * ffmpeg.av_q2d(s_time_base))) * _audioCodecContext->sample_rate);
                        _audioSeek = false;
                        _audioNowTimestamp -= _audioNowTimestamp % _audioFrame->nb_samples;
                    }
                    else
                    {
                        _audioNowTimestamp = _audioNextTimestamp;
                    }

                    _audioNextTimestamp = _audioNowTimestamp + _audioFrame->nb_samples;
                    ffmpeg.av_packet_unref(_audioPacket);

                    return true;
                }
            }

            ffmpeg.av_packet_unref(_audioPacket);
        }

        if (ffmpeg.avcodec_send_packet(_audioCodecContext, _audioPacket) != 0)
        {
            Debug.WriteLine("avcodec_send_packet failed");
            ffmpeg.av_packet_unref(_audioPacket);
            return false;
        }

        if (ffmpeg.avcodec_receive_frame(_audioCodecContext, _audioFrame) == 0)
        {
            if (_audioSeek)
            {
                _audioNowTimestamp = (long)(((_audioFrame->pts) * ffmpeg.av_q2d(_audioStream->time_base) - (_formatContext->start_time * ffmpeg.av_q2d(s_time_base))) * _audioCodecContext->sample_rate);
                _audioSeek = false;
                _audioNowTimestamp -= _audioNowTimestamp % _audioFrame->nb_samples;
            }
            else
            {
                _audioNowTimestamp = _audioNextTimestamp;
            }

            _audioNextTimestamp = _audioNowTimestamp + _audioFrame->nb_samples;
            ffmpeg.av_packet_unref(_audioPacket);

            return true;
        }

        return false;
    }

    private void SeekAudio(long sample_pos)
    {
        var tb = new AVRational() { num = 1, den = _audioCodecContext->sample_rate };
        long timestamp = sample_pos * 1000000 / _audioCodecContext->sample_rate + _formatContext->start_time;
        ffmpeg.avformat_seek_file(_formatContext, -1, long.MinValue, timestamp, long.MaxValue, ffmpeg.AVSEEK_FLAG_BACKWARD);
        ffmpeg.avcodec_flush_buffers(_audioCodecContext);
        _audioSeek = true;

        while (GrabAudio() && _audioNextTimestamp < sample_pos)
        {
        }
    }

    private bool GrabVideo()
    {
        int ret;
        if (ffmpeg.avcodec_receive_frame(_videoCodecContext, _videoFrame) >= 0)
        {
            _videoNowFrame = GetNowFrame();
            return true;
        }
        ffmpeg.av_packet_unref(_videoPacket);
        while ((ret = ffmpeg.av_read_frame(_formatContext, _videoPacket)) == 0)
        {
            if (_videoPacket->stream_index == _videoStream->index)
            {
                ret = ffmpeg.avcodec_send_packet(_videoCodecContext, _videoPacket);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    continue;
                }
                if (ret < 0)
                {
                    Debug.WriteLine("avcodec_send_packet failed");
                    ffmpeg.av_packet_unref(_videoPacket);
                    return false;
                }
                if (ffmpeg.avcodec_receive_frame(_videoCodecContext, _videoFrame) >= 0)
                {
                    _videoNowFrame = GetNowFrame();
                    return true;
                }
            }
            ffmpeg.av_packet_unref(_videoPacket);
        }
        //もう一度avcodec_send_packetするとフレームが出てくることがある
        ret = ffmpeg.avcodec_send_packet(_videoCodecContext, null);
        if (ret < 0)
        {
            Debug.WriteLine("avcodec_send_packet failed");
            ffmpeg.av_packet_unref(_videoPacket);
            return false;
        }
        if (ffmpeg.avcodec_receive_frame(_videoCodecContext, _videoFrame) >= 0)
        {
            _videoNowFrame = GetNowFrame();
            return true;
        }
        return false;
    }

    private void SeekVideo(int frame)
    {
        void SeekOnly(long frame)
        {
            long time_stamp = (long)Math.Round(frame * 1000000 / _videoAvgFrameRateDouble + _formatContext->start_time, MidpointRounding.AwayFromZero);
            ffmpeg.avformat_seek_file(_formatContext, -1, long.MinValue, time_stamp, long.MaxValue, ffmpeg.AVSEEK_FLAG_BACKWARD);
            ffmpeg.avcodec_flush_buffers(_videoCodecContext);
            GrabVideo();
        }

        SeekOnly(frame);
        // 移動先が目的地より進んでいることがあるためその場合は戻る
        long f = frame - (_videoNowFrame - frame) - 3;
        while (_videoNowFrame > frame)
        {
            if (f < 0) f = 0;
            SeekOnly(f);
            if (f == 0) break;
            f -= 30;
        }
        while (_videoNowFrame < frame && GrabVideo()) { }
    }

    private void GenerateSwsContext()
    {
        _swsContext = ffmpeg.sws_getContext(
            _videoCodecContext->width,
            _videoCodecContext->height,
            _videoCodecContext->pix_fmt,
            _videoCodecContext->width,
            _videoCodecContext->height,
            AVPixelFormat.AV_PIX_FMT_BGRA,
            // scaling_algorithm
            (int)_settings.Scaling,
            null,
            null,
            null);
    }

    private long GetNowFrame()
    {
        double f = (_videoFrame->pts - _videoStream->start_time) * _videoTimeBaseDouble * _videoAvgFrameRateDouble + 0.5;
        return (long)Math.Round(f, MidpointRounding.AwayFromZero);
    }

    private void ConfigureVideoStream()
    {
        if (HasVideo && _videoStream != null)
        {
            _videoCodec = ffmpeg.avcodec_find_decoder(_videoStream->codecpar->codec_id);
            if (_videoCodec == null)
            {
                Debug.WriteLine("No supported decoder ...");
                _hasVideo = false;
                return;
            }

            _videoCodecContext = ffmpeg.avcodec_alloc_context3(_videoCodec);
            if (_videoCodecContext == null)
            {
                Debug.WriteLine("avcodec_alloc_context3 failed");
                _hasVideo = false;
                return;
            }

            if (ffmpeg.avcodec_parameters_to_context(_videoCodecContext, _videoStream->codecpar) < 0)
            {
                Debug.WriteLine("avcodec_parameters_to_context failed");
                _hasVideo = false;
                return;
            }

            if (_settings.ThreadCount != 0)
            {
                _videoCodecContext->thread_count = Math.Min(
                    Environment.ProcessorCount,
                    _settings.ThreadCount > 0 ? _settings.ThreadCount : 16);
            }
            else
            {
                _videoCodecContext->thread_count = 0;
            }

            if (ffmpeg.avcodec_open2(_videoCodecContext, _videoCodec, null) != 0)
            {
                Debug.WriteLine("avcodec_open2 failed");
                _hasVideo = false;
                return;
            }

            GenerateSwsContext();

            if (_swsContext == null)
            {
                Debug.WriteLine("Can not use sws");
                _hasVideo = false;
                return;
            }

            _videoFrame = ffmpeg.av_frame_alloc();
            _videoPacket = ffmpeg.av_packet_alloc();
            _videoTimeBaseDouble = ffmpeg.av_q2d(_videoStream->time_base);
            _videoAvgFrameRateDouble = ffmpeg.av_q2d(_videoStream->avg_frame_rate);
        }
    }

    private void ConfigureAudioStream()
    {
        if (HasAudio && _audioStream != null)
        {
            _audioCodec = ffmpeg.avcodec_find_decoder(_audioStream->codecpar->codec_id);
            if (_audioCodec == null)
            {
                Debug.WriteLine("No supported decoder ...");
                _hasAudio = false;
                return;
            }
            _audioCodecContext = ffmpeg.avcodec_alloc_context3(_audioCodec);
            if (_audioCodecContext == null)
            {
                Debug.WriteLine("avcodec_alloc_context3 failed");
                _hasAudio = false;
                return;
            }
            if (ffmpeg.avcodec_parameters_to_context(_audioCodecContext, _audioStream->codecpar) < 0)
            {
                Debug.WriteLine("avcodec_parameters_to_context failed\n");
                _hasAudio = false;
                return;
            }

            if (_settings.ThreadCount != 0)
            {
                _audioCodecContext->thread_count = Math.Min(
                    Environment.ProcessorCount,
                    _settings.ThreadCount > 0 ? _settings.ThreadCount : 16);
            }
            else
            {
                _audioCodecContext->thread_count = 0;
            }

            if (ffmpeg.avcodec_open2(_audioCodecContext, _audioCodec, null) != 0)
            {
                Debug.WriteLine("avcodec_open2 failed\n");
                _hasAudio = false;
                return;
            }

            _audioFrame = ffmpeg.av_frame_alloc();
            _audioPacket = ffmpeg.av_packet_alloc();
        }
    }
}

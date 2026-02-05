using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;

using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

public sealed class FFmpegReader : MediaReader
{
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
    private MediaDemuxer? _demuxer;
    private MediaStream? _audioStream;
    private MediaStream? _videoStream;
    private bool _hasVideo;
    private bool _hasAudio;
    private MediaDecoder? _videoDecoder;
    private MediaDecoder? _audioDecoder;
    private MediaFrame? _currentVideoFrame;
    private MediaFrame? _currentAudioFrame;
    private MediaPacket? _packet;
    private PixelConverter? _pixelConverter;
    private SampleConverter? _sampleConverter;
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
            // MediaDemuxerでファイルを開く
            _demuxer = MediaDemuxer.Open(file);

            bool loadAudio = options.StreamsToLoad.HasFlag(MediaMode.Audio);
            bool loadVideo = options.StreamsToLoad.HasFlag(MediaMode.Video);

            // ストリーム検索
            for (int i = 0; i < _demuxer.Count; i++)
            {
                var stream = _demuxer[i];
                var codecType = stream.CodecparRef.codec_type;
                if (loadVideo && !_hasVideo && codecType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    _videoStream = stream;
                    _hasVideo = true;
                }
                if (loadAudio && !_hasAudio && codecType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    _audioStream = stream;
                    _hasAudio = true;
                }
            }

            if (_videoStream == null && _audioStream == null)
            {
                return;
            }

            ConfigureVideoStream();
            ConfigureAudioStream();

            // パケット初期化
            _packet = new MediaPacket();

            if (!HasVideo && !HasAudio)
            {
                return;
            }

            if (HasVideo)
            {
                GrabVideo();

                var codec = _videoDecoder!.GetCodec();
                _videoInfo = new VideoStreamInfo(
                    codec?.LongName ?? "Unknown",
                    _videoStream!.NbFrames,
                    new PixelSize(_videoDecoder.Width, _videoDecoder.Height),
                    new Rational(_videoStream.AvgFrameRate.num, _videoStream.AvgFrameRate.den))
                {
                    Duration = new Rational(_demuxer.Duration, ffmpeg.AV_TIME_BASE)
                };
            }

            if (HasAudio)
            {
                var codec = _audioDecoder!.GetCodec();
                _audioInfo = new AudioStreamInfo(
                    codec?.Name ?? "Unknown",
                    new Rational(_demuxer!.Duration, ffmpeg.AV_TIME_BASE),
                    _audioDecoder.SampleRate,
                    _audioDecoder.ChLayout.nb_channels);
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

    public override unsafe bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? result)
    {
        // Decoder側でResampleされるかのチェックのため
        if (length == 0)
        {
            result = new Pcm<Stereo32BitFloat>(AudioInfo.SampleRate, 0);
            return true;
        }

        if (_audioDecoder == null || _currentAudioFrame == null)
        {
            result = null;
            return false;
        }

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
        bool needGrab = false;

        // SampleConverterを遅延初期化
        _sampleConverter ??= new SampleConverter();

        fixed (Stereo32BitFloat* buf = sound.DataSpan)
        {
            while ((!needGrab || GrabAudio()) && _currentAudioFrame.NbSamples > 0)
            {
                // SampleConverterの設定
                _sampleConverter.SetOpts(
                    AV_CHANNEL_LAYOUT_STEREO,
                    AudioInfo.SampleRate,
                    AVSampleFormat.AV_SAMPLE_FMT_FLT,
                    _currentAudioFrame.NbSamples);

                // 変換
                using var convertedFrame = _sampleConverter.ConvertFrame(_currentAudioFrame, out _, out _);
                _samplesReturn = convertedFrame.NbSamples;

                if (_samplesReturn < 0)
                {
                    Debug.WriteLine("swr_convert error.");
                    sound.Dispose();
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
                    Buffer.MemoryCopy(
                        convertedFrame.Data[0] + (skip * size),
                        ((byte*)buf) + (decoded * size),
                        len * size,
                        len * size);
                    decoded += len;
                }

                if (decoded >= length || len <= 0)
                {
                    result = sound;
                    return true;
                }

                needGrab = skip + len >= _currentAudioFrame.NbSamples;
            }

            result = sound;
            return true;
        }
    }

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        if (_videoStream == null || _videoDecoder == null || _currentVideoFrame == null)
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

        int width = _currentVideoFrame.Width;
        int height = _currentVideoFrame.Height;

        // PixelConverterを初期化（遅延初期化）
        _pixelConverter ??= new PixelConverter();
        _pixelConverter.SetOpts(width, height, AVPixelFormat.AV_PIX_FMT_BGRA);

        // 変換
        using var dstFrame = _pixelConverter.ConvertFrame(_currentVideoFrame);

        // ビットマップにコピー
        var bmp = new Bitmap<Bgra8888>(width, height);
        int byteCount = width * height * 4;
        Buffer.MemoryCopy(dstFrame.Data[0], (void*)bmp.Data, byteCount, byteCount);

        image = bmp;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pixelConverter?.Dispose();
            _pixelConverter = null;

            _sampleConverter?.Dispose();
            _sampleConverter = null;

            _videoDecoder?.Dispose();
            _videoDecoder = null;

            _audioDecoder?.Dispose();
            _audioDecoder = null;

            _currentVideoFrame?.Dispose();
            _currentVideoFrame = null;

            _currentAudioFrame?.Dispose();
            _currentAudioFrame = null;

            _packet?.Dispose();
            _packet = null;

            _demuxer?.Dispose();
            _demuxer = null;
        }

        base.Dispose(disposing);
    }

    private bool GrabAudio()
    {
        if (_demuxer == null || _audioDecoder == null || _packet == null || _audioStream == null || _currentAudioFrame == null)
            return false;

        _currentAudioFrame.Unref();

        foreach (var packet in _demuxer.ReadPackets(_packet))
        {
            if (packet.StreamIndex == _audioStream.Index)
            {
                foreach (var frame in _audioDecoder.DecodePacket(packet, _currentAudioFrame))
                {
                    UpdateAudioTimestamp();
                    return true;
                }
            }
        }

        // フラッシュ：残りのフレームを取り出す
        foreach (var frame in _audioDecoder.DecodePacket(null, _currentAudioFrame))
        {
            UpdateAudioTimestamp();
            return true;
        }

        return false;
    }

    private void UpdateAudioTimestamp()
    {
        if (_currentAudioFrame == null || _audioStream == null || _audioDecoder == null || _demuxer == null)
            return;

        if (_audioSeek)
        {
            _audioNowTimestamp = (long)((_currentAudioFrame.Pts * ffmpeg.av_q2d(_audioStream.TimeBase)
                                         - (_demuxer.StartTime * ffmpeg.av_q2d(s_time_base))) * _audioDecoder.SampleRate);
            _audioSeek = false;
            _audioNowTimestamp -= _audioNowTimestamp % _currentAudioFrame.NbSamples;
        }
        else
        {
            _audioNowTimestamp = _audioNextTimestamp;
        }

        _audioNextTimestamp = _audioNowTimestamp + _currentAudioFrame.NbSamples;
    }

    private unsafe void SeekAudio(long sample_pos)
    {
        if (_demuxer == null || _audioDecoder == null) return;

        long timestamp = sample_pos * 1000000 / _audioDecoder.SampleRate + _demuxer.StartTime;
        _demuxer.Seek(timestamp, -1);
        ffmpeg.avcodec_flush_buffers(_audioDecoder);
        _audioSeek = true;

        while (GrabAudio() && _audioNextTimestamp < sample_pos)
        {
        }
    }

    private bool GrabVideo()
    {
        if (_demuxer == null || _videoDecoder == null || _packet == null || _videoStream == null)
            return false;

        foreach (var packet in _demuxer.ReadPackets(_packet))
        {
            if (packet.StreamIndex == _videoStream.Index)
            {
                foreach (var frame in _videoDecoder.DecodePacket(packet, _currentVideoFrame))
                {
                    _videoNowFrame = GetNowFrame();
                    return true;
                }
            }
        }

        // フラッシュ：残りのフレームを取り出す
        foreach (var frame in _videoDecoder.DecodePacket(null, _currentVideoFrame))
        {
            _videoNowFrame = GetNowFrame();
            return true;
        }

        return false;
    }

    private unsafe void SeekVideo(int frame)
    {
        if (_demuxer == null || _videoDecoder == null) return;

        void SeekOnly(long targetFrame)
        {
            long timestamp = (long)Math.Round(targetFrame * 1000000 / _videoAvgFrameRateDouble + _demuxer.StartTime, MidpointRounding.AwayFromZero);
            _demuxer.Seek(timestamp, -1);
            ffmpeg.avcodec_flush_buffers(_videoDecoder);
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

    private long GetNowFrame()
    {
        if (_currentVideoFrame == null || _videoStream == null) return 0;
        double f = (_currentVideoFrame.Pts - _videoStream.StartTime) * _videoTimeBaseDouble * _videoAvgFrameRateDouble + 0.5;
        return (long)f;
    }

    private void ConfigureVideoStream()
    {
        if (!_hasVideo || _videoStream == null) return;

        try
        {
            // デコーダー作成
            _videoDecoder = MediaDecoder.CreateDecoder(
                _videoStream.CodecparRef,
                ctx =>
                {
                    if (_settings.ThreadCount != 0)
                    {
                        ctx.ThreadCount = Math.Min(
                            Environment.ProcessorCount,
                            _settings.ThreadCount > 0 ? _settings.ThreadCount : 16);
                    }
                    else
                    {
                        ctx.ThreadCount = 0;
                    }
                });
        }
        catch
        {
            Debug.WriteLine("Failed to create video decoder");
            _hasVideo = false;
            return;
        }

        if (_videoDecoder == null)
        {
            _hasVideo = false;
            return;
        }

        // フレーム初期化
        _currentVideoFrame = new MediaFrame();

        // TimeBase計算
        _videoTimeBaseDouble = ffmpeg.av_q2d(_videoStream.TimeBase);
        _videoAvgFrameRateDouble = ffmpeg.av_q2d(_videoStream.AvgFrameRate);
    }

    private void ConfigureAudioStream()
    {
        if (!_hasAudio || _audioStream == null) return;

        try
        {
            // デコーダー作成
            _audioDecoder = MediaDecoder.CreateDecoder(
                _audioStream.CodecparRef,
                ctx =>
                {
                    if (_settings.ThreadCount != 0)
                    {
                        ctx.ThreadCount = Math.Min(
                            Environment.ProcessorCount,
                            _settings.ThreadCount > 0 ? _settings.ThreadCount : 16);
                    }
                    else
                    {
                        ctx.ThreadCount = 0;
                    }
                });
        }
        catch
        {
            Debug.WriteLine("Failed to create audio decoder");
            _hasAudio = false;
            return;
        }

        if (_audioDecoder == null)
        {
            _hasAudio = false;
            return;
        }

        // フレーム初期化
        _currentAudioFrame = new MediaFrame();
    }
}

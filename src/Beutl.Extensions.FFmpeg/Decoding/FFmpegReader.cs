using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;
using Microsoft.Extensions.Logging;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

public sealed class FFmpegReader : MediaReader
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegReader>();
    private static readonly AVRational s_time_base = new() { num = 1, den = ffmpeg.AV_TIME_BASE };

#pragma warning disable IDE1006 // 命名スタイル
    private static readonly AVChannelLayout AV_CHANNEL_LAYOUT_STEREO = new()
#pragma warning restore IDE1006 // 命名スタイル
    {
        order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
        nb_channels = 2,
        u = new AVChannelLayout_u { mask = ffmpeg.AV_CH_LAYOUT_STEREO }
    };

    private readonly FFmpegDecodingSettings _settings;
    private readonly MediaStream? _audioStream;
    private readonly MediaStream? _videoStream;
    private MediaDemuxer? _demuxer;
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
    private MediaFrame? _swVideoFrame;
    private bool _isHWDecoding;
    private bool _isHdr;
    private BitmapColorSpace? _colorspace;

    public FFmpegReader(string file, MediaOptions options, FFmpegDecodingSettings settings)
    {
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
                GetPacketColorSpace();

                var codec = _videoDecoder!.GetCodec();
                VideoInfo = new VideoStreamInfo(
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
                AudioInfo = new AudioStreamInfo(
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

    private MediaFrame? ActiveVideoFrame => _isHWDecoding ? _swVideoFrame : _currentVideoFrame;

    public override VideoStreamInfo VideoInfo => field ?? throw new Exception("The stream does not exist.");

    public override AudioStreamInfo AudioInfo => field ?? throw new Exception("The stream does not exist.");

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

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out Bitmap? image)
    {
        var videoFrame = ActiveVideoFrame;
        if (_videoStream == null || _videoDecoder == null || videoFrame == null)
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

        // GrabVideo後にActiveVideoFrameを再取得
        videoFrame = ActiveVideoFrame;
        if (videoFrame == null)
        {
            image = null;
            return false;
        }

        int width = videoFrame.Width;
        int height = videoFrame.Height;

        // PixelConverterを初期化（遅延初期化）
        _pixelConverter ??= new PixelConverter();

        // HDR時はRGBA64LE (16bit/ch)、SDR時はBGRA (8bit/ch)
        var dstPixFmt = _isHdr ? AVPixelFormat.AV_PIX_FMT_RGBA64LE : AVPixelFormat.AV_PIX_FMT_BGRA;
        _pixelConverter.SetOpts(width, height, dstPixFmt);

        // 変換
        using var dstFrame = _pixelConverter.ConvertFrame(videoFrame, (int)_settings.Scaling);

        // フレームの色空間を取得
        var colorSpace = !_settings.ForceSrgbGamma ? GetFrameColorSpace(dstFrame) : BitmapColorSpace.Srgb;

        // ビットマップにコピー
        var colorType = _isHdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888;
        int bytesPerPixel = _isHdr ? 8 : 4;
        var bmp = new Bitmap(width, height, colorType, BitmapAlphaType.Unpremul, colorSpace);
        int byteCount = width * height * bytesPerPixel;
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

            _swVideoFrame?.Dispose();
            _swVideoFrame = null;

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
        if (_demuxer == null || _audioDecoder == null || _packet == null || _audioStream == null ||
            _currentAudioFrame == null)
            return false;

        _currentAudioFrame.Unref();

        foreach (var packet in _demuxer.ReadPackets(_packet))
        {
            if (packet.StreamIndex == _audioStream.Index)
            {
                foreach (var _ in _audioDecoder.DecodePacket(packet, _currentAudioFrame))
                {
                    UpdateAudioTimestamp();
                    return true;
                }
            }
        }

        // フラッシュ：残りのフレームを取り出す
        foreach (var _ in _audioDecoder.DecodePacket(null, _currentAudioFrame))
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
                                         - (_demuxer.StartTime * ffmpeg.av_q2d(s_time_base))) *
                                        _audioDecoder.SampleRate);
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
                foreach (var _ in _videoDecoder.DecodePacket(packet, _currentVideoFrame, _swVideoFrame))
                {
                    _videoNowFrame = GetNowFrame();
                    return true;
                }
            }
        }

        // フラッシュ：残りのフレームを取り出す
        foreach (var _ in _videoDecoder.DecodePacket(null, _currentVideoFrame, _swVideoFrame))
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
            long timestamp = (long)Math.Round(targetFrame * 1000000.0 / _videoAvgFrameRateDouble + _demuxer.StartTime,
                MidpointRounding.AwayFromZero);
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

        while (_videoNowFrame < frame && GrabVideo())
        {
        }
    }

    private long GetNowFrame()
    {
        var frame = ActiveVideoFrame;
        if (frame == null || _videoStream == null) return 0;
        double f = (frame.Pts - _videoStream.StartTime) * _videoTimeBaseDouble * _videoAvgFrameRateDouble + 0.5;
        return (long)f;
    }

    private void ConfigureVideoStream()
    {
        if (!_hasVideo || _videoStream == null) return;

        AVHWDeviceType? hwDeviceType = GetAVHWDeviceType();

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

                    if (_settings.Acceleration != FFmpegDecodingSettings.AccelerationOptions.Software)
                    {
                        try
                        {
                            // 1のとき成功
                            int result = ctx.InitHWDeviceContext(hwDeviceType);
                            _isHWDecoding = result != 0;
                        }
                        catch
                        {
                            _logger.LogWarning(
                                "Failed to initialize HW device context, falling back to software decoding");
                            _isHWDecoding = false;
                        }
                    }
                });
        }
        catch
        {
            _logger.LogError("Failed to create video decoder");
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
        if (_isHWDecoding)
        {
            _swVideoFrame = new MediaFrame();
        }

        // TimeBase計算
        _videoTimeBaseDouble = ffmpeg.av_q2d(_videoStream.TimeBase);
        _videoAvgFrameRateDouble = ffmpeg.av_q2d(_videoStream.AvgFrameRate);
    }

    private AVHWDeviceType? GetAVHWDeviceType()
    {
        return _settings.Acceleration switch
        {
            FFmpegDecodingSettings.AccelerationOptions.Software => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE,
            FFmpegDecodingSettings.AccelerationOptions.VDPAU => AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU,
            FFmpegDecodingSettings.AccelerationOptions.CUDA => AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            FFmpegDecodingSettings.AccelerationOptions.VAAPI => AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
            FFmpegDecodingSettings.AccelerationOptions.DXVA2 => AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
            FFmpegDecodingSettings.AccelerationOptions.QSV => AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
            FFmpegDecodingSettings.AccelerationOptions.VideoToolbox => AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
            FFmpegDecodingSettings.AccelerationOptions.D3D11VA => AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            FFmpegDecodingSettings.AccelerationOptions.DRM => AVHWDeviceType.AV_HWDEVICE_TYPE_DRM,
            FFmpegDecodingSettings.AccelerationOptions.OpenCL => AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL,
            FFmpegDecodingSettings.AccelerationOptions.MediaCodec => AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC,
            FFmpegDecodingSettings.AccelerationOptions.Vulkan => AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
            _ => null
        };
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
            _logger.LogError("Failed to create audio decoder");
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

    private unsafe void GetPacketColorSpace()
    {
        if (_videoStream == null) return;

        // HDR判定: PQ (HDR10) または HLG の場合はHDR
        var trc = _videoStream.Codecpar->color_trc;
        _isHdr = trc is AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084
            or AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67;

        AVPacketSideData* psd = ffmpeg.av_packet_side_data_get(
            _videoStream.Codecpar->coded_side_data,
            _videoStream.Codecpar->nb_coded_side_data,
            AVPacketSideDataType.AV_PKT_DATA_ICC_PROFILE);
        if (psd != null)
        {
            _colorspace = BitmapColorSpace.CreateIcc(new ReadOnlySpan<byte>(psd->data, (int)psd->size));
        }

        if (_colorspace == null)
        {
            var transferFn = GetTransferFunction(_videoStream.Codecpar->color_trc);
            var gamut = GetBitmapColorSpaceXyz(_videoStream.Codecpar->color_primaries);

            if (transferFn == BitmapColorSpaceTransferFn.Srgb && gamut == BitmapColorSpaceXyz.Srgb)
            {
                _colorspace = BitmapColorSpace.Srgb;
            }
            else if (transferFn == BitmapColorSpaceTransferFn.Linear && gamut == BitmapColorSpaceXyz.Srgb)
            {
                _colorspace = BitmapColorSpace.LinearSrgb;
            }
            else
            {
                _colorspace = BitmapColorSpace.CreateRgb(transferFn, gamut);
            }
        }

        if (_colorspace == null)
        {
            var videoFrame = ActiveVideoFrame;
            if (videoFrame != null)
            {
                _colorspace = GetFrameColorSpace(videoFrame);
            }
        }

        if (_colorspace != null)
        {
            _logger.LogInformation("Video color space: {ColorSpace} ({Hdr})", _colorspace, _isHdr ? "HDR" : "SDR");
        }
        else
        {
            _logger.LogWarning("Failed to determine video color space.");
        }

        if (_settings.ForceSrgbGamma && _colorspace != BitmapColorSpace.Srgb)
        {
            _logger.LogWarning("ForceSrgbGamma is enabled, but the detected color space is not sRGB. Forcing sRGB gamma may lead to incorrect colors.");
        }
    }

    private static BitmapColorSpaceTransferFn GetTransferFunction(AVColorTransferCharacteristic trc)
    {
        return trc switch
        {
            AVColorTransferCharacteristic.AVCOL_TRC_LINEAR => BitmapColorSpaceTransferFn.Linear,
            AVColorTransferCharacteristic.AVCOL_TRC_GAMMA22 => BitmapColorSpaceTransferFn.TwoDotTwo,
            AVColorTransferCharacteristic.AVCOL_TRC_BT2020_10 or
                AVColorTransferCharacteristic.AVCOL_TRC_BT2020_12 => BitmapColorSpaceTransferFn.Rec2020,
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084 => BitmapColorSpaceTransferFn.Pq,
            AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67 => BitmapColorSpaceTransferFn.Hlg,
            AVColorTransferCharacteristic.AVCOL_TRC_BT709 or
                AVColorTransferCharacteristic.AVCOL_TRC_SMPTE170M or
                AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_4 or
                AVColorTransferCharacteristic.AVCOL_TRC_BT1361_ECG => BitmapColorSpaceTransferFn.Bt709,
            AVColorTransferCharacteristic.AVCOL_TRC_GAMMA28 => BitmapColorSpaceTransferFn.Gamma28,
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE240M => BitmapColorSpaceTransferFn.Smpte240M,
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE428 => BitmapColorSpaceTransferFn.Smpte428,
            _ => BitmapColorSpaceTransferFn.Srgb
        };
    }

    private static BitmapColorSpaceXyz GetBitmapColorSpaceXyz(AVColorPrimaries primaries)
    {
        return primaries switch
        {
            AVColorPrimaries.AVCOL_PRI_BT709 => BitmapColorSpaceXyz.Bt709,
            AVColorPrimaries.AVCOL_PRI_BT470M => BitmapColorSpaceXyz.Bt470M,
            AVColorPrimaries.AVCOL_PRI_BT470BG => BitmapColorSpaceXyz.Bt470BG,
            AVColorPrimaries.AVCOL_PRI_SMPTE170M => BitmapColorSpaceXyz.Smpte170M,
            AVColorPrimaries.AVCOL_PRI_SMPTE240M => BitmapColorSpaceXyz.Smpte240M,
            AVColorPrimaries.AVCOL_PRI_FILM => BitmapColorSpaceXyz.Film,
            AVColorPrimaries.AVCOL_PRI_BT2020 => BitmapColorSpaceXyz.Rec2020,
            AVColorPrimaries.AVCOL_PRI_SMPTE428 => BitmapColorSpaceXyz.Xyz,
            AVColorPrimaries.AVCOL_PRI_SMPTE431 => BitmapColorSpaceXyz.Smpte431,
            AVColorPrimaries.AVCOL_PRI_SMPTE432 => BitmapColorSpaceXyz.Dcip3,
            AVColorPrimaries.AVCOL_PRI_EBU3213 => BitmapColorSpaceXyz.Ebu3213,
            _ => BitmapColorSpaceXyz.Srgb
        };
    }

    private BitmapColorSpace GetFrameColorSpace(MediaFrame frame)
    {
        if (_colorspace != null) return _colorspace;

        var transferFn = GetTransferFunction(frame.ColorTrc);
        var gamut = GetBitmapColorSpaceXyz(frame.ColorPrimaries);

        if (transferFn == BitmapColorSpaceTransferFn.Srgb && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.Srgb;

        if (transferFn == BitmapColorSpaceTransferFn.Linear && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.LinearSrgb;

        return BitmapColorSpace.CreateRgb(transferFn, gamut);
    }
}

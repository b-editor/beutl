using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;
using Microsoft.Extensions.Logging;

namespace Beutl.FFmpegWorker.Decoding;

public sealed class FFmpegReader : MediaReader
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegReader>();
    private static readonly AVRational s_time_base = new() { num = 1, den = ffmpeg.AV_TIME_BASE };

    // Every reader lives in the same worker process, and frame threading keeps a decoded frame and
    // decoder state alive per thread, so the automatic count stays below FFmpeg's own limit of 16.
    private const int AutoThreadCountLimit = 8;

#pragma warning disable IDE1006 // 命名スタイル
    private static readonly AVChannelLayout AV_CHANNEL_LAYOUT_STEREO = new()
#pragma warning restore IDE1006 // 命名スタイル
    {
        order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
        nb_channels = 2,
        u = new AVChannelLayout_u { mask = ffmpeg.AV_CH_LAYOUT_STEREO }
    };

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
    private MediaFilterGraph? _filterGraph;
    private MediaFilterContext? _bufferSrcCtx;
    private MediaFilterContext? _bufferSinkCtx;
    private MediaFrame? _filterFrame;
    private int _filterWidth;
    private int _filterHeight;
    private AVPixelFormat _filterSrcPixFmt = AVPixelFormat.AV_PIX_FMT_NONE;

    public FFmpegReader(string file, MediaOptions options, FFmpegDecodingSettings settings)
    {
        Settings = settings;
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

    public FFmpegDecodingSettings Settings { get; }

    private MediaFrame? ActiveVideoFrame => _isHWDecoding ? _swVideoFrame : _currentVideoFrame;

    public override VideoStreamInfo VideoInfo => field ?? throw new Exception("The stream does not exist.");

    public override AudioStreamInfo AudioInfo => field ?? throw new Exception("The stream does not exist.");

    public override bool HasVideo => _hasVideo;

    public override bool HasAudio => _hasAudio;

    public bool IsHdr => _isHdr;

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? result)
    {
        // Decoder側でResampleされるかのチェックのため
        if (length == 0)
        {
            result = Ref<IPcm>.Create(new Pcm<Stereo32BitFloat>(AudioInfo.SampleRate, 0));
            return true;
        }

        result = null;
        if (_audioDecoder == null || _currentAudioFrame == null) return false;

        var sound = new Pcm<Stereo32BitFloat>(AudioInfo.SampleRate, length);
        if (ReadAudio(start, length, MemoryMarshal.AsBytes(sound.DataSpan), out _))
        {
            result = Ref<IPcm>.Create(sound);
            return true;
        }
        else
        {
            sound.Dispose();
            return false;
        }
    }

    public unsafe bool ReadAudio(int start, int length, Span<byte> destination, out AudioFrameInfo info)
    {
        info = default;

        if (length == 0)
        {
            info = CreateAudioFrameInfo(0);
            return true;
        }

        if (_audioDecoder == null || _currentAudioFrame == null)
            return false;

        if (!IsInCurrentAudioFrame(start))
        {
            GrabAudio();

            if (!IsInCurrentAudioFrame(start))
            {
                SeekAudio(start);
            }
        }

        int decoded = 0;
        bool needGrab = false;
        int sampleSize = sizeof(Stereo32BitFloat);

        // SampleConverterを遅延初期化
        _sampleConverter ??= new SampleConverter();

        fixed (byte* buf = destination)
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
                    Buffer.MemoryCopy(
                        convertedFrame.Data[0] + (skip * sampleSize),
                        buf + (decoded * sampleSize),
                        len * sampleSize,
                        len * sampleSize);
                    decoded += len;
                }

                if (decoded >= length || len <= 0)
                {
                    info = CreateAudioFrameInfo(decoded);
                    return true;
                }

                needGrab = skip + len >= _currentAudioFrame.NbSamples;
            }

            info = CreateAudioFrameInfo(decoded);
            return true;
        }
    }

    private unsafe AudioFrameInfo CreateAudioFrameInfo(int samples)
    {
        return new AudioFrameInfo
        {
            SampleRate = AudioInfo.SampleRate,
            NumSamples = samples,
            DataLength = samples * sizeof(Stereo32BitFloat),
        };
    }

    private bool IsInCurrentAudioFrame(int start)
        => start >= _audioNowTimestamp && start < _audioNextTimestamp;

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;

        MediaFrame? filterFrame = ReadVideoCore(frame);
        if (filterFrame == null) return false;

        // フレームの色空間を取得
        var colorSpace = ResolveFrameColorSpace(filterFrame);
        int width = filterFrame.Width;
        int height = filterFrame.Height;
        var colorType = _isHdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888;
        int bytesPerPixel = BytesPerPixel;
        var bmp = new Bitmap(width, height, colorType, BitmapAlphaType.Unpremul, colorSpace);

        try
        {
            int srcRowBytes = filterFrame.Linesize[0];
            int dstRowBytes = width * bytesPerPixel;
            for (int y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(
                    (void*)(filterFrame.Data[0] + (long)y * srcRowBytes),
                    (void*)((byte*)bmp.Data + (long)y * dstRowBytes),
                    dstRowBytes,
                    dstRowBytes);
            }
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
        finally
        {
            filterFrame.Unref();
        }

        image = Ref<Bitmap>.Create(bmp);
        return true;
    }

    public unsafe bool ReadVideo(int frame, Span<byte> destination, out VideoFrameInfo info)
    {
        info = default;

        MediaFrame? filterFrame = ReadVideoCore(frame);
        if (filterFrame == null) return false;

        try
        {
            // フレームの色空間を取得
            var colorSpace = ResolveFrameColorSpace(filterFrame);
            int width = filterFrame.Width;
            int height = filterFrame.Height;
            int bytesPerPixel = BytesPerPixel;
            int byteCount = width * height * bytesPerPixel;

            info = new VideoFrameInfo
            {
                Width = width,
                Height = height,
                BytesPerPixel = bytesPerPixel,
                IsHdr = _isHdr,
                ColorSpace = colorSpace,
                DataLength = byteCount,
            };

            if (byteCount > destination.Length)
                return false;

            int srcRowBytes = filterFrame.Linesize[0];
            int dstRowBytes = width * bytesPerPixel;
            for (int y = 0; y < height; y++)
            {
                new ReadOnlySpan<byte>((void*)(filterFrame.Data[0] + (long)y * srcRowBytes), dstRowBytes)
                    .CopyTo(destination.Slice(y * dstRowBytes));
            }
            return true;
        }
        finally
        {
            filterFrame.Unref();
        }
    }

    // RGBA64LE (HDR) or BGRA (SDR), matching the format filter InitFilterGraph builds.
    private int BytesPerPixel => FFmpegVideoSlotSizing.GetBytesPerPixel(_isHdr);

    private BitmapColorSpace ResolveFrameColorSpace(MediaFrame frame)
        => (!Settings.ForceSrgbGamma || _isHdr) ? GetFrameColorSpace(frame) : BitmapColorSpace.Srgb;

    private MediaFrame? ReadVideoCore(int frame)
    {
        var videoFrame = ActiveVideoFrame;
        if (_videoStream == null || _videoDecoder == null || videoFrame == null)
            return null;

        long skip = frame - _videoNowFrame;

        // If the previous grab left the active frame unreferenced (a seek/grab that could not
        // reach a decodable frame), _videoNowFrame no longer describes a usable frame, so `skip`
        // (which can be 0) must not be trusted — force a fresh seek so a later in-range request
        // recovers the frame instead of repeatedly returning the empty one.
        bool currentUsable =
            FFmpegFrameValidation.IsUsableVideoFrame(videoFrame.Format, videoFrame.Width, videoFrame.Height);
        if (FFmpegSeekDecision.ShouldReseek(currentUsable, skip))
        {
            SeekVideo(frame);
            skip = 0;
        }

        for (int i = 0; i < skip; i++)
        {
            if (!GrabVideo())
                return null;
        }

        // GrabVideo後にActiveVideoFrameを再取得
        videoFrame = ActiveVideoFrame;
        if (videoFrame == null)
            return null;

        // A seek/grab that could not reach a decodable frame (e.g. the target is at or past
        // EOF) leaves the frame unreferenced by avcodec_receive_frame. Drop it instead of
        // feeding AV_PIX_FMT_NONE to the buffer source, which would throw AVERROR(EINVAL).
        if (!FFmpegFrameValidation.IsUsableVideoFrame(videoFrame.Format, videoFrame.Width, videoFrame.Height))
            return null;

        // AVFilterグラフを初期化（入力パラメータ変更時のみ再構築）
        InitFilterGraph(videoFrame);

        // フレームをフィルタグラフに送信
        const int AV_BUFFERSRC_FLAG_KEEP_REF = 8;
        _bufferSrcCtx!.WriteFrame(videoFrame, AV_BUFFERSRC_FLAG_KEEP_REF);

        // フィルタグラフから変換済みフレームを取得
        _filterFrame ??= new MediaFrame();
        int ret = _bufferSinkCtx!.GetFrame(_filterFrame);
        if (ret < 0)
        {
            _filterFrame.Unref();
            return null;
        }

        return _filterFrame;
    }

    private void InitFilterGraph(MediaFrame videoFrame)
    {
        int width = videoFrame.Width;
        int height = videoFrame.Height;
        var srcPixFmt = (AVPixelFormat)videoFrame.Format;

        // 入力パラメータが変わっていなければ再構築不要
        if (_filterGraph != null && _filterWidth == width && _filterHeight == height && _filterSrcPixFmt == srcPixFmt)
            return;

        // Invalidate the cache before rebuilding so that if the build below throws, the next
        // call rebuilds from scratch instead of matching a stale cache and reusing a
        // half-initialised graph.
        _filterWidth = 0;
        _filterHeight = 0;
        _filterSrcPixFmt = AVPixelFormat.AV_PIX_FMT_NONE;

        _filterGraph?.Dispose();

        _filterGraph = new MediaFilterGraph();

        var bufferSrc = new MediaFilter(MediaFilter.VideoSources.Buffer);
        var bufferSink = new MediaFilter(MediaFilter.VideoSinks.Buffersink);

        var timeBase = _videoStream!.TimeBase;
        var aspect = videoFrame.SampleAspectRatio;
        var frameRate = _videoStream.AvgFrameRate;

        _bufferSrcCtx =
            _filterGraph.AddVideoSrcFilter(bufferSrc, width, height, srcPixFmt, timeBase, aspect, frameRate);

        var dstPixFmt = _isHdr ? AVPixelFormat.AV_PIX_FMT_RGBA64LE : AVPixelFormat.AV_PIX_FMT_BGRA;
        _bufferSinkCtx = _filterGraph.AddVideoSinkFilter(bufferSink, [dstPixFmt]);

        // HDR (PQ/HLG): RGBA64LEに変換のみ。
        // 輝度マッピングはSkiaの色空間変換で行う（BuildHdrColorSpaceでガマット行列にスケーリングを組み込み済み）
        // SDR: BGRAに変換
        string pixelFormats = _isHdr ? "pix_fmts=rgba64le" : "pix_fmts=bgra";
        var formatFilter = new MediaFilter("format");
        var formatCtx = _filterGraph.AddFilter(formatFilter, pixelFormats);

        _bufferSrcCtx.LinkTo(0, formatCtx);
        formatCtx.LinkTo(0, _bufferSinkCtx);

        _filterGraph.Initialize();

        _filterWidth = width;
        _filterHeight = height;
        _filterSrcPixFmt = srcPixFmt;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _filterFrame?.Dispose();
            _filterFrame = null;

            _filterGraph?.Dispose();
            _filterGraph = null;
            _bufferSrcCtx = null;
            _bufferSinkCtx = null;

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
            if (packet.StreamIndex == _videoStream.Index && DecodeVideoPacket(packet))
            {
                _videoNowFrame = GetNowFrame();
                return true;
            }
        }

        // フラッシュ：残りのフレームを取り出す
        if (DecodeVideoPacket(null))
        {
            _videoNowFrame = GetNowFrame();
            return true;
        }

        return false;
    }

    // Same contract as the first iteration of MediaDecoder.DecodePacket, except for hardware frames:
    // DecodePacket copies props before the transfer, so the reused _swVideoFrame accumulates side data on
    // every frame (av_frame_copy_props appends) and the first transfer, which allocates, drops the props.
    private unsafe bool DecodeVideoPacket(MediaPacket? packet)
    {
        int ret = _videoDecoder!.SendPacket(packet);
        if (ret < 0 && ret != ffmpeg.AVERROR(ffmpeg.EAGAIN) && ret != ffmpeg.AVERROR_EOF)
            ret.ThrowIfError();

        // Like DecodePacket, a receive error ends this packet without throwing.
        if (_videoDecoder.ReceiveFrame(_currentVideoFrame) < 0)
            return false;

        if (_isHWDecoding)
            TransferHardwareFrame(_currentVideoFrame!, _swVideoFrame!);

        return true;
    }

    private static unsafe void TransferHardwareFrame(AVFrame* hwFrame, AVFrame* swFrame)
    {
        if (hwFrame->hw_frames_ctx == null)
        {
            // The decoder produced a software frame despite the device context.
            ffmpeg.av_frame_unref(swFrame);
            ffmpeg.av_frame_ref(swFrame, hwFrame).ThrowIfError();
            return;
        }

        // Downloading into the previous frame's buffers avoids a full-frame allocation per decode, which is
        // only valid while nothing else references them and the downloaded geometry is unchanged.
        var framesContext = (AVHWFramesContext*)hwFrame->hw_frames_ctx->data;
        if (swFrame->buf[0] != null
            && (ffmpeg.av_frame_is_writable(swFrame) == 0
                || swFrame->width != hwFrame->width
                || swFrame->height != hwFrame->height
                || (AVPixelFormat)swFrame->format != framesContext->sw_format))
        {
            ffmpeg.av_frame_unref(swFrame);
        }

        ffmpeg.av_frame_side_data_free(&swFrame->side_data, &swFrame->nb_side_data);
        ffmpeg.av_dict_free(&swFrame->metadata);

        ffmpeg.av_hwframe_transfer_data(swFrame, hwFrame, 0).ThrowIfError();
        ffmpeg.av_frame_copy_props(swFrame, hwFrame).ThrowIfError();
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
                    ApplyThreadCount(ctx);

                    if (Settings.Acceleration != FFmpegDecodingSettings.AccelerationOptions.Software)
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

    private void ApplyThreadCount(MediaCodecContext ctx)
    {
        if (Settings.ThreadCount != 0)
        {
            ctx.ThreadCount = Math.Min(
                Environment.ProcessorCount,
                Settings.ThreadCount > 0 ? Settings.ThreadCount : AutoThreadCountLimit);
        }
        else
        {
            ctx.ThreadCount = 0;
        }
    }

    private AVHWDeviceType? GetAVHWDeviceType()
    {
        return Settings.Acceleration switch
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
                ctx => ApplyThreadCount(ctx));
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
        _isHdr = ColorSpaceHelper.IsHdrTransfer(trc);

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
            if (_isHdr)
            {
                // HDR: 輝度スケーリング付き色空間（エンコードと対称）
                _colorspace = ColorSpaceHelper.BuildHdrColorSpace(
                    _videoStream.Codecpar->color_trc, _videoStream.Codecpar->color_primaries);
            }
            else
            {
                _colorspace = ColorSpaceHelper.BuildTargetColorSpace(
                    _videoStream.Codecpar->color_trc, _videoStream.Codecpar->color_primaries);
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

        if (Settings.ForceSrgbGamma && _colorspace != BitmapColorSpace.Srgb)
        {
            if (_isHdr)
            {
                _logger.LogInformation(
                    "ForceSrgbGamma is enabled, but HDR content detected. HDR color space will be used instead of sRGB.");
            }
            else
            {
                _logger.LogWarning(
                    "ForceSrgbGamma is enabled, but the detected color space is not sRGB. Forcing sRGB gamma may lead to incorrect colors.");
            }
        }
    }

    private BitmapColorSpace GetFrameColorSpace(MediaFrame frame)
    {
        if (_colorspace != null) return _colorspace;

        if (_isHdr) return ColorSpaceHelper.BuildHdrColorSpace(frame.ColorTrc, frame.ColorPrimaries);
        return ColorSpaceHelper.BuildTargetColorSpace(frame.ColorTrc, frame.ColorPrimaries);
    }
}

public struct VideoFrameInfo
{
    public int Width;
    public int Height;
    public int BytesPerPixel;
    public bool IsHdr;
    public BitmapColorSpace ColorSpace;
    public int DataLength;
}

public struct AudioFrameInfo
{
    public int SampleRate;
    public int NumSamples;
    public int DataLength;
}

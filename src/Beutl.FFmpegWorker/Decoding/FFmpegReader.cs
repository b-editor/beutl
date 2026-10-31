using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
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

    // Fixed at open, even if a later decoded frame changes its layout.
    private AVChannelLayout _audioOutputLayout;
    private int AudioBytesPerFrame => _audioOutputLayout.nb_channels * sizeof(float);

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
    // A video packet avcodec_send_packet rejected with EAGAIN; it must be resent before any later packet.
    private MediaPacket? _pendingVideoPacket;
    private bool _hasPendingVideoPacket;
    private AudioFrameConverter? _sampleConverter;
    private long _audioNowTimestamp;
    private long _audioNextTimestamp;
    // The number of the decoded frame after the one on screen; once the stream has ended, where the last frame ends.
    private long _videoNowFrame;
    private bool _videoEnded;
    private long _videoDiscardBefore = long.MinValue;
    // The latest timestamp among packets decoded in full since the last seek.
    private long _videoDecodedPts = long.MinValue;
    // Frames before the first video frame show it, as AVFoundation does.
    private long _firstVideoFrame;
    // A frame stays on screen from its number until the next frame's, as ffmpeg's fps filter presents it.
    private MediaFrame? _shownVideoFrame;
    private long _shownFrameNumber = long.MinValue;
    private int _samplesReturn;
    private bool _audioSeek;
    // The target of a seek whose next frame starts after it; reads from there up to that frame are silence.
    private long _audioSilenceStart = long.MaxValue;
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
                _firstVideoFrame = _videoNowFrame;
                GetPacketColorSpace();

                var codec = _videoDecoder!.GetCodec();
                VideoInfo = new VideoStreamInfo(
                    codec?.LongName ?? "Unknown",
                    // Counted from the container start; an unknown count stays 0 so callers use the duration.
                    _videoStream!.NbFrames > 0 ? GetVideoFrameCount() : 0,
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
                    _audioOutputLayout.nb_channels)
                {
                    ChannelLayout = GetChannelLayout(_audioOutputLayout)
                };
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

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? result)
    {
        result = null;
        if (_audioDecoder == null || _currentAudioFrame == null) return false;

        int channels = _audioOutputLayout.nb_channels;
        // Decoder側でResampleされるかのチェックのため
        if (length == 0)
        {
            result = new AudioBuffer(AudioInfo.SampleRate, channels, 0);
            return true;
        }

        int valueCount = checked(length * channels);
        float[] interleaved = ArrayPool<float>.Shared.Rent(valueCount);
        try
        {
            // Cleared, so the tail of a short read stays silent.
            Span<float> samples = interleaved.AsSpan(0, valueCount);
            samples.Clear();
            if (!ReadAudio(start, length, MemoryMarshal.AsBytes(samples), out _))
                return false;

            result = new AudioBuffer(AudioInfo.SampleRate, channels, length);
            AudioConversion.Deinterleave(samples, result);
            return true;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(interleaved);
        }
    }

    public unsafe bool ReadAudio(int start, int length, Span<byte> destination, out AudioFrameInfo info)
    {
        info = default;

        if (_audioDecoder == null || _currentAudioFrame == null)
            return false;

        if (length <= 0)
        {
            info = CreateAudioFrameInfo(0);
            return true;
        }

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
        int sampleSize = AudioBytesPerFrame;

        // SampleConverterを遅延初期化
        _sampleConverter ??= new AudioFrameConverter(AudioInfo.SampleRate, _audioOutputLayout);

        while ((!needGrab || GrabAudio()) && _currentAudioFrame.NbSamples > 0)
        {
            // 変換
            using var convertedFrame = _sampleConverter.ConvertFrame(_currentAudioFrame);
            _samplesReturn = convertedFrame.NbSamples;

            if (_samplesReturn < 0)
            {
                Debug.WriteLine("swr_convert error.");
                return false;
            }

            // The stream has no audio before this frame, either before it starts or in a gap of its timestamps.
            long position = (long)start + decoded;
            if (_audioNowTimestamp > position)
            {
                int silence = (int)Math.Min(_audioNowTimestamp - position, length - decoded);
                destination.Slice(checked(decoded * sampleSize), checked(silence * sampleSize)).Clear();
                decoded += silence;
                position += silence;
                if (decoded >= length)
                {
                    info = CreateAudioFrameInfo(decoded);
                    return true;
                }
            }

            int skip = OffsetInFrame(position, _audioNowTimestamp, _samplesReturn);
            if (skip < 0)
            {
                needGrab = true;
                continue;
            }

            int len = Math.Min(_samplesReturn - skip, length - decoded);
            new ReadOnlySpan<byte>(convertedFrame.Data[0] + ((long)skip * sampleSize), checked(len * sampleSize))
                .CopyTo(destination.Slice(checked(decoded * sampleSize)));
            decoded += len;

            if (decoded >= length)
            {
                info = CreateAudioFrameInfo(decoded);
                return true;
            }

            needGrab = skip + len >= _currentAudioFrame.NbSamples;
        }

        info = CreateAudioFrameInfo(decoded);
        return true;
    }

    // Where a read at the position starts in the frame, or -1 when the frame does not hold that position.
    internal static int OffsetInFrame(long position, long frameStart, int frameSamples)
    {
        long offset = position - frameStart;
        return offset >= 0 && offset < frameSamples ? (int)offset : -1;
    }

    internal static unsafe AVChannelLayout GetOutputLayout(AVChannelLayout decoderLayout)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decoderLayout.nb_channels);
        // Some decoders only learn their channels from the first frame; keep the legacy stereo output.
        if (decoderLayout.nb_channels == 0)
        {
            return new AVChannelLayout
            {
                order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
                nb_channels = 2,
                u = new AVChannelLayout_u { mask = ffmpeg.AV_CH_LAYOUT_STEREO }
            };
        }

        if (decoderLayout.order == AVChannelOrder.AV_CHANNEL_ORDER_NATIVE)
            return decoderLayout;

        if (decoderLayout.order == AVChannelOrder.AV_CHANNEL_ORDER_CUSTOM)
        {
            // Retyping can free the map. Never retype the decoder's shallow ChLayout copy.
            AVChannelLayout copy = default;
            try
            {
                ffmpeg.av_channel_layout_copy(&copy, &decoderLayout).ThrowIfError();
                if (ffmpeg.av_channel_layout_retype(&copy, AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
                        ffmpeg.AV_CHANNEL_LAYOUT_RETYPE_FLAG_LOSSLESS) == 0)
                    return copy;
            }
            finally
            {
                ffmpeg.av_channel_layout_uninit(&copy);
            }
        }

        // UNSPEC (and layouts that cannot be represented losslessly) retain their channel indices.
        return new AVChannelLayout { order = AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC, nb_channels = decoderLayout.nb_channels };
    }

    internal static AudioChannelLayout GetChannelLayout(AVChannelLayout layout)
    {
        const ulong speakerBits = (1UL << 18) - 1;
        ulong downmixBits = ffmpeg.AV_CH_STEREO_LEFT | ffmpeg.AV_CH_STEREO_RIGHT;
        if (layout.order != AVChannelOrder.AV_CHANNEL_ORDER_NATIVE || (layout.u.mask & ~(speakerBits | downmixBits)) != 0)
            return AudioChannelLayout.Discrete(layout.nb_channels);

        ulong mask = layout.u.mask & speakerBits;
        // Aliases must also preserve the ascending speaker order of the unchanged native samples.
        // For example, FR+DL is ordered FR,DL in FFmpeg, not FL,FR as a stereo mask would claim.
        if (((layout.u.mask & ffmpeg.AV_CH_STEREO_LEFT) != 0 && (mask & ~(ulong)AudioSpeakers.FrontLeft) != 0)
            || ((layout.u.mask & ffmpeg.AV_CH_STEREO_RIGHT) != 0 && (mask & ~(ulong)AudioChannelLayout.Stereo.Speakers) != 0))
            return AudioChannelLayout.Discrete(layout.nb_channels);

        if ((layout.u.mask & ffmpeg.AV_CH_STEREO_LEFT) != 0)
            mask |= (uint)AudioSpeakers.FrontLeft;
        if ((layout.u.mask & ffmpeg.AV_CH_STEREO_RIGHT) != 0)
            mask |= (uint)AudioSpeakers.FrontRight;
        return BitOperations.PopCount(mask) == layout.nb_channels
            ? new AudioChannelLayout(layout.nb_channels, (AudioSpeakers)mask)
            : AudioChannelLayout.Discrete(layout.nb_channels);
    }

    internal sealed class AudioFrameConverter : SampleConverter
    {
        public AudioFrameConverter(int sampleRate) : this(sampleRate, new AVChannelLayout
        {
            order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
            nb_channels = 2,
            u = new AVChannelLayout_u { mask = ffmpeg.AV_CH_LAYOUT_STEREO }
        })
        {
        }

        // outputLayout is the pointer-free NATIVE or UNSPEC layout fixed by GetOutputLayout at open.
        public AudioFrameConverter(int sampleRate, AVChannelLayout outputLayout)
        {
            SetOpts(outputLayout, sampleRate, AVSampleFormat.AV_SAMPLE_FMT_FLT, 0);
        }

        public unsafe MediaFrame ConvertFrame(MediaFrame frame)
        {
            // SampleConverter compares CUSTOM map pointers and leaks the layouts it queries. Compare
            // their contents and release the copies, while keeping the existing per-frame FIFO behavior.
            ConfigureInput(frame);
            AudioFifo.Reset();
            var output = new MediaFrame
            {
                ChLayout = dstChLayout,
                SampleRate = dstSampleRate,
                Format = (int)dstFormat,
                NbSamples = frame.NbSamples
            };
            try
            {
                output.AllocateBuffer();
                AVFrame* source = frame;
                AVFrame* destination = output;
                int count = ffmpeg.swr_convert(pSwrContext, destination->extended_data, output.NbSamples,
                    source->extended_data, frame.NbSamples).ThrowIfError();
                AudioFifo.Add((void**)destination->extended_data, count);
                while (count == output.NbSamples)
                {
                    count = ffmpeg.swr_convert(pSwrContext, destination->extended_data, output.NbSamples, null, 0).ThrowIfError();
                    AudioFifo.Add((void**)destination->extended_data, count);
                }
                output.NbSamples = AudioFifo.Read((void**)destination->extended_data, output.NbSamples);
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        private unsafe void ConfigureInput(MediaFrame frame)
        {
            var inputLayout = frame.ChLayout;
            if (dstChLayout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC)
            {
                // A discrete output has no positions to rematrix into. Preserve input channel indices.
                inputLayout = new AVChannelLayout { order = AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC, nb_channels = inputLayout.nb_channels };
            }

            AVChannelLayout previousLayout = default;
            try
            {
                ffmpeg.av_opt_get_chlayout(pSwrContext, "ichl", 0, &previousLayout).ThrowIfError();
                long rate;
                AVSampleFormat format;
                ffmpeg.av_opt_get_int(pSwrContext, "isr", 0, &rate).ThrowIfError();
                ffmpeg.av_opt_get_sample_fmt(pSwrContext, "isf", 0, &format).ThrowIfError();
                if (ffmpeg.av_channel_layout_compare(&previousLayout, &inputLayout) != 0
                    || rate != frame.SampleRate || (int)format != frame.Format)
                {
                    var outputLayout = dstChLayout;
                    fixed (SwrContext** context = &pSwrContext)
                    {
                        ffmpeg.swr_alloc_set_opts2(context, &outputLayout, dstFormat, dstSampleRate,
                            &inputLayout, (AVSampleFormat)frame.Format, frame.SampleRate, 0, null).ThrowIfError();
                    }
                    ffmpeg.swr_init(pSwrContext).ThrowIfError();
                }
            }
            finally
            {
                ffmpeg.av_channel_layout_uninit(&previousLayout);
            }
        }
    }

    private unsafe AudioFrameInfo CreateAudioFrameInfo(int samples)
    {
        return new AudioFrameInfo
        {
            SampleRate = AudioInfo.SampleRate,
            NumChannels = _audioOutputLayout.nb_channels,
            NumSamples = samples,
            DataLength = checked(samples * AudioBytesPerFrame),
        };
    }

    private bool IsInCurrentAudioFrame(int start)
        => start >= Math.Min(_audioNowTimestamp, _audioSilenceStart) && start < _audioNextTimestamp;

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;

        MediaFrame? filterFrame = ReadVideoCore(frame);
        if (filterFrame == null) return false;

        // フレームの色空間を取得
        var colorSpace = _colorspace!;
        int width = filterFrame.Width;
        int height = filterFrame.Height;
        var colorType = _isHdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888;
        int bytesPerPixel = BytesPerPixel;
        var bmp = new Bitmap(width, height, colorType, BitmapAlphaType.Unpremul, colorSpace);

        try
        {
            FFmpegUtil.CopyPlane((IntPtr)filterFrame.Data[0], filterFrame.Linesize[0],
                bmp.Data, bmp.RowBytes, width * bytesPerPixel, height);
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
            var colorSpace = _colorspace!;
            int width = filterFrame.Width;
            int height = filterFrame.Height;
            int bytesPerPixel = BytesPerPixel;
            int byteCount = checked(width * height * bytesPerPixel);

            info = new VideoFrameInfo
            {
                Width = width,
                Height = height,
                BytesPerPixel = bytesPerPixel,
                ColorSpace = colorSpace,
                DataLength = byteCount,
            };

            if (byteCount > destination.Length)
                return false;

            int dstRowBytes = width * bytesPerPixel;
            fixed (byte* dst = destination)
            {
                FFmpegUtil.CopyPlane((IntPtr)filterFrame.Data[0], filterFrame.Linesize[0],
                    (IntPtr)dst, dstRowBytes, dstRowBytes, height);
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

    private MediaFrame? ReadVideoCore(int frame)
    {
        if (_videoStream == null || _videoDecoder == null || _shownVideoFrame == null)
            return null;

        // Leading frames map to the first frame, so reading them again does not seek.
        long target = Math.Max(frame, _firstVideoFrame);
        long skip = target - _videoNowFrame;

        // The frame on screen covers reads up to the next frame's number, and after the last frame, reads past its
        // end have nothing to decode.
        bool settled = IsUsableVideoFrame(_shownVideoFrame) && target >= _shownFrameNumber && (skip < 0 || _videoEnded);
        if (!settled)
        {
            // If the previous grab left the active frame unreferenced (a seek/grab that could not
            // reach a decodable frame), _videoNowFrame no longer describes a usable frame, so `skip`
            // (which can be 0) must not be trusted — force a fresh seek so a later in-range request
            // recovers the frame instead of repeatedly returning the empty one.
            bool currentUsable = !_videoEnded && IsUsableVideoFrame(ActiveVideoFrame);
            if (target < _shownFrameNumber || FFmpegSeekDecision.ShouldReseek(currentUsable, skip))
            {
                SeekVideo(target);
            }
            else
            {
                // Variable frame rate media numbers frames by time, so decode until the next frame starts after the
                // target instead of counting frames.
                while (target >= _videoNowFrame && AdvanceVideo())
                {
                }
            }
        }

        // Drop an unreferenced frame (see above) instead of feeding AV_PIX_FMT_NONE to the buffer source, which throws.
        var videoFrame = _shownVideoFrame;
        if (target >= _videoNowFrame || !IsUsableVideoFrame(videoFrame))
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

            _shownVideoFrame?.Dispose();
            _shownVideoFrame = null;

            _currentAudioFrame?.Dispose();
            _currentAudioFrame = null;

            _packet?.Dispose();
            _packet = null;

            _pendingVideoPacket?.Dispose();
            _pendingVideoPacket = null;

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

        long? position = _currentAudioFrame.Pts == ffmpeg.AV_NOPTS_VALUE
            ? null
            : SamplePosition(
                (_currentAudioFrame.Pts * ffmpeg.av_q2d(_audioStream.TimeBase)) - (AudioOriginTime * ffmpeg.av_q2d(s_time_base)),
                _audioDecoder.SampleRate);
        long fromPts = position ?? _audioNextTimestamp;

        // A frame over 0.1 s after the previous one follows a gap, as in aresample; less is rounding or drift.
        bool afterGap = fromPts - _audioNextTimestamp > _audioDecoder.SampleRate / 10;
        _audioNowTimestamp = _audioSeek || afterGap ? fromPts : _audioNextTimestamp;
        _audioSeek = false;
        _audioSilenceStart = long.MaxValue;
        _audioNextTimestamp = _audioNowTimestamp + _currentAudioFrame.NbSamples;
    }

    // The sample at the time, or null for a time an int sample position cannot address, as a crafted PTS gives.
    internal static long? SamplePosition(double seconds, int sampleRate)
    {
        double sample = Math.Round(seconds * sampleRate);
        return Math.Abs(sample) <= int.MaxValue ? (long)sample : null;
    }

    // Sample 0 is the container start; WAV gives none, and its timestamps start at 0.
    private long AudioOriginTime => _demuxer!.StartTime == ffmpeg.AV_NOPTS_VALUE ? 0 : _demuxer.StartTime;

    private unsafe void SeekAudio(long sample_pos)
    {
        if (_demuxer == null || _audioDecoder == null) return;

        long timestamp = sample_pos * 1000000 / _audioDecoder.SampleRate + AudioOriginTime;
        _demuxer.Seek(timestamp, -1);
        ffmpeg.avcodec_flush_buffers(_audioDecoder);
        _audioSeek = true;

        while (GrabAudio() && _audioNextTimestamp < sample_pos)
        {
        }

        if (_audioNowTimestamp > sample_pos)
            _audioSilenceStart = sample_pos;
    }

    private static bool IsUsableVideoFrame([NotNullWhen(true)] MediaFrame? frame)
        => frame != null && FFmpegFrameValidation.IsUsableVideoFrame(frame.Format, frame.Width, frame.Height);

    // The decoded frame takes the screen, and the frame after it decodes into the buffers the previous one held.
    private bool AdvanceVideo()
    {
        if (_isHWDecoding)
            (_shownVideoFrame, _swVideoFrame) = (_swVideoFrame, _shownVideoFrame);
        else
            (_shownVideoFrame, _currentVideoFrame) = (_currentVideoFrame, _shownVideoFrame);
        _shownFrameNumber = _videoNowFrame;

        if (GrabVideo())
            return true;

        // The last frame stays on screen until it ends, and at least for its own number.
        _videoEnded = true;
        _videoNowFrame = Math.Max(
            _shownFrameNumber + 1, GetVideoFrameNumber(_shownVideoFrame!.Pts + _shownVideoFrame.Duration));
        return false;
    }

    private bool GrabVideo()
    {
        if (_demuxer == null || _videoDecoder == null || _packet == null || _videoStream == null)
            return false;

        if (DecodePendingVideoPacket())
        {
            _videoNowFrame = GetNowFrame();
            return true;
        }

        foreach (var packet in _demuxer.ReadPackets(_packet))
        {
            if (packet.StreamIndex == _videoStream.Index
                && (DecodeVideoPacket(packet) || DecodePendingVideoPacket()))
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

    // Unlike MediaDecoder.DecodePacket, keeps a packet that avcodec_send_packet rejects with EAGAIN so it can be
    // resent instead of lost when the demuxer reuses its buffer, and copies hardware frame props after the transfer.
    private unsafe bool DecodeVideoPacket(MediaPacket? packet)
    {
        // A seek only needs reference pictures before its target. A packet that presents before a frame
        // already decoded in full, whose number is still at most the target, never reaches the screen.
        // Packet durations cannot tell this: they are decode order intervals, which differ from the
        // presentation intervals around a frame rate change. Decode normally when timestamps are unknown.
        // FFmpeg captures skip_frame per packet.
        bool superseded = _videoDiscardBefore != long.MinValue
            && packet != null && packet.Pts != ffmpeg.AV_NOPTS_VALUE
            && packet.Pts < _videoDecodedPts && GetVideoFrameNumber(_videoDecodedPts) <= _videoDiscardBefore;
        _videoDecoder!.SkipFrame = superseded ? AVDiscard.AVDISCARD_NONREF : AVDiscard.AVDISCARD_DEFAULT;
        int ret = _videoDecoder!.SendPacket(packet);
        if (ret >= 0 && !superseded && packet != null && packet.Pts != ffmpeg.AV_NOPTS_VALUE)
            _videoDecodedPts = Math.Max(_videoDecodedPts, packet.Pts);
        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) && packet != null)
        {
            KeepPendingVideoPacket(packet);
        }
        else
        {
            if (packet != null && ReferenceEquals(packet, _pendingVideoPacket))
                DiscardPendingVideoPacket();

            if (ret < 0 && ret != ffmpeg.AVERROR(ffmpeg.EAGAIN) && ret != ffmpeg.AVERROR_EOF)
                ret.ThrowIfError();
        }

        // Like DecodePacket, a receive error ends this packet without throwing.
        if (_videoDecoder.ReceiveFrame(_currentVideoFrame) < 0)
            return false;

        if (_isHWDecoding)
            TransferHardwareFrame(_currentVideoFrame!, _swVideoFrame!);

        return true;
    }

    // Resends the rejected packet before anything read after it. Receiving the pending output makes the
    // decoder accept the resend, so a second attempt only fails if the decoder breaks that contract.
    private bool DecodePendingVideoPacket()
    {
        for (int attempt = 0; _hasPendingVideoPacket && attempt < 2; attempt++)
        {
            if (DecodeVideoPacket(_pendingVideoPacket))
                return true;
        }

        DiscardPendingVideoPacket();
        return false;
    }

    private unsafe void KeepPendingVideoPacket(MediaPacket packet)
    {
        if (!ReferenceEquals(packet, _pendingVideoPacket))
        {
            _pendingVideoPacket ??= new MediaPacket();
            _pendingVideoPacket.Unref();
            ffmpeg.av_packet_ref(_pendingVideoPacket, packet).ThrowIfError();
        }

        _hasPendingVideoPacket = true;
    }

    private void DiscardPendingVideoPacket()
    {
        _pendingVideoPacket?.Unref();
        _hasPendingVideoPacket = false;
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

    private unsafe void SeekVideo(long frame)
    {
        if (_demuxer == null || _videoDecoder == null) return;

        void SeekOnly(long targetFrame)
        {
            long timestamp = (long)Math.Round(targetFrame * 1000000.0 / _videoAvgFrameRateDouble + _demuxer.StartTime,
                MidpointRounding.AwayFromZero);
            _demuxer.Seek(timestamp, -1);
            ffmpeg.avcodec_flush_buffers(_videoDecoder);
            DiscardPendingVideoPacket();
            _videoDecodedPts = long.MinValue;
            _shownVideoFrame!.Unref();
            _videoEnded = false;
            GrabVideo();
        }

        _videoDiscardBefore = frame;
        try
        {
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

            // The last frame that starts by the target takes the screen, or the first one when none does.
            if (IsUsableVideoFrame(ActiveVideoFrame))
            {
                while (AdvanceVideo() && _videoNowFrame <= frame)
                {
                }
            }
        }
        finally
        {
            _videoDiscardBefore = long.MinValue;
            _videoDecoder.SkipFrame = AVDiscard.AVDISCARD_DEFAULT;
        }
    }

    // Counts from the container start, like the seek target in SeekVideo.
    private long GetNowFrame()
    {
        var frame = ActiveVideoFrame;
        if (frame == null || _demuxer == null) return 0;
        return GetVideoFrameNumber(frame.Pts);
    }

    private long GetVideoFrameNumber(long pts)
    {
        double seconds = pts * _videoTimeBaseDouble - _demuxer!.StartTime / (double)ffmpeg.AV_TIME_BASE;
        return (long)(seconds * _videoAvgFrameRateDouble + 0.5);
    }

    // The frame number where the stream ends. The sample count also holds samples an edit list hides, and variable
    // frame rate media does not have one frame per number.
    private long GetVideoFrameCount()
    {
        if (_videoStream!.StartTime == ffmpeg.AV_NOPTS_VALUE || _videoStream.Duration <= 0)
            return _firstVideoFrame + _videoStream.NbFrames;

        return GetVideoFrameNumber(_videoStream.StartTime + _videoStream.Duration);
    }

    private void ConfigureVideoStream()
    {
        if (!_hasVideo || _videoStream == null) return;

        AVHWDeviceType? hwDeviceType = HardwareAcceleration.ToHWDeviceType((int)Settings.Acceleration.Value);

        try
        {
            // デコーダー作成
            _videoDecoder = MediaDecoder.CreateDecoder(
                _videoStream.CodecparRef,
                ctx =>
                {
                    ApplyThreadCount(ctx);

                    if (Settings.Acceleration.Value != FFmpegDecodingSettings.AccelerationOptions.Software)
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
        _shownVideoFrame = new MediaFrame();
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
        if (Settings.ThreadCount.Value != 0)
        {
            ctx.ThreadCount = Math.Min(
                Environment.ProcessorCount,
                Settings.ThreadCount.Value > 0 ? Settings.ThreadCount.Value : AutoThreadCountLimit);
        }
        else
        {
            ctx.ThreadCount = 0;
        }
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
                    ApplyThreadCount(ctx);
                    // Without it, FFmpeg keeps the PTS of a frame whose leading samples it skips (MP3 and Opus delay).
                    ctx.PktTimebase = _audioStream.TimeBase;
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

        _audioOutputLayout = GetOutputLayout(_audioDecoder.ChLayout);

        // フレーム初期化
        _currentAudioFrame = new MediaFrame();
        // The first frame takes its position from the PTS, as after a seek.
        _audioSeek = true;
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

        _logger.LogInformation("Video color space: {ColorSpace} ({Hdr})", _colorspace, _isHdr ? "HDR" : "SDR");
    }
}

public struct VideoFrameInfo
{
    public int Width;
    public int Height;
    public int BytesPerPixel;
    public BitmapColorSpace ColorSpace;
    public int DataLength;
}

public struct AudioFrameInfo
{
    public int SampleRate;
    public int NumChannels;
    public int NumSamples;
    public int DataLength;
}

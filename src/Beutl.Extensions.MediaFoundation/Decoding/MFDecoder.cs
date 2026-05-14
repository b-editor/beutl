// https://github.com/amate/MFVideoReader

using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Microsoft.Extensions.Logging;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D9;
using Vortice.MediaFoundation;
using Vortice.Multimedia;
using Vortice.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using IDirect3DDeviceManager9 = Windows.Win32.Media.MediaFoundation.IDirect3DDeviceManager9;
using MFRatio = Windows.Win32.Media.MediaFoundation.MFRatio;
using MFVIDEOFORMAT = Windows.Win32.Media.MediaFoundation.MFVIDEOFORMAT;
using WAVEFORMATEX = Windows.Win32.Media.Audio.WAVEFORMATEX;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;

using Beutl.Embedding.MediaFoundation;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
using Beutl.Extensions.MediaFoundation;
#endif

#pragma warning disable CA1416 // プラットフォームの互換性を検証

internal sealed class MFDecoder : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<MFDecoder>();
    private readonly string _file;
    private readonly MediaOptions _options;
    private readonly IMFSourceReader? _videoSourceReader;
    private readonly IMFSourceReader? _audioSourceReader;
    private readonly IMFAttributes? _attributes;
    private IDirect3DDeviceManager9? _deviceManager;
    private IDirect3DDevice9? _device;
    private IMFTransform? _transform;
    private MFMediaInfo _mediaInfo;
    private bool _useDXVA2;
    private IMFSample? _mfOutBufferSample;
    private long _firstGapTimeStamp = 0;
    private long _currentVideoTimeStamp = 0;
    private long _currentAudioTimeStamp = 0;

    // Threshold of consecutive ConvertColor HardError results before we surface
    // a single high-visibility log (not LogError per-frame). The decode loop keeps
    // running, but the user gets a clear signal that something systemic is wrong.
    private int _consecutiveConvertColorErrors = 0;
    private const int ConvertColorErrorReportThreshold = 30;

    private readonly MFSampleCache _sampleCache;

    // 現在のフレームからどれくらいの範囲ならシーケンシャル読み込みさせるかの閾値
    private readonly int _thresholdFrameCount = 30;

    // 現在のサンプル数からどれくらいの範囲ならシーケンシャル読み込みさせるかの閾値
    private readonly int _thresholdSampleCount = 30000;

    public MFDecoder(string file, MediaOptions options, MFDecodingExtension extension)
    {
        SharpGen.Runtime.Configuration.EnableObjectTracking = true;
        SharpGen.Runtime.Configuration.EnableReleaseOnFinalizer = true;
        SharpGen.Runtime.Configuration.UseThreadStaticObjectTracking = true;
        _file = file;
        _options = options;
        _thresholdFrameCount = extension.Settings.ThresholdFrameCount;
        _thresholdSampleCount = extension.Settings.ThresholdSampleCount;
        _sampleCache =
            new MFSampleCache(new(extension.Settings.MaxVideoBufferSize, extension.Settings.MaxAudioBufferSize));

        _useDXVA2 = InitializeDXVA2(extension.Settings.UseDXVA2);

        try
        {
            _attributes = MediaFactory.MFCreateAttributes(_useDXVA2 ? 3u : 1u);
            if (_useDXVA2)
            {
                _attributes.Set(SourceReaderAttributeKeys.D3DManager, new ComObject(_deviceManager!));
                _attributes.Set(SourceReaderAttributeKeys.DisableDxva, 0);
                _attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, true);
            }
            else
            {
                _attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true);
            }

            using (var sourceReader = MediaFactory.MFCreateSourceReaderFromURL(file, _attributes))
            {
                ConfigureDecoder(sourceReader, SourceReaderIndex.FirstVideoStream);
                ConfigureDecoder(sourceReader, SourceReaderIndex.FirstAudioStream);

                CheckMediaInfo(sourceReader);
            }

            if (_mediaInfo.VideoStreamIndex != -1 && options.StreamsToLoad.HasFlag(MediaMode.Video))
            {
                _videoSourceReader = MediaFactory.MFCreateSourceReaderFromURL(file, _attributes);
                SelectStream(_videoSourceReader, MediaTypeGuids.Video);

                try
                {
                    ConfigureDecoder(_videoSourceReader, (SourceReaderIndex)_mediaInfo.VideoStreamIndex);
                }
                catch (Exception ex)
                {
                    // Video stream configuration failed but the user asked for video.
                    // Falling through to an audio-only reader would silently drop the
                    // visual content the caller expected, so we let the original
                    // exception bubble up after logging — MFReader catches it.
                    _logger.LogError(ex, "ConfigureDecoder(_videoSourceReader, _mediaInfo.VideoStreamIndex) failed");
                    _mediaInfo.VideoStreamIndex = -1;
                    if (!options.StreamsToLoad.HasFlag(MediaMode.Audio))
                    {
                        throw;
                    }
                    _logger.LogWarning(
                        "Video stream configuration failed; continuing with audio-only because StreamsToLoad includes Audio");
                }
            }

            if (_mediaInfo.AudioStreamIndex != -1 && options.StreamsToLoad.HasFlag(MediaMode.Audio))
            {
                _audioSourceReader = MediaFactory.MFCreateSourceReaderFromURL(file, _attributes);
                SelectStream(_audioSourceReader, MediaTypeGuids.Audio);

                try
                {
                    ConfigureDecoder(_audioSourceReader, (SourceReaderIndex)_mediaInfo.AudioStreamIndex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ConfigureDecoder(_audioSourceReader, _mediaInfo.AudioStreamIndex) failed");
                    _mediaInfo.AudioStreamIndex = -1;
                }

                _sampleCache.ResetAudio((short)_mediaInfo.AudioFormat.BlockAlign);
            }

            if (_mediaInfo.VideoStreamIndex == -1 && _mediaInfo.AudioStreamIndex == -1)
            {
                const string message = "File contains no video or audio.";
                _logger.LogInformation(message);
                throw new Exception(message);
            }

            if (_useDXVA2 && _videoSourceReader != null)
            {
                // Send a message to the decoder to tell it to use DXVA2.
                nint videoDecoderPtr = _videoSourceReader.GetServiceForStream(
                    _mediaInfo.VideoStreamIndex,
                    Guid.Empty,
                    typeof(IMFTransform).GUID);
                using var videoDecoder = new IMFTransform(videoDecoderPtr);

                try
                {
                    videoDecoder.ProcessMessage(TMessageType.MessageSetD3DManager,
                        (nuint)Marshal.GetIUnknownForObject(_deviceManager!));
                    ChangeColorConvertSettingAndCreateBuffer();
                }
                catch (Exception ex)
                {
                    _useDXVA2 = false;
                    _logger.LogError(ex, "ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER) failed");
                    _logger.LogWarning(
                        "DXVA2 initialization failed mid-setup; falling back to software decoding. " +
                        "Some D3D attributes remain on the source reader but VideoProcessorMFT is disabled.");
                }
            }

            TestFirstReadSample();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred during initialization of the video stream.");
            throw;
        }
    }

    private IDirect3DDeviceManager9 CreateD3DDevManager(IntPtr video_window, out IDirect3DDevice9 device)
    {
        using var d3d = D3D9.Direct3DCreate9();

        var presentParams = new PresentParameters
        {
            // Not sure if these values are correct, or if
            // they even matter. (taken from DXVA_HD sample code)
            BackBufferWidth = 0,
            BackBufferHeight = 0,
            BackBufferFormat = Format.Unknown,
            BackBufferCount = 1,
            SwapEffect = SwapEffect.Discard,
            DeviceWindowHandle = video_window,
            Windowed = true,
            PresentFlags = PresentFlags.Video,
            FullScreenRefreshRateInHz = 0,
            PresentationInterval = 0
        };

        // D3DCREATE_HARDWARE_VERTEXPROCESSING specifies hardware vertex processing.
        device = d3d.CreateDevice(
            adapter: 0, // D3DADAPTER_DEFAULT
            deviceType: DeviceType.Hardware,
            focusWindow: video_window,
            createFlags: CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded,
            presentationParameters: presentParams);

        HRESULT hr = PInvoke.DXVA2CreateDirect3DDeviceManager9(
            out uint dev_manager_reset_token,
            out IDirect3DDeviceManager9? deviceManager);
        Marshal.ThrowExceptionForHR(hr);

        deviceManager.ResetDevice(
            (Windows.Win32.Graphics.Direct3D9.IDirect3DDevice9)Marshal.GetObjectForIUnknown(device.NativePointer),
            dev_manager_reset_token);

        return deviceManager;
    }

    private bool InitializeDXVA2(bool useDXVA2)
    {
        if (!useDXVA2) return false;
        try
        {
            _deviceManager = CreateD3DDevManager(PInvoke.GetDesktopWindow(), out _device);
            ComActivationHelpers.CreateComInstance(
                    PInvoke.CLSID_VideoProcessorMFT,
                    ComContext.InprocServer,
                    typeof(IMFTransform).GUID,
                    out IntPtr comObject1)
                .CheckError();
            _transform = new IMFTransform(comObject1);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred during initialization of DXVA2.");
            return false;
        }
    }

    public int ReadFrame(int frame, nint buf)
    {
        if (_mediaInfo.VideoStreamIndex == -1)
        {
            return 0;
        }

        int funcCopyBuffer(IMFSample sample)
        {
            IMFSample yuy2Sample = sample;
            if (_useDXVA2 && _mfOutBufferSample != null)
            {
                ConvertColorStatus status = ConvertColor(sample);
                if (status == ConvertColorStatus.Success)
                {
                    yuy2Sample = _mfOutBufferSample;
                    _consecutiveConvertColorErrors = 0;
                }
                else
                {
                    // VideoProcessorMFT (CLSID_VideoProcessorMFT) is documented
                    // as a synchronous MFT, so a 1:1 ProcessInput → ProcessOutput
                    // mapping is the contract. NEED_MORE_INPUT here implies a
                    // non-sync hardware variant; feeding the next ReadSample
                    // would queue it in _sampleCache and cause a duplicate
                    // ProcessInput on the next ReadFrame call. Drop the frame
                    // instead and surface the anomaly. HardError already
                    // logged its HRESULT inside ConvertColor.
                    if (status == ConvertColorStatus.NeedMoreInput)
                    {
                        _logger.LogWarning(
                            "VideoProcessorMFT returned NEED_MORE_INPUT for a synchronous converter at frame {Frame}; dropping",
                            frame);
                    }
                    else if (status == ConvertColorStatus.HardError)
                    {
                        _consecutiveConvertColorErrors++;
                        if (_consecutiveConvertColorErrors == ConvertColorErrorReportThreshold)
                        {
                            _logger.LogError(
                                "VideoProcessorMFT has failed {Count} frames in a row; preview is likely stuck on dropped frames. Toggle UseDXVA2 off in settings to recover.",
                                _consecutiveConvertColorErrors);
                        }
                    }
                    return 0;
                }
            }

            return SampleUtilities.SampleCopyToBuffer(yuy2Sample, buf, _mediaInfo.OutImageBufferSize);
        }

        IMFSample? sample = _sampleCache.SearchFrameSample(frame);
        if (sample != null)
        {
            return funcCopyBuffer(sample);
        }

        int currentFrame = _sampleCache.LastFrameNumber();

        if (currentFrame == -1)
        {
            currentFrame = TimestampUtilities.ConvertFrameFromTimeStamp(_currentVideoTimeStamp, _mediaInfo.Fps);
        }

        if (frame < currentFrame || (currentFrame + _thresholdFrameCount) < frame)
        {
            long destTimePosition = TimestampUtilities.ConvertTimeStampFromFrame(frame, _mediaInfo.Fps);
            SeekVideo(destTimePosition);
            _logger.LogDebug(
                "ReadFrame Seek currentFrame: {currentFrame}, destFrame: {destFrame} - destTimePos: {destTimePos} relativeFrame: {relativeFrame}",
                currentFrame, frame, TimestampUtilities.ConvertSecFrom100ns(destTimePosition), frame - currentFrame);
        }

        sample = ReadSample(_mediaInfo.VideoStreamIndex);
        while (sample != null)
        {
            try
            {
                int readSampleFrame = _sampleCache.LastFrameNumber();

                if (frame <= readSampleFrame)
                {
                    if ((readSampleFrame - frame) > 0)
                    {
                        _logger.LogWarning(
                            "wrong frame currentFrame: {currentFrame} targetFrame: {frame} readSampleFrame: {readSampleFrame} distance: {distance}",
                            currentFrame, frame, readSampleFrame, readSampleFrame - frame);
                    }

                    return funcCopyBuffer(sample);
                }

                sample = ReadSample(_mediaInfo.VideoStreamIndex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReadFrame: aborting read loop at frame {Frame} (currentFrame={CurrentFrame})",
                    frame, currentFrame);
                break;
            }
        }

        return 0;
    }

    public int ReadAudio(int start, int length, nint buf)
    {
        bool hitCache = _sampleCache.SearchAudioSampleAndCopyBuffer(start, length, buf);
        if (hitCache)
        {
            return length;
        }

        int currentSample = _sampleCache.LastAudioSampleNumber();
        if (currentSample == -1)
        {
            currentSample =
                TimestampUtilities.ConvertSampleFromTimeStamp(_currentAudioTimeStamp,
                    _mediaInfo.AudioFormat.SampleRate);
        }

        if (start < currentSample || (currentSample + _thresholdSampleCount) < start)
        {
            long destTimePosition =
                TimestampUtilities.ConvertTimeStampFromSample(start, _mediaInfo.AudioFormat.SampleRate);
            SeekAudio(destTimePosition);
            _logger.LogInformation(
                "ReadAudio Seek currentTimestamp: {currentTimestamp} - destTimePos: {destTimePos} relativeSample: {relativeSample}",
                TimestampUtilities.ConvertSecFrom100ns(_currentAudioTimeStamp),
                TimestampUtilities.ConvertSecFrom100ns(destTimePosition),
                start - currentSample);
        }

        IMFSample? sample = ReadSample(_mediaInfo.AudioStreamIndex);
        while (sample != null)
        {
            try
            {
                int readSampleNum = _sampleCache.LastAudioSampleNumber();

                if (start <= readSampleNum)
                {
                    if (_sampleCache.SearchAudioSampleAndCopyBuffer(start, length, buf))
                    {
                        return length;
                    }
                }

                sample = ReadSample(_mediaInfo.AudioStreamIndex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReadAudio: aborting read loop at sample {Start} (length={Length})",
                    start, length);
                break;
            }
        }

        return 0;
    }

    public MFMediaInfo GetMediaInfo() => _mediaInfo;

    private IMFSample? ReadSample(int streamIndex)
    {
        IMFSourceReader? reader;
        if (streamIndex == _mediaInfo.VideoStreamIndex && _videoSourceReader != null)
        {
            reader = _videoSourceReader;
        }
        else if (streamIndex == _mediaInfo.AudioStreamIndex && _audioSourceReader != null)
        {
            reader = _audioSourceReader;
        }
        else
        {
            _logger.LogWarning("MFDecoder.ReadSample: streamIndex is invalid");
            return null;
        }

        IMFSample sample = reader.ReadSample(
            streamIndex,
            SourceReaderControlFlag.None,
            out int actualStreamIndex,
            out SourceReaderFlag flags,
            out long timestamp);

        if (flags.HasFlag(SourceReaderFlag.EndOfStream))
        {
            _logger.LogTrace("End of stream - stream: {streamIndex}", streamIndex);
            return null;
        }

        if (flags.HasFlag(SourceReaderFlag.NewStream))
        {
            _logger.LogTrace("New stream");
        }

        if (flags.HasFlag(SourceReaderFlag.NativeMediaTypeChanged))
        {
            _logger.LogTrace("Native type changed");
        }

        if (flags.HasFlag(SourceReaderFlag.CurrentMediaTypeChanged))
        {
            _logger.LogTrace("Current type changed");
            if (actualStreamIndex == _mediaInfo.VideoStreamIndex)
            {
                ChangeColorConvertSettingAndCreateBuffer();
            }
            else
            {
                _logger.LogError("unknown CurrentMediaTypeChanged");
            }
        }

        if (flags.HasFlag(SourceReaderFlag.StreamTick))
        {
            _logger.LogTrace("Stream tick");
        }

        if (flags.HasFlag(SourceReaderFlag.NativeMediaTypeChanged))
        {
            // The format changed. Reconfigure the decoder.
            try
            {
                ConfigureDecoder(reader, (SourceReaderIndex)actualStreamIndex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "MFDecoder.ReadSample MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED ConfigureDecoder failed");
                return null;
            }
        }

        // success!
        // add cache
        timestamp -= _firstGapTimeStamp;
        if (actualStreamIndex == _mediaInfo.VideoStreamIndex)
        {
            int frame = TimestampUtilities.ConvertFrameFromTimeStamp(timestamp, _mediaInfo.Fps);
            _sampleCache.AddFrameSample(frame, sample);
            _currentVideoTimeStamp = timestamp;
        }
        else if (actualStreamIndex == _mediaInfo.AudioStreamIndex)
        {
            int sampleNum = TimestampUtilities.ConvertSampleFromTimeStamp(timestamp, _mediaInfo.AudioFormat.SampleRate);
            _sampleCache.AddAudioSample(sampleNum, sample);
            _currentAudioTimeStamp = timestamp;
        }

        return sample;
    }

    private void SeekVideo(long destTimePosition)
    {
        _sampleCache.ResetVideo();
        _videoSourceReader!.SetCurrentPosition(destTimePosition);
    }

    private void SeekAudio(long destTimePosition)
    {
        _sampleCache.ResetAudio((short)_mediaInfo.AudioFormat.BlockAlign);
        _audioSourceReader!.SetCurrentPosition(destTimePosition);
    }

    // MF_E_TRANSFORM_NEED_MORE_INPUT — the transform hasn't accumulated enough
    // input for an output sample yet. Caller should feed another input sample
    // and retry rather than treating this as a frame-read failure.
    private const uint MF_E_TRANSFORM_NEED_MORE_INPUT = 0xC00D6D72;

    private enum ConvertColorStatus
    {
        Success,
        NeedMoreInput,
        HardError,
    }

    // NV12 -> YUY2. On Success, _mfOutBufferSample holds the converted frame.
    private ConvertColorStatus ConvertColor(IMFSample sample)
    {
        _transform!.ProcessInput(0, sample, 0);

        OutputDataBuffer mftOutputDataBuffer = new() { Sample = _mfOutBufferSample };
        Result result = _transform.ProcessOutput(ProcessOutputFlags.None, 1, ref mftOutputDataBuffer, out _);
        if (result.Success)
        {
            return ConvertColorStatus.Success;
        }

        if ((uint)result.Code == MF_E_TRANSFORM_NEED_MORE_INPUT)
        {
            return ConvertColorStatus.NeedMoreInput;
        }

        _logger.LogError("VideoProcessorMFT.ProcessOutput failed: HRESULT 0x{HResult:X8}", (uint)result.Code);
        return ConvertColorStatus.HardError;
    }

    private void ChangeColorConvertSettingAndCreateBuffer()
    {
        _mfOutBufferSample?.Dispose();
        _mfOutBufferSample = null;

        if (!_useDXVA2 || _videoSourceReader == null || _transform == null)
        {
            return;
        }

        using IMFMediaType mediaType = _videoSourceReader.GetCurrentMediaType(_mediaInfo.VideoStreamIndex);

        Guid subType = mediaType.GetGUID(MediaTypeAttributeKeys.Subtype);

        string? subTypeText = VideoFormatName.GetName(subType) ?? subType.ToString();
        _logger.LogInformation("GetCurrentMediaType subType: {SubType}", subTypeText);

        // If Source Reader + its video-processing attributes already delivered
        // YUY2, VideoProcessorMFT would be an identity transform. Skip it —
        // the ProcessInput/ProcessOutput round-trip is pure overhead in that case.
        if (subType == VideoFormatGuids.YUY2)
        {
            return;
        }

        ulong aspectRatio = mediaType.GetUInt64(MediaTypeAttributeKeys.PixelAspectRatio);
        uint pixelNume = (uint)(aspectRatio >> 32);
        uint pixelDenom = (uint)(aspectRatio & 0xffffffff);

        ulong frameSize = mediaType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        uint width = (uint)(frameSize >> 32);
        uint height = (uint)(frameSize & 0xffffffff);

        RECT rcSrc = RECT.FromXYWH(0, 0, (int)width, (int)height);
        Ratio srcPAR = new() { Numerator = (int)pixelNume, Denominator = (int)pixelDenom };
        RECT destRect =
            AspectRatioUtilities.CorrectAspectRatio(rcSrc, srcPAR, new() { Numerator = 1, Denominator = 1 });
        uint destWidth = (uint)destRect.right;
        uint destHeight = (uint)destRect.bottom;

        // 高さが16の倍数、幅が2の倍数になるように調節する
        const int AlignHeightSize = 16;
        const int AlignWidth = 2;

        static uint funcAlign(uint value, uint align)
        {
            if (value % align != 0)
            {
                uint alignedValue = (value / align * align) + align;
                value = alignedValue;
            }

            return value;
        }

        height = funcAlign(height, AlignHeightSize);
        destHeight = funcAlign(destHeight, AlignHeightSize);
        destWidth = funcAlign(destWidth, AlignWidth);

        // VideoProcessorMFT input/output configuration: NV12 → YUY2. Frame width
        // must be a multiple of 2 and height a multiple of 16 for the MFT to
        // accept the type pair.
        using var inputMediaType = MediaFactory.MFCreateMediaType();
        inputMediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        inputMediaType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        inputMediaType.Set(MediaTypeAttributeKeys.AllSamplesIndependent, true);
        inputMediaType.Set(MediaTypeAttributeKeys.FixedSizeSamples, true);

        inputMediaType.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((ulong)pixelNume << 32) | pixelDenom);
        inputMediaType.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)width << 32) | height);
        _transform.SetInputType(0, inputMediaType, 0);

        using var outputMediaType = MediaFactory.MFCreateMediaType();
        outputMediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        outputMediaType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.YUY2);
        outputMediaType.Set(MediaTypeAttributeKeys.AllSamplesIndependent, true);
        outputMediaType.Set(MediaTypeAttributeKeys.FixedSizeSamples, true);

        inputMediaType.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((ulong)1 << 32) | 1);
        outputMediaType.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)destWidth << 32) | destHeight);
        _transform.SetOutputType(0, outputMediaType, 0);

        _transform.ProcessMessage(TMessageType.MessageNotifyEndOfStream, 0);

        _transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, 0);

        // 出力先IMFSample作成
        var streamInfo = _transform.GetOutputStreamInfo(0);

        _mfOutBufferSample = MediaFactory.MFCreateSample();

        IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(streamInfo.Size);
        _mfOutBufferSample.AddBuffer(buffer);
    }

    // MF_E_INVALIDSTREAMNUMBER (0xC00D36B3) is how Source Reader signals "no more
    // streams" — that's our intended loop terminator. Any other failure should
    // surface so we don't silently mis-select streams.
    private const uint MF_E_INVALIDSTREAMNUMBER = 0xC00D36B3;

    private static void SelectStream(IMFSourceReader sourceReader, Guid selectMajorType)
    {
        for (int streamIndex = 0; true; streamIndex++)
        {
            try
            {
                using IMFMediaType currentMediaType = sourceReader.GetCurrentMediaType(streamIndex);

                var selected = sourceReader.GetStreamSelection(streamIndex);
                if (!selected)
                {
                    continue;
                }

                Guid majorType = currentMediaType.MajorType;
                if (majorType == selectMajorType)
                {
                    sourceReader.SetStreamSelection(streamIndex, true);
                }
                else
                {
                    sourceReader.SetStreamSelection(streamIndex, false);
                }
            }
            catch (Exception ex) when ((uint)ex.HResult == MF_E_INVALIDSTREAMNUMBER)
            {
                break;
            }
        }
    }

    private void ConfigureDecoder(IMFSourceReader sourceReader, SourceReaderIndex readerIndex)
    {
        using IMFMediaType nativeType = sourceReader.GetNativeMediaType(readerIndex, 0);

        Guid majorType = nativeType.GetGUID(MediaTypeAttributeKeys.MajorType);
        Guid subType;

        using var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, majorType);

        // Select a subtype.
        if (majorType == MediaTypeGuids.Video)
        {
            subType = VideoFormatGuids.YUY2;
        }
        else if (majorType == MediaTypeGuids.Audio)
        {
            subType = AudioFormatGuids.Pcm;

            uint nativeAudioChannels = nativeType.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels);

            if (2 < nativeAudioChannels)
            {
                _logger.LogWarning(
                    "ConfigureDecoder audio channel change - nativeAudioChannels: {nativeAudioChannels} -> 2",
                    nativeAudioChannels);
                type.Set(MediaTypeAttributeKeys.AudioNumChannels, 2);
            }
        }
        else
        {
            return;
        }

        type.Set(MediaTypeAttributeKeys.Subtype, subType);

        sourceReader.SetCurrentMediaType(readerIndex, type);
    }

    private unsafe void CheckMediaInfo(IMFSourceReader sourceReader)
    {
        _mediaInfo.VideoStreamIndex = -1;
        _mediaInfo.AudioStreamIndex = -1;
        for (int streamIndex = 0; true; ++streamIndex)
        {
            try
            {
                using IMFMediaType currentMediaType = sourceReader.GetCurrentMediaType(streamIndex);

                var selected = sourceReader.GetStreamSelection(streamIndex);
                if (!selected)
                {
                    continue;
                }

                Guid majorType = currentMediaType.MajorType;

                if (majorType == MediaTypeGuids.Video)
                {
                    _mediaInfo.VideoStreamIndex = streamIndex;
                }
                else if (majorType == MediaTypeGuids.Audio)
                {
                    _mediaInfo.AudioStreamIndex = streamIndex;
                }
                else
                {
                    Debug.Fail("");
                }
            }
            catch (Exception ex) when ((uint)ex.HResult == MF_E_INVALIDSTREAMNUMBER)
            {
                break;
            }
        }

        // 再生時間取得
        if (_mediaInfo.VideoStreamIndex != -1 || _mediaInfo.AudioStreamIndex != -1)
        {
            _mediaInfo.HnsDuration = (long)(ulong)sourceReader.GetPresentationAttribute(SourceReaderIndex.MediaSource,
                PresentationDescriptionAttributeKeys.Duration).Value;
        }
        else
        {
            const string message = "File contains no video or audio.";
            _logger.LogError(message);
            throw new Exception(message);
        }

        if (_mediaInfo.VideoStreamIndex != -1)
        {
            BitmapInfoHeader bih = default;

            // Read color metadata from the *native* (pre-conversion) type so that
            // HDR tags survive even though we configure YUY2 as the output subtype.
            // If the container doesn't tag these, leave them at Unknown — MFColorSpaceHelper
            // will fall back to sRGB/Rec.709 defaults.
            using (IMFMediaType nativeType = sourceReader.GetNativeMediaType(_mediaInfo.VideoStreamIndex, 0))
            {
                _mediaInfo.TransferFunction = TryGetEnum<VideoTransferFunction>(
                    nativeType, MediaTypeAttributeKeys.TransferFunction, VideoTransferFunction.FuncUnknown);
                _mediaInfo.ColorPrimaries = TryGetEnum<VideoPrimaries>(
                    nativeType, MediaTypeAttributeKeys.VideoPrimaries, VideoPrimaries.Unknown);
                _mediaInfo.YCbCrMatrix = TryGetEnum<VideoTransferMatrix>(
                    nativeType, MediaTypeAttributeKeys.YuvMatrix, VideoTransferMatrix.Unknown);
                _mediaInfo.IsHdr = MFColorSpaceHelper.IsHdrTransfer(_mediaInfo.TransferFunction);
            }

            using IMFMediaType mediaType = sourceReader.GetCurrentMediaType(_mediaInfo.VideoStreamIndex);

            MediaFactory.MFCreateMFVideoFormatFromMFMediaType(mediaType, out IntPtr pMFVF, out var pcbSize);
            try
            {
                var ppMFVF = (MFVIDEOFORMAT*)pMFVF;

                bih.Width = (int)ppMFVF->videoInfo.dwWidth;
                bih.Height = (int)ppMFVF->videoInfo.dwHeight;

                RECT rcSrc = RECT.FromXYWH(0, 0, bih.Width, bih.Height);
                RECT destRect = AspectRatioUtilities.CorrectAspectRatio(
                    rcSrc,
                    ppMFVF->videoInfo.PixelAspectRatio,
                    new MFRatio { Denominator = 1, Numerator = 1 });
                bih.Width = destRect.right;
                bih.Height = destRect.bottom;

                _mediaInfo.Fps = ppMFVF->videoInfo.FramesPerSecond;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pMFVF);
            }

            Guid subType = mediaType.GetGUID(MediaTypeAttributeKeys.Subtype);
            // Source Reader is always asked for YUY2 (see ConfigureDecoder), even
            // for HDR input — the original PQ/HLG transfer tag is carried up to
            // MFReader so Skia reinterprets the 8-bit samples in the right color
            // space. True 10-bit preservation would need a P010 output path;
            // that's a future extension on this code.
            bih.Compression = new FourCC("YUY2");
            bih.BitCount = 16;

            _mediaInfo.ImageFormat = bih;
            _mediaInfo.TotalFrameCount =
                TimestampUtilities.ConvertFrameFromTimeStamp(_mediaInfo.HnsDuration, _mediaInfo.Fps);
            _mediaInfo.OutImageBufferSize = bih.Width * bih.Height * (bih.BitCount / 8);
            _mediaInfo.VideoFormatName = VideoFormatName.GetName(subType) ?? subType.ToString();
        }

        if (_mediaInfo.AudioStreamIndex != -1)
        {
            // audio
            using IMFMediaType mediaType = sourceReader.GetCurrentMediaType(_mediaInfo.AudioStreamIndex);

            MediaFactory.MFCreateWaveFormatExFromMFMediaType(
                mediaType,
                out IntPtr pWF,
                out uint pcbSize,
                0);
            try
            {
                var ppWF = (WAVEFORMATEX*)pWF;

                ppWF->wFormatTag = (ushort)PInvoke.WAVE_FORMAT_PCM;

                _mediaInfo.AudioFormat = WaveFormat.MarshalFrom((nint)ppWF);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pWF);
            }

            _mediaInfo.TotalAudioSampleCount =
                TimestampUtilities.ConvertSampleFromTimeStamp(_mediaInfo.HnsDuration,
                    _mediaInfo.AudioFormat.SampleRate);
        }

        _logger.LogInformation("ChechMediaInfo: \n{MediaInfo}", _mediaInfo.GetMediaInfoText());
    }

    private void TestFirstReadSample()
    {
        long firstVideoTimeStamp = 0;
        long firstAudioTimeStamp = 0;

        if (_mediaInfo.VideoStreamIndex != -1 && _options.StreamsToLoad.HasFlag(MediaMode.Video))
        {
            _ = ReadSample(_mediaInfo.VideoStreamIndex) ?? throw new Exception("TestFirstReadSample() failed");
            _logger.LogInformation(
                "TestFirstReadSample firstVideoTimeStamp: {currentVideoTimeStamp} ({seconds})",
                _currentVideoTimeStamp, TimestampUtilities.ConvertSecFrom100ns(_currentVideoTimeStamp));
            firstVideoTimeStamp = _currentVideoTimeStamp;
            SeekVideo(0);
            _currentVideoTimeStamp = 0;
        }

        if (_mediaInfo.AudioStreamIndex != -1 && _options.StreamsToLoad.HasFlag(MediaMode.Audio))
        {
            _ = ReadSample(_mediaInfo.AudioStreamIndex) ?? throw new Exception("TestFirstReadSample() failed");
            _logger.LogInformation(
                "TestFirstReadSample firstAudioTimeStamp: {currentAudioTimeStamp} ({seconds})",
                _currentAudioTimeStamp, TimestampUtilities.ConvertSecFrom100ns(_currentAudioTimeStamp));
            firstAudioTimeStamp = _currentAudioTimeStamp;
            SeekAudio(0);
            _currentAudioTimeStamp = 0;
        }

        if (_mediaInfo.VideoStreamIndex != -1
            && _mediaInfo.AudioStreamIndex != -1
            && _options.StreamsToLoad == MediaMode.AudioVideo)
        {
            if (firstVideoTimeStamp != firstAudioTimeStamp)
            {
                _logger.LogWarning(
                    "fisrt timestamp gapped - firstVideoTimeStamp: {firstVideoTimeStamp} firstAudioTimestamp: {firstAudioTimestamp}",
                    firstVideoTimeStamp, firstAudioTimeStamp);
            }
        }

        _firstGapTimeStamp = Math.Max(firstVideoTimeStamp, firstAudioTimeStamp);
        _logger.LogInformation("TestFirstReadSample - firstGapTimeStamp: {firstGapTimeStamp}", _firstGapTimeStamp);
    }

    // MF_E_ATTRIBUTENOTFOUND (0xC00D36E6) is the only exception we want to
    // swallow here — IMFMediaType.Get* raises it when the optional color tag
    // is missing, which is normal for legacy containers. Any other HRESULT
    // (or non-MF exception) indicates a real failure that must surface.
    private const uint MF_E_ATTRIBUTENOTFOUND = 0xC00D36E6;

    private static TEnum TryGetEnum<TEnum>(IMFMediaType mediaType, Guid key, TEnum fallback)
        where TEnum : struct, Enum
    {
        try
        {
            uint value = mediaType.GetUInt32(key);
            return (TEnum)Enum.ToObject(typeof(TEnum), (int)value);
        }
        catch (Exception ex) when ((uint)ex.HResult == MF_E_ATTRIBUTENOTFOUND)
        {
            return fallback;
        }
    }

    public void Dispose()
    {
        _sampleCache.ResetVideo();
        _sampleCache.ResetAudio(0);

        if (_useDXVA2)
        {
            if (_mfOutBufferSample != null)
            {
                try
                {
                    _transform?.ProcessMessage(TMessageType.MessageNotifyEndOfStream, 0);
                }
                catch (SharpGenException ex)
                {
                    // Swallow only MF HRESULT failures here — letting OOM /
                    // ThreadAbort propagate is intentional. ProcessMessage during
                    // Dispose is best-effort cleanup; if it fails, log and continue.
                    _logger.LogError(ex, "_transform?.ProcessMessage failed");
                }

                _mfOutBufferSample.Dispose();
            }
        }

        _transform?.Dispose();

        _device?.Dispose();
        if (_deviceManager != null)
        {
            Marshal.ReleaseComObject(_deviceManager);
        }

        _videoSourceReader?.Dispose();
        _audioSourceReader?.Dispose();
    }
}

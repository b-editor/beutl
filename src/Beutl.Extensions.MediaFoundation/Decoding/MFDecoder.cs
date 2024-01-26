// https://github.com/amate/MFVideoReader

using System.Diagnostics;
using System.Runtime.InteropServices;

using Beutl.Logging;
using Beutl.Media.Decoding;

using Microsoft.Extensions.Logging;

using SharpDX;
using SharpDX.Direct3D9;
using SharpDX.MediaFoundation;
using SharpDX.Multimedia;
using SharpDX.Win32;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D9;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.MediaFoundation;

using DeviceType = SharpDX.Direct3D9.DeviceType;
using MediaType = SharpDX.MediaFoundation.MediaType;
using Sample = SharpDX.MediaFoundation.Sample;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

#pragma warning disable CA1416 // プラットフォームの互換性を検証

internal sealed class MFDecoder : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<MFDecoder>();
    private readonly string _file;
    private readonly MediaOptions _options;
    private readonly SourceReader? _videoSourceReader;
    private readonly SourceReader? _audioSourceReader;
    private readonly MediaAttributes? _attributes;
    private IDirect3DDeviceManager9? _deviceManager;
    private Device? _device;
    private Transform? _transform;
    private MFMediaInfo _mediaInfo;
    private readonly bool _useDXVA2;
    private Sample? _mfOutBufferSample;
    private long _firstGapTimeStamp = 0;
    private long _currentVideoTimeStamp = 0;
    private long _currentAudioTimeStamp = 0;

    private readonly MFSampleCache _sampleCache;

    // 現在のフレームからどれくらいの範囲ならシーケンシャル読み込みさせるかの閾値
    private readonly int _thresholdFrameCount = 30;

    // 現在のサンプル数からどれくらいの範囲ならシーケンシャル読み込みさせるかの閾値
    private readonly int _thresholdSampleCount = 30000;

    public MFDecoder(string file, MediaOptions options, MFDecodingExtension extension)
    {
        SharpDX.Configuration.EnableObjectTracking = true;
        SharpDX.Configuration.EnableReleaseOnFinalizer = true;
        SharpDX.Configuration.UseThreadStaticObjectTracking = true;
        _file = file;
        _options = options;
        _thresholdFrameCount = extension.Settings.ThresholdFrameCount;
        _thresholdSampleCount = extension.Settings.ThresholdSampleCount;
        _sampleCache = new MFSampleCache(new(extension.Settings.MaxVideoBufferSize, extension.Settings.MaxAudioBufferSize));

        _useDXVA2 = InitializeDXVA2(extension.Settings.UseDXVA2);

        try
        {
            _attributes = new MediaAttributes(_useDXVA2 ? 3 : 1);
            if (_useDXVA2)
            {
                _attributes.Set(SourceReaderAttributeKeys.D3DManager, new ComObject(_deviceManager));
                _attributes.Set(SourceReaderAttributeKeys.DisableDxva, 0);
                _attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, true);
            }
            else
            {
                _attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing.Guid, true);
            }

            using (var sourceReader = new SourceReader(file, _attributes))
            {
                ConfigureDecoder(sourceReader, SourceReaderIndex.FirstVideoStream);
                ConfigureDecoder(sourceReader, SourceReaderIndex.FirstAudioStream);

                CheckMediaInfo(sourceReader);
            }

            if (_mediaInfo.VideoStreamIndex != -1 && options.StreamsToLoad.HasFlag(MediaMode.Video))
            {
                _videoSourceReader = new SourceReader(file, _attributes);
                SelectStream(_videoSourceReader, MediaTypeGuids.Video);

                try
                {
                    ConfigureDecoder(_videoSourceReader, (SourceReaderIndex)_mediaInfo.VideoStreamIndex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ConfigureDecoder(_videoSourceReader, _mediaInfo.VideoStreamIndex) failed");
                    _mediaInfo.VideoStreamIndex = -1;
                }
            }
            if (_mediaInfo.AudioStreamIndex != -1 && options.StreamsToLoad.HasFlag(MediaMode.Audio))
            {
                _audioSourceReader = new SourceReader(file, _attributes);
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
                    SharpDX.Utilities.GetGuidFromType(typeof(Transform)));
                using var videoDecoder = new Transform(videoDecoderPtr);

                try
                {
                    videoDecoder.ProcessMessage(TMessageType.SetD3DManager, Marshal.GetIUnknownForObject(_deviceManager!));
                    ChangeColorConvertSettingAndCreateBuffer();

                }
                catch (Exception ex)
                {
                    _useDXVA2 = false;
                    _logger.LogError(ex, "ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER) failed");
                }
            }

            TestFirstReadSample();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred during initialization of the video stream.");
            throw;
        }
        finally
        {
        }
    }

    private IDirect3DDeviceManager9 CreateD3DDevManager(IntPtr video_window, out Device device)
    {
        using var d3d = new Direct3D();

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
        device = new Device(
            direct3D: d3d,
            adapter: 0, /* D3DADAPTER_DEFAULT */
            deviceType: DeviceType.Hardware,
            hFocusWindow: video_window,
            behaviorFlags: CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded,
            presentationParametersRef: [presentParams]);


        HRESULT hr = PInvoke.DXVA2CreateDirect3DDeviceManager9(
            out uint dev_manager_reset_token,
            out IDirect3DDeviceManager9? deviceManager);
        Marshal.ThrowExceptionForHR(hr);

        deviceManager.ResetDevice((IDirect3DDevice9)Marshal.GetObjectForIUnknown(device.NativePointer), dev_manager_reset_token);

        return deviceManager;
    }

    private bool InitializeDXVA2(bool useDXVA2)
    {
        if (!useDXVA2) return false;
        try
        {
            _deviceManager = CreateD3DDevManager(PInvoke.GetDesktopWindow(), out _device);
            _transform = new Transform(PInvoke.CLSID_VideoProcessorMFT);
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

        int funcCopyBuffer(Sample sample)
        {
            Sample yuy2Sample = sample;
            if (_useDXVA2 && _mfOutBufferSample != null)
            {
                ConvertColor(sample);
                yuy2Sample = _mfOutBufferSample;
            }

            return SampleUtilities.SampleCopyToBuffer(yuy2Sample, buf, _mediaInfo.OutImageBufferSize);
        }

        Sample? sample = _sampleCache.SearchFrameSample(frame);
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
            catch
            {
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
            currentSample = TimestampUtilities.ConvertSampleFromTimeStamp(_currentAudioTimeStamp, _mediaInfo.AudioFormat.SampleRate);
        }
        if (start < currentSample || (currentSample + _thresholdSampleCount) < start)
        {
            long destTimePosition = TimestampUtilities.ConvertTimeStampFromSample(start, _mediaInfo.AudioFormat.SampleRate);
            SeekAudio(destTimePosition);
            _logger.LogInformation(
                "ReadAudio Seek currentTimestamp: {currentTimestamp} - destTimePos: {destTimePos} relativeSample: {relativeSample}",
                TimestampUtilities.ConvertSecFrom100ns(_currentAudioTimeStamp),
                TimestampUtilities.ConvertSecFrom100ns(destTimePosition),
                start - currentSample);
        }

        Sample? sample = ReadSample(_mediaInfo.AudioStreamIndex);
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
            catch
            {
                break;
            }
        }

        return 0;
    }

    public MFMediaInfo GetMediaInfo() => _mediaInfo;

    private Sample? ReadSample(int streamIndex)
    {
        SourceReader? reader;
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

        Sample sample = reader.ReadSample(
            streamIndex,
            SourceReaderControlFlags.None,
            out int actualStreamIndex,
            out SourceReaderFlags flags,
            out long timestamp);

        if (flags.HasFlag(SourceReaderFlags.Endofstream))
        {
            _logger.LogTrace("End of stream - stream: {streamIndex}", streamIndex);
            return null;
        }
        if (flags.HasFlag(SourceReaderFlags.Newstream))
        {
            _logger.LogTrace("New stream");
        }
        if (flags.HasFlag(SourceReaderFlags.Nativemediatypechanged))
        {
            _logger.LogTrace("Native type changed");
        }
        if (flags.HasFlag(SourceReaderFlags.Currentmediatypechanged))
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
        if (flags.HasFlag(SourceReaderFlags.StreamTick))
        {
            _logger.LogTrace("Stream tick");
        }

        if (flags.HasFlag(SourceReaderFlags.Nativemediatypechanged))
        {
            // The format changed. Reconfigure the decoder.
            try
            {
                ConfigureDecoder(reader, (SourceReaderIndex)actualStreamIndex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MFDecoder.ReadSample MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED ConfigureDecoder failed");
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

    // NV12 -> YUY2
    private void ConvertColor(Sample sample)
    {
        _transform!.ProcessInput(0, sample, 0);

        TOutputDataBuffer mftOutputDataBuffer = new()
        {
            PSample = _mfOutBufferSample
        };

        _transform.ProcessOutput(TransformProcessOutputFlags.None, [mftOutputDataBuffer], out _);
    }

    private void ChangeColorConvertSettingAndCreateBuffer()
    {
        _mfOutBufferSample?.Dispose();
        _mfOutBufferSample = null;

        if (!_useDXVA2 || _videoSourceReader == null || _transform == null)
        {
            return;
        }

        using MediaType mediaType = _videoSourceReader.GetCurrentMediaType(_mediaInfo.VideoStreamIndex);

        Guid subType = mediaType.Get(MediaTypeAttributeKeys.Subtype);

        string? subTypeText = VideoFormatName.GetName(subType) ?? subType.ToString();
        _logger.LogInformation("GetCurrentMediaType subType: {SubType}", subTypeText);

        long aspectRatio = mediaType.Get(MediaTypeAttributeKeys.PixelAspectRatio);
        uint pixelNume = (uint)(aspectRatio >> 32);
        uint pixelDenom = (uint)(aspectRatio & 0xffffffff);

        long frameSize = mediaType.Get(MediaTypeAttributeKeys.FrameSize);
        uint width = (uint)(frameSize >> 32);
        uint height = (uint)(frameSize & 0xffffffff);

        RECT rcSrc = RECT.FromXYWH(0, 0, (int)width, (int)height);
        Ratio srcPAR = new() { Numerator = (int)pixelNume, Denominator = (int)pixelDenom };
        RECT destRect = AspectRatioUtilities.CorrectAspectRatio(rcSrc, srcPAR, new() { Numerator = 1, Denominator = 1 });
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

        // 色変換 NV12 -> YUY2
        // 動画の幅は 2の倍数、高さは 16の倍数でないとダメっぽい？
        using var inputMediaType = new MediaType();
        inputMediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        inputMediaType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        inputMediaType.Set(MediaTypeAttributeKeys.AllSamplesIndependent, 1); // UnCompressed
        inputMediaType.Set(MediaTypeAttributeKeys.FixedSizeSamples, 1); // UnCompressed

        // Todo: 後で確認
        inputMediaType.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((long)pixelNume << 32) | pixelDenom);
        inputMediaType.Set(MediaTypeAttributeKeys.FrameSize, ((long)width << 32) | height);
        _transform.SetInputType(0, inputMediaType, 0);

        using var outputMediaType = new MediaType();
        outputMediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        // YUY2
        outputMediaType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.YUY2);
        outputMediaType.Set(MediaTypeAttributeKeys.AllSamplesIndependent, 1); // UnCompressed
        outputMediaType.Set(MediaTypeAttributeKeys.FixedSizeSamples, 1); // UnCompressed

        inputMediaType.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((long)1 << 32) | 1);
        //spOutputMediaType.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((long)1 << 32) | 1);
        outputMediaType.Set(MediaTypeAttributeKeys.FrameSize, ((long)destWidth << 32) | destHeight);
        _transform.SetOutputType(0, outputMediaType, 0);

        _transform.ProcessMessage(TMessageType.NotifyEndOfStream, 0);

        _transform.ProcessMessage(TMessageType.NotifyBeginStreaming, 0);

        // 出力先IMFSample作成
        _transform.GetOutputStreamInfo(0, out TOutputStreamInformation streamInfo);

        _mfOutBufferSample = MediaFactory.CreateSample();

        MediaBuffer buffer = MediaFactory.CreateMemoryBuffer(streamInfo.CbSize);
        _mfOutBufferSample.AddBuffer(buffer);
    }

    private static void SelectStream(SourceReader sourceReader, Guid selectMajorType)
    {
        for (int streamIndex = 0; true; streamIndex++)
        {
            try
            {
                using MediaType currentMediaType = sourceReader.GetCurrentMediaType(streamIndex);

                sourceReader.GetStreamSelection(streamIndex, out SharpDX.Mathematics.Interop.RawBool selected);
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
            catch
            {
                break;
            }
        }
    }

    private void ConfigureDecoder(SourceReader sourceReader, SourceReaderIndex readerIndex)
    {
        using MediaType nativeType = sourceReader.GetNativeMediaType(readerIndex, 0);

        Guid majorType = nativeType.Get(MediaTypeAttributeKeys.MajorType);
        Guid subType;

        using var type = new MediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, majorType);

        // Select a subtype.
        if (majorType == MediaTypeGuids.Video)
        {
            subType = VideoFormatGuids.YUY2;
        }
        else if (majorType == MediaTypeGuids.Audio)
        {
            subType = AudioFormatGuids.Pcm;

            int nativeAudioChannels = nativeType.Get(MediaTypeAttributeKeys.AudioNumChannels);

            if (2 < nativeAudioChannels)
            {
                _logger.LogWarning("ConfigureDecoder audio channel change - nativeAudioChannels: {nativeAudioChannels} -> 2", nativeAudioChannels);
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

    private unsafe void CheckMediaInfo(SourceReader sourceReader)
    {
        _mediaInfo.VideoStreamIndex = -1;
        _mediaInfo.AudioStreamIndex = -1;
        for (int streamIndex = 0; true; ++streamIndex)
        {
            try
            {
                using MediaType currentMediaType = sourceReader.GetCurrentMediaType(streamIndex);

                sourceReader.GetStreamSelection(streamIndex, out SharpDX.Mathematics.Interop.RawBool selected);
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
            catch
            {
                break;
            }
        }

        // 再生時間取得
        if (_mediaInfo.VideoStreamIndex != -1 || _mediaInfo.AudioStreamIndex != -1)
        {
            _mediaInfo.HnsDuration = sourceReader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration);
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

            MediaType mediaType = sourceReader.GetCurrentMediaType(_mediaInfo.VideoStreamIndex);

            uint pcbSize = 0;
            HRESULT hr = PInvoke.MFCreateMFVideoFormatFromMFMediaType(
                (IMFMediaType)Marshal.GetObjectForIUnknown(mediaType.NativePointer),
                out MFVIDEOFORMAT* ppMFVF,
                &pcbSize);
            Marshal.ThrowExceptionForHR(hr.Value);

            bih.Width = (int)ppMFVF->videoInfo.dwWidth;
            bih.Height = (int)ppMFVF->videoInfo.dwHeight;

            RECT rcSrc = RECT.FromXYWH(0, 0, bih.Width, bih.Height);
            RECT destRect = AspectRatioUtilities.CorrectAspectRatio(rcSrc, ppMFVF->videoInfo.PixelAspectRatio, new MFRatio { Denominator = 1, Numerator = 1 });
            bih.Width = destRect.right;
            bih.Height = destRect.bottom;

            _mediaInfo.Fps = ppMFVF->videoInfo.FramesPerSecond;

            Marshal.FreeCoTaskMem((nint)ppMFVF);

            Guid subType = mediaType.Get(MediaTypeAttributeKeys.Subtype);
            // YUY2
            bih.Compression = new FourCC("YUY2");
            bih.BitCount = 16;

            _mediaInfo.ImageFormat = bih;
            _mediaInfo.TotalFrameCount =
                TimestampUtilities.ConvertFrameFromTimeStamp(_mediaInfo.HnsDuration, _mediaInfo.Fps);
            _mediaInfo.OutImageBufferSize = bih.Width * bih.Height * (bih.BitCount / 8);
            _mediaInfo.VideoFormatName = VideoFormatName.GetName(subType) ?? subType.ToString();
        }
        if (_mediaInfo.AudioStreamIndex != -1)
        {   // audio
            using MediaType mediaType = sourceReader.GetCurrentMediaType(_mediaInfo.AudioStreamIndex);

            uint pcbSize = 0;
            HRESULT hr = PInvoke.MFCreateWaveFormatExFromMFMediaType(
                (IMFMediaType)Marshal.GetObjectForIUnknown(mediaType.NativePointer),
                out WAVEFORMATEX* ppWF,
                &pcbSize,
                0);
            Marshal.ThrowExceptionForHR(hr.Value);

            ppWF->wFormatTag = (ushort)PInvoke.WAVE_FORMAT_PCM;

            _mediaInfo.AudioFormat = WaveFormat.MarshalFrom((nint)ppWF);

            Marshal.FreeCoTaskMem((nint)ppWF);

            _mediaInfo.TotalAudioSampleCount =
                TimestampUtilities.ConvertSampleFromTimeStamp(_mediaInfo.HnsDuration, _mediaInfo.AudioFormat.SampleRate);
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
                    _transform?.ProcessMessage(TMessageType.NotifyEndOfStream, 0);
                }
                catch (Exception ex)
                {
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

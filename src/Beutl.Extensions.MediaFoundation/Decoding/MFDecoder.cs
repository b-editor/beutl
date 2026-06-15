// https://github.com/amate/MFVideoReader

using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Microsoft.Extensions.Logging;
using SharpGen.Runtime;
using Vortice;
using Vortice.MediaFoundation;
using Vortice.Multimedia;
using Vortice.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using MFRatio = Windows.Win32.Media.MediaFoundation.MFRatio;
using MFVIDEOFORMAT = Windows.Win32.Media.MediaFoundation.MFVIDEOFORMAT;

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
    private readonly IMFSourceReader? _videoSourceReader;
    private readonly IMFAttributes? _attributes;
    private MFMediaInfo _mediaInfo;
    private long _firstGapTimeStamp = 0;
    private long _currentVideoTimeStamp = 0;

    private readonly MFSampleCache _sampleCache;

    // 現在のフレームからどれくらいの範囲ならシーケンシャル読み込みさせるかの閾値
    private readonly int _thresholdFrameCount = 30;

    public MFDecoder(string file, MediaOptions options, MFDecodingExtension extension)
    {
        SharpGen.Runtime.Configuration.EnableObjectTracking = true;
        SharpGen.Runtime.Configuration.EnableReleaseOnFinalizer = true;
        SharpGen.Runtime.Configuration.UseThreadStaticObjectTracking = true;
        _file = file;
        _options = options;
        _thresholdFrameCount = extension.Settings.ThresholdFrameCount;
        _sampleCache = new MFSampleCache(new(extension.Settings.MaxVideoBufferSize));

        try
        {
            _attributes = MediaFactory.MFCreateAttributes(1u);
            _attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true);

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
                    _logger.LogError(ex, "ConfigureDecoder(_videoSourceReader, _mediaInfo.VideoStreamIndex) failed");
                    _mediaInfo.VideoStreamIndex = -1;
                }
            }

            if (_mediaInfo.VideoStreamIndex == -1)
            {
                const string message = "File contains no decodable video stream.";
                _logger.LogInformation(message);
                throw new Exception(message);
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

    public int ReadFrame(int frame, nint buf)
    {
        if (_mediaInfo.VideoStreamIndex == -1)
        {
            return 0;
        }

        int funcCopyBuffer(IMFSample sample)
        {
            return SampleUtilities.SampleCopyToBuffer(sample, buf, _mediaInfo.OutImageBufferSize);
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
            catch
            {
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
            if (actualStreamIndex != _mediaInfo.VideoStreamIndex)
            {
                _logger.LogError(
                    "unexpected CurrentMediaTypeChanged on stream {streamIndex} (expected video stream {videoStreamIndex})",
                    actualStreamIndex, _mediaInfo.VideoStreamIndex);
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

        return sample;
    }

    private void SeekVideo(long destTimePosition)
    {
        _sampleCache.ResetVideo();
        _videoSourceReader!.SetCurrentPosition(destTimePosition);
    }

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
            catch
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
                    // Audio is decoded separately via NAudio in MFReader; ignore it here.
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
        if (_mediaInfo.VideoStreamIndex != -1)
        {
            _mediaInfo.HnsDuration = (long)(ulong)sourceReader.GetPresentationAttribute(SourceReaderIndex.MediaSource,
                PresentationDescriptionAttributeKeys.Duration).Value;
        }
        else
        {
            const string message = "File contains no decodable video stream.";
            _logger.LogError(message);
            throw new Exception(message);
        }

        if (_mediaInfo.VideoStreamIndex != -1)
        {
            BitmapInfoHeader bih = default;

            IMFMediaType mediaType = sourceReader.GetCurrentMediaType(_mediaInfo.VideoStreamIndex);

            MediaFactory.MFCreateMFVideoFormatFromMFMediaType(mediaType, out IntPtr pMFVF, out var pcbSize);
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

            Marshal.FreeCoTaskMem((nint)ppMFVF);

            Guid subType = mediaType.GetGUID(MediaTypeAttributeKeys.Subtype);
            // YUY2
            bih.Compression = new FourCC("YUY2");
            bih.BitCount = 16;

            _mediaInfo.ImageFormat = bih;
            _mediaInfo.TotalFrameCount =
                TimestampUtilities.ConvertFrameFromTimeStamp(_mediaInfo.HnsDuration, _mediaInfo.Fps);
            _mediaInfo.OutImageBufferSize = bih.Width * bih.Height * (bih.BitCount / 8);
            _mediaInfo.VideoFormatName = VideoFormatName.GetName(subType) ?? subType.ToString();
        }

        _logger.LogInformation("ChechMediaInfo: \n{MediaInfo}", _mediaInfo.GetMediaInfoText());
    }

    private void TestFirstReadSample()
    {
        long firstVideoTimeStamp = 0;

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

        _firstGapTimeStamp = firstVideoTimeStamp;
        _logger.LogInformation("TestFirstReadSample - firstGapTimeStamp: {firstGapTimeStamp}", _firstGapTimeStamp);
    }

    public void Dispose()
    {
        _sampleCache.ResetVideo();

        _videoSourceReader?.Dispose();
    }
}

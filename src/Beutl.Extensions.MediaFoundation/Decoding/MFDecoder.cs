// https://github.com/amate/MFVideoReader

using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SharpGen.Runtime;
using Vortice;
using Vortice.MediaFoundation;
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

internal sealed class MFDecoder : IMediaFoundationVideoDecoder
{
    private const int UnsupportedByteStreamType = unchecked((int)0xC00D36C4);
    private const int InvalidPosition = unchecked((int)0xC00D36E5);

    private readonly ILogger _logger;
    private readonly IMFSourceReader? _videoSourceReader;
    private readonly IMFAttributes? _attributes;
    private MFMediaInfo _mediaInfo;
    // The timestamp of the container start; MF leaves an MP4 video edit list's trim in its timestamps.
    private long _originTimeStamp = 0;
    // Frames before the first video frame show it.
    private int _firstFrame = 0;
    private long _currentVideoTimeStamp = 0;

    private readonly MFSampleCache _sampleCache;

    // 現在のフレームからどれくらいの範囲ならシーケンシャル読み込みさせるかの閾値
    private readonly int _thresholdFrameCount = 30;

    public MFDecoder(string file, MFDecodingExtension extension, ILogger? logger = null)
    {
        _logger = logger ?? Log.CreateLogger<MFDecoder>();
        _thresholdFrameCount = extension.Settings.ThresholdFrameCount.Value;
        _sampleCache = new MFSampleCache(new(extension.Settings.MaxVideoBufferSize.Value));

        try
        {
            _attributes = MediaFactory.MFCreateAttributes(1u);
            _attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true);

            using (var sourceReader = MediaFactory.MFCreateSourceReaderFromURL(file, _attributes))
            {
                // Probe first: configuring a missing video stream throws a generic MF error, which would hide
                // the NoVideoStreamException that lets MFReader fall back to NAudio for audio-only files.
                if (MFStreamProbe.FindVideoStreamIndex(sourceReader) == -1)
                {
                    const string message = "File contains no video stream.";
                    _logger.LogInformation(message);
                    throw new NoVideoStreamException(message);
                }

                ConfigureDecoder(sourceReader, SourceReaderIndex.FirstVideoStream);

                CheckMediaInfo(sourceReader);
            }

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

            if (_mediaInfo.VideoStreamIndex == -1)
            {
                // The video stream exists but failed to initialize. Surface it as a real error so other
                // decoders (e.g. FFmpeg) retry.
                const string message = "Failed to initialize the video stream.";
                _logger.LogError(message);
                throw new Exception(message);
            }

            TestFirstReadSample(file);
        }
        catch (NoVideoStreamException)
        {
            DisposeAfterInitializationFailure();
            throw;
        }
        catch (Exception ex)
        {
            if (ex is SharpGenException mfException && mfException.ResultCode.Code == UnsupportedByteStreamType)
            {
                _logger.LogDebug(ex, "Media Foundation does not support this media byte stream.");
            }
            else
            {
                _logger.LogError(ex, "An exception occurred during initialization of the video stream.");
            }
            DisposeAfterInitializationFailure();
            throw;
        }
    }

    public int ReadFrame(int frame, nint buf)
    {
        int funcCopyBuffer(IMFSample sample)
        {
            return SampleUtilities.SampleCopyToBuffer(sample, buf, _mediaInfo.OutImageBufferSize);
        }

        frame = Math.Max(frame, _firstFrame);
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
            try
            {
                SeekVideo(destTimePosition);
            }
            catch (SharpGenException ex) when (ex.ResultCode.Code == InvalidPosition)
            {
                // The frame is past the end of the stream.
                return 0;
            }

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
        IMFSourceReader reader = _videoSourceReader!;

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
                sample?.Dispose();
                _logger.LogError(ex,
                    "MFDecoder.ReadSample MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED ConfigureDecoder failed");
                return null;
            }
        }

        // success!
        // add cache
        timestamp -= _originTimeStamp;
        int frame = TimestampUtilities.ConvertFrameFromTimeStamp(timestamp, _mediaInfo.Fps);
        _sampleCache.AddFrameSample(frame, sample);
        _currentVideoTimeStamp = timestamp;

        return sample;
    }

    // Resets the cache only once the seek succeeded, so a rejected seek keeps the frames decoded so far.
    private void SeekVideo(long destTimePosition)
    {
        _videoSourceReader!.SetCurrentPosition(destTimePosition);
        _sampleCache.ResetVideo();
    }

    private static void SelectStream(IMFSourceReader sourceReader, Guid selectMajorType)
    {
        foreach ((int streamIndex, Guid majorType) in MFStreamProbe.EnumerateSelectedStreams(sourceReader))
        {
            sourceReader.SetStreamSelection(streamIndex, majorType == selectMajorType);
        }
    }

    private void ConfigureDecoder(IMFSourceReader sourceReader, SourceReaderIndex readerIndex)
    {
        using IMFMediaType nativeType = sourceReader.GetNativeMediaType(readerIndex, 0);

        Guid majorType = nativeType.GetGUID(MediaTypeAttributeKeys.MajorType);

        using var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, majorType);
        type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.YUY2);

        sourceReader.SetCurrentMediaType(readerIndex, type);
    }

    private unsafe void CheckMediaInfo(IMFSourceReader sourceReader)
    {
        _mediaInfo.VideoStreamIndex = -1;
        foreach ((int streamIndex, Guid majorType) in MFStreamProbe.EnumerateSelectedStreams(sourceReader))
        {
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

        // 再生時間取得
        _mediaInfo.HnsDuration = (long)(ulong)sourceReader.GetPresentationAttribute(SourceReaderIndex.MediaSource,
            PresentationDescriptionAttributeKeys.Duration).Value;

        BitmapInfoHeader bih = default;

        using IMFMediaType mediaType = sourceReader.GetCurrentMediaType(_mediaInfo.VideoStreamIndex);

        IntPtr pMFVF = IntPtr.Zero;
        try
        {
            MediaFactory.MFCreateMFVideoFormatFromMFMediaType(mediaType, out pMFVF, out var pcbSize);
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
        // YUY2
        bih.Compression = new FourCC("YUY2");
        bih.BitCount = 16;

        _mediaInfo.ImageFormat = bih;
        _mediaInfo.TotalFrameCount =
            TimestampUtilities.ConvertFrameFromTimeStamp(_mediaInfo.HnsDuration, _mediaInfo.Fps);
        _mediaInfo.OutImageBufferSize = MFFrameBufferSize.Calculate(
            bih.Width,
            bih.Height,
            bih.BitCount / 8);
        _mediaInfo.VideoFormatName = VideoFormatName.GetName(subType) ?? subType.ToString();

        // The source reports the container/bitstream tags on the native type.
        using (IMFMediaType nativeType = sourceReader.GetNativeMediaType(_mediaInfo.VideoStreamIndex, 0))
        {
            _mediaInfo.Color = MFColorTags.Read(nativeType);
        }

        _logger.LogInformation("ChechMediaInfo: \n{MediaInfo}", _mediaInfo.GetMediaInfoText());
    }

    private void TestFirstReadSample(string file)
    {
        _ = ReadSample(_mediaInfo.VideoStreamIndex) ?? throw new Exception("TestFirstReadSample() failed");
        _logger.LogInformation(
            "TestFirstReadSample firstVideoTimeStamp: {currentVideoTimeStamp} ({seconds})",
            _currentVideoTimeStamp, TimestampUtilities.ConvertSecFrom100ns(_currentVideoTimeStamp));
        long firstVideoTimeStamp = _currentVideoTimeStamp;
        SeekVideo(0);
        _currentVideoTimeStamp = 0;

        long firstVideoTime = MFVideoTimeOrigin.GetFirstVideoTime(file, firstVideoTimeStamp);
        _originTimeStamp = firstVideoTimeStamp - firstVideoTime;
        _firstFrame = TimestampUtilities.ConvertFrameFromTimeStamp(firstVideoTime, _mediaInfo.Fps);
        _logger.LogInformation("TestFirstReadSample - originTimeStamp: {originTimeStamp}", _originTimeStamp);
    }

    private void DisposeAfterInitializationFailure()
    {
        try
        {
            Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose Media Foundation decoder resources after initialization failure.");
        }
    }

    public void Dispose()
    {
        _sampleCache.ResetVideo();

        // Dispose can also be reached from MediaReader's finalizer thread, so callers
        // are not guaranteed to run on MFThread.Dispatcher.
        _videoSourceReader?.Dispose();
        _attributes?.Dispose();
    }
}

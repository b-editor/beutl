using System.Diagnostics.CodeAnalysis;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Microsoft.Extensions.Logging;
using MonoMac.AVFoundation;
using MonoMac.CoreMedia;
using MonoMac.CoreVideo;

namespace Beutl.Extensions.AVFoundation.Decoding;

public class AVFVideoStreamReader : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<AVFVideoStreamReader>();
    private readonly AVAsset _asset;

    private readonly AVFVideoSampleCache _sampleCache;

    // 現在のフレームからどれくらいの範囲ならシーケンシャル読み込みさせるかの閾値
    private readonly int _thresholdFrameCount;

    private readonly AVAssetTrack _track;
    private AVAssetReader _reader;
    private AVAssetReaderTrackOutput _output;
    private CMTime _currentTimestamp;


    public AVFVideoStreamReader(AVAsset asset, AVFDecodingExtension extension)
    {
        _asset = asset;
        _sampleCache = new AVFVideoSampleCache(
            new AVFSampleCacheOptions(MaxVideoBufferSize: extension.Settings?.MaxVideoBufferSize ?? 4));
        _thresholdFrameCount = extension.Settings?.ThresholdFrameCount ?? 30;

        _track = _asset.TracksWithMediaType(AVMediaType.Video)[0];

        _reader = AVAssetReader.FromAsset(_asset, out var error);
        if (error != null) throw new Exception(error.LocalizedDescription);

        _output = new AVAssetReaderTrackOutput(
            _track,
            new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32ARGB }.Dictionary);
        _output.AlwaysCopiesSampleData = false;

        _reader.AddOutput(_output);
        _reader.StartReading();

        var desc = _track.FormatDescriptions[0];
        var frameSize = new PixelSize(desc.VideoDimensions.Width, desc.VideoDimensions.Height);
        string codec = desc.VideoCodecType.ToString();
        float framerate = _track.NominalFrameRate;
        double duration = _track.TotalSampleDataLength / _track.EstimatedDataRate * 8d;
        VideoInfo = new VideoStreamInfo(
            codec,
            Rational.FromDouble(duration),
            frameSize,
            Rational.FromSingle(framerate));
    }

    ~AVFVideoStreamReader()
    {
        if (!IsDisposed)
        {
            DisposeCore(false);
        }
    }

    public VideoStreamInfo VideoInfo { get; }

    public bool IsDisposed { get; private set; }

    private CMSampleBuffer? ReadSample()
    {
        var buffer = _output.CopyNextSampleBuffer();
        if (!buffer.DataIsReady)
        {
            _logger.LogTrace("buffer.DataIsReady = false");
            return null;
        }

        if (!buffer.IsValid)
        {
            _logger.LogTrace("buffer is invalid.");
            return null;
        }

        // success!
        // add cache
        // timestamp -= _firstGapTimeStamp;
        _currentTimestamp = buffer.PresentationTimeStamp;
        int frame = CMTimeUtilities.ConvertFrameFromTimeStamp(_currentTimestamp, _track.NominalFrameRate);
        _sampleCache.Add(frame, buffer);

        return buffer;
    }

    private void Seek(CMTime timestamp)
    {
        _sampleCache.Reset();
        _reader.Dispose();
        _output.Dispose();

        _reader = AVAssetReader.FromAsset(_asset, out var error);
        if (error != null) throw new Exception(error.LocalizedDescription);
        _reader.TimeRange = new CMTimeRange { Start = timestamp, Duration = CMTime.PositiveInfinity };

        _output = new AVAssetReaderTrackOutput(
            _track,
            new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32ARGB }.Dictionary);
        _output.AlwaysCopiesSampleData = false;
        _reader.AddOutput(_output);

        _reader.StartReading();
    }

    public bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        CMSampleBuffer? sample = _sampleCache.SearchSample(frame);
        if (sample != null)
        {
            image = AVFSampleUtilities.ConvertToBgra(sample);
            if (image != null)
                return true;
        }

        int currentFrame = _sampleCache.LastFrameNumber();

        if (currentFrame == -1)
        {
            currentFrame =
                CMTimeUtilities.ConvertFrameFromTimeStamp(_currentTimestamp, _track.NominalFrameRate);
        }

        if (frame < currentFrame || (currentFrame + _thresholdFrameCount) < frame)
        {
            CMTime destTimePosition = CMTimeUtilities.ConvertTimeStampFromFrame(frame, _track.NominalFrameRate);
            Seek(destTimePosition);
            _logger.LogDebug(
                "ReadFrame Seek currentFrame: {currentFrame}, destFrame: {destFrame} - destTimePos: {destTimePos} relativeFrame: {relativeFrame}",
                currentFrame, frame, destTimePosition.Seconds, frame - currentFrame);
        }

        sample = ReadSample();
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

                    image = AVFSampleUtilities.ConvertToBgra(sample);
                    if (image != null)
                        return true;
                }

                sample = ReadSample();
            }
            catch
            {
                break;
            }
        }

        image = null;
        return false;
    }

    private void DisposeCore(bool disposing)
    {
        _sampleCache.Reset();
        _output.Dispose();
        _reader.Dispose();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        DisposeCore(true);

        GC.SuppressFinalize(this);
        IsDisposed = true;
    }
}

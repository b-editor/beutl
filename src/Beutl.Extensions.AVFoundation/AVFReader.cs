using System.Diagnostics.CodeAnalysis;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Microsoft.Extensions.Logging;
using MonoMac.AVFoundation;
using MonoMac.CoreMedia;
using MonoMac.CoreVideo;
using MonoMac.Foundation;

namespace Beutl.Extensions.AVFoundation.Decoding;

public unsafe sealed class AVFReader : MediaReader
{
    private readonly ILogger _logger = Log.CreateLogger<AVFReader>();
    private readonly AVAsset _asset;
    private readonly AVAssetTrack _videoTrack;
    private AVAssetReader _assetReader;
    private AVAssetReaderTrackOutput _videoReaderOutput;
    private string _file;
    private MediaOptions _options;
    private AVFDecodingExtension _extension;
    private CMTime _currentVideoTimestamp;
    private AVFSampleCache _sampleCache;

    // 現在のフレームからどれくらいの範囲ならシーケンシャル読み込みさせるかの閾値
    private readonly int _thresholdFrameCount = 30;

    public AVFReader(string file, MediaOptions options, AVFDecodingExtension extension)
    {
        _file = file;
        _options = options;
        _extension = extension;

        _sampleCache = new AVFSampleCache(new AVFSampleCacheOptions());
        var url = NSUrl.FromFilename(file);
        _asset = AVAsset.FromUrl(url);
        _assetReader = AVAssetReader.FromAsset(_asset, out var error);
        if (error != null) throw new Exception(error.LocalizedDescription);

        _videoTrack = _asset.TracksWithMediaType(AVMediaType.Video)[0];
        _videoReaderOutput = new AVAssetReaderTrackOutput(
            _videoTrack,
            NSDictionary.FromObjectsAndKeys(
                [CVPixelFormatType.CV32ARGB],
                [CVPixelBuffer.PixelFormatTypeKey]));
        _videoReaderOutput.AlwaysCopiesSampleData = false;
        _assetReader.AddOutput(_videoReaderOutput);

        _assetReader.StartReading();

        var desc = _videoTrack.FormatDescriptions[0];
        var frameSize = new PixelSize(desc.VideoDimensions.Width, desc.VideoDimensions.Height);
        string codec = desc.VideoCodecType.ToString();
        float framerate = _videoTrack.NominalFrameRate;
        double duration = _videoTrack.TotalSampleDataLength / _videoTrack.EstimatedDataRate * 8d;
        VideoInfo = new VideoStreamInfo(
            codec,
            Rational.FromDouble(duration),
            frameSize,
            Rational.FromSingle(framerate));
    }

    public override VideoStreamInfo VideoInfo { get; }

    public override AudioStreamInfo AudioInfo => throw new NotImplementedException();

    public override bool HasVideo => true;

    public override bool HasAudio => false;

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        throw new NotImplementedException();
    }

    private CMSampleBuffer? ReadSample()
    {
        var buffer = _videoReaderOutput.CopyNextSampleBuffer();
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
        int frame = CMTimeUtilities.ConvertFrameFromTimeStamp(_currentVideoTimestamp, _videoTrack.NominalFrameRate);
        _sampleCache.AddFrameSample(frame, buffer);
        _currentVideoTimestamp = buffer.PresentationTimeStamp;

        return buffer;
    }

    private void Seek(CMTime timestamp)
    {
        _sampleCache.ResetVideo();
        _assetReader.Dispose();
        _videoReaderOutput.Dispose();

        _assetReader = AVAssetReader.FromAsset(_asset, out var error);
        if (error != null) throw new Exception(error.LocalizedDescription);
        _assetReader.TimeRange = new CMTimeRange { Start = timestamp, Duration = CMTime.PositiveInfinity };

        _videoReaderOutput = new AVAssetReaderTrackOutput(
            _videoTrack,
            NSDictionary.FromObjectsAndKeys(
                [CVPixelFormatType.CV32ARGB],
                [CVPixelBuffer.PixelFormatTypeKey]));
        _videoReaderOutput.AlwaysCopiesSampleData = false;
        _assetReader.AddOutput(_videoReaderOutput);

        _assetReader.StartReading();
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        CMSampleBuffer? sample = _sampleCache.SearchFrameSample(frame);
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
                CMTimeUtilities.ConvertFrameFromTimeStamp(_currentVideoTimestamp, _videoTrack.NominalFrameRate);
        }

        if (frame < currentFrame || (currentFrame + _thresholdFrameCount) < frame)
        {
            CMTime destTimePosition = CMTimeUtilities.ConvertTimeStampFromFrame(frame, _videoTrack.NominalFrameRate);
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _asset.Dispose();
        _assetReader.Dispose();
        _sampleCache.ResetVideo();
        _videoTrack.Dispose();
        _videoReaderOutput.Dispose();
    }
}

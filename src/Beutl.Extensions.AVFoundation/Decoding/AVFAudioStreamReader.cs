using System.Diagnostics.CodeAnalysis;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Microsoft.Extensions.Logging;
using MonoMac.AudioToolbox;
using MonoMac.AVFoundation;
using MonoMac.CoreMedia;

namespace Beutl.Extensions.AVFoundation.Decoding;

public class AVFAudioStreamReader : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<AVFAudioStreamReader>();
    private readonly AVAsset _asset;
    private readonly MediaOptions _options;
    private readonly AVFAudioSampleCache _sampleCache;
    private readonly int _thresholdSampleCount;

    private readonly AVAssetTrack _audioTrack;
    private AVAssetReader _assetAudioReader;
    private AVAssetReaderTrackOutput _audioReaderOutput;
    private CMTime _currentAudioTimestamp = CMTime.Zero;
    private CMTime _firstGapTimestamp = CMTime.Zero;

    public AVFAudioStreamReader(AVAsset asset, MediaOptions options, AVFDecodingExtension extension)
    {
        _asset = asset;
        _options = options;
        _sampleCache =
            new AVFAudioSampleCache(
                new AVFSampleCacheOptions(MaxAudioBufferSize: extension.Settings?.MaxAudioBufferSize ?? 20));
        _thresholdSampleCount = extension.Settings?.ThresholdSampleCount ?? 30000;

        _sampleCache.Reset(4 * 2);

        _audioTrack = _asset.TracksWithMediaType(AVMediaType.Audio)[0];

        _assetAudioReader = AVAssetReader.FromAsset(_asset, out var error);
        if (error != null) throw new Exception(error.LocalizedDescription);

        _audioReaderOutput = new AVAssetReaderTrackOutput(
            _audioTrack,
            new AudioSettings()
            {
                Format = AudioFormatType.LinearPCM,
                LinearPcmBitDepth = 32,
                LinearPcmBigEndian = false,
                LinearPcmFloat = true,
                SampleRate = options.SampleRate,
                NumberChannels = 2,
            }.Dictionary);
        _assetAudioReader.AddOutput(_audioReaderOutput);

        _assetAudioReader.StartReading();

        var audioDesc = _audioTrack.FormatDescriptions[0];
        AudioInfo = new AudioStreamInfo(
            audioDesc.AudioFormatType.ToString(),
            Rational.FromDouble(_audioTrack.TotalSampleDataLength / _audioTrack.EstimatedDataRate * 8d),
            _audioTrack.NaturalTimeScale,
            audioDesc.AudioChannelLayout.Channels.Length);

        TestFirstReadSample();
    }

    ~AVFAudioStreamReader()
    {
        if (!IsDisposed)
        {
            DisposeCore(false);
        }
    }

    public bool IsDisposed { get; private set; }

    public AudioStreamInfo AudioInfo { get; }

    private void SeekAudio(CMTime timestamp)
    {
        // _audioTrack.
        _sampleCache.Reset(4 * 2);
        _assetAudioReader.Dispose();
        _audioReaderOutput.Dispose();

        _assetAudioReader = AVAssetReader.FromAsset(_asset, out var error);
        if (error != null) throw new Exception(error.LocalizedDescription);
        _assetAudioReader.TimeRange = new CMTimeRange { Start = timestamp, Duration = CMTime.PositiveInfinity };

        _audioReaderOutput = new AVAssetReaderTrackOutput(
            _audioTrack,
            new AudioSettings()
            {
                Format = AudioFormatType.LinearPCM,
                LinearPcmBitDepth = 32,
                LinearPcmBigEndian = false,
                LinearPcmFloat = true,
                SampleRate = _options.SampleRate,
                NumberChannels = 2,
            }.Dictionary);
        _audioReaderOutput.AlwaysCopiesSampleData = false;
        _assetAudioReader.AddOutput(_audioReaderOutput);

        _assetAudioReader.StartReading();
    }

    public bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        start = (int)((long)_options.SampleRate * start / AudioInfo.SampleRate);
        length = (int)((long)_options.SampleRate * length / AudioInfo.SampleRate);
        var buffer = new Pcm<Stereo32BitFloat>(_options.SampleRate, length);
        bool hitCache = _sampleCache.SearchAudioSampleAndCopyBuffer(start, length, buffer.Data);
        if (hitCache)
        {
            sound = buffer;
            return true;
        }

        int currentSample = _sampleCache.LastAudioSampleNumber();
        if (currentSample == -1)
        {
            currentSample = (int)_currentAudioTimestamp.Value;
        }

        if (start < currentSample || (currentSample + _thresholdSampleCount) < start)
        {
            var destTimePosition = new CMTime(start, _options.SampleRate);
            SeekAudio(destTimePosition);
        }

        CMSampleBuffer? sample = ReadAudioSample();

        while (sample != null)
        {
            try
            {
                int readSampleNum = _sampleCache.LastAudioSampleNumber();

                if (start <= readSampleNum)
                {
                    if (_sampleCache.SearchAudioSampleAndCopyBuffer(start, length, buffer.Data))
                    {
                        sound = buffer;
                        return true;
                    }
                }

                sample = ReadAudioSample();
            }
            catch
            {
                break;
            }
        }

        sound = buffer;
        return true;
    }

    private CMSampleBuffer? ReadAudioSample()
    {
        var buffer = _audioReaderOutput.CopyNextSampleBuffer();
        if (!buffer.DataIsReady)
        {
            _logger.LogTrace("buffer.DataIsReady = false");

            buffer = _audioReaderOutput.CopyNextSampleBuffer();
            if (!buffer.DataIsReady)
            {
                _logger.LogTrace("2 buffer.DataIsReady = false");
                return null;
            }

            // return null;
        }

        if (!buffer.IsValid)
        {
            _logger.LogTrace("buffer is invalid.");
            return null;
        }

        // success!
        // add cache
        var timestamp = buffer.PresentationTimeStamp;
        timestamp -= _firstGapTimestamp;
        int startSample = (int)timestamp.Value;
        _sampleCache.Add(startSample, buffer);
        _currentAudioTimestamp = timestamp;

        return buffer;
    }

    private void TestFirstReadSample()
    {
        _ = ReadAudioSample() ?? throw new Exception("TestFirstReadSample() failed");
        _logger.LogInformation(
            "TestFirstReadSample firstTimeStamp: {currentAudioTimeStamp}",
            _currentAudioTimestamp);
        CMTime firstAudioTimeStamp = _currentAudioTimestamp;
        SeekAudio(CMTime.Zero);
        _currentAudioTimestamp = CMTime.Zero;

        _firstGapTimestamp = firstAudioTimeStamp;
        _logger.LogInformation("TestFirstReadSample - firstGapTimeStamp: {firstGapTimeStamp}", _firstGapTimestamp);
    }

    private void DisposeCore(bool disposing)
    {
        _sampleCache.Reset(0);
        _audioReaderOutput.Dispose();
        _assetAudioReader.Dispose();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        DisposeCore(true);

        GC.SuppressFinalize(this);
        IsDisposed = true;
    }
}

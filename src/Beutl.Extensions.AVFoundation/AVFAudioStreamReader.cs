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
    
    private readonly AVAssetTrack _audioTrack;
    private AVAssetReader _assetAudioReader;
    private AVAssetReaderTrackOutput _audioReaderOutput;
    private CMTime _currentAudioTimestamp;
    private readonly int _thresholdSampleCount = 30000;

    public AVFAudioStreamReader(AVAsset asset, MediaOptions options)
    {
        _asset = asset;
        _options = options;
        _sampleCache = new AVFAudioSampleCache(new AVFSampleCacheOptions());
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
        _assetAudioReader.AddOutput(_audioReaderOutput);

        _assetAudioReader.StartReading();
    }

    public bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
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
            currentSample =
                CMTimeUtilities.ConvertFrameFromTimeStamp(_currentAudioTimestamp, _audioTrack.NaturalTimeScale);
        }

        if (start < currentSample || (currentSample + _thresholdSampleCount) < start)
        {
            CMTime destTimePosition = CMTimeUtilities.ConvertTimeStampFromFrame(start, _audioTrack.NaturalTimeScale);
            SeekAudio(destTimePosition);
            // _logger.LogInformation(
            //     "ReadAudio Seek currentTimestamp: {currentTimestamp} - destTimePos: {destTimePos} relativeSample: {relativeSample}",
            //     TimestampUtilities.ConvertSecFrom100ns(_currentAudioTimeStamp),
            //     TimestampUtilities.ConvertSecFrom100ns(destTimePosition),
            //     start - currentSample);
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

        buffer.Dispose();
        sound = null;
        return false;
    }

    private CMSampleBuffer? ReadAudioSample()
    {
        var buffer = _audioReaderOutput.CopyNextSampleBuffer();
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
        int startSample =
            CMTimeUtilities.ConvertFrameFromTimeStamp(_currentAudioTimestamp, _audioTrack.NaturalTimeScale);
        _sampleCache.Add(startSample, buffer);
        _currentAudioTimestamp = buffer.PresentationTimeStamp;

        return buffer;
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

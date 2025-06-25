using System;
using System.Collections.Generic;
using System.Linq;
using Beutl.Animation;
using Beutl.Audio.Graph.Animation;
using Beutl.Audio.Graph.Exceptions;
using Beutl.Audio.Graph.Math;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Graph.Integration;

/// <summary>
/// High-level composer that can mix multiple audio sources using the graph system
/// </summary>
public sealed class AudioComposer : IDisposable
{
    private readonly List<IAudioTrack> _tracks = new();
    private readonly AnimationSampler _animationSampler = new();
    private bool _disposed;

    public void AddTrack(IAudioTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _tracks.Add(track);
    }

    public void RemoveTrack(IAudioTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _tracks.Remove(track);
    }

    public void ClearTracks()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _tracks.Clear();
    }

    public Pcm<Stereo32BitFloat> Compose(TimeRange range, int sampleRate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_tracks.Count == 0)
        {
            // Return silence
            var sampleCount = (int)(range.Duration.TotalSeconds * sampleRate);
            return new Pcm<Stereo32BitFloat>(sampleRate, sampleCount);
        }

        try
        {
            return ComposeInternal(range, sampleRate);
        }
        catch (Exception ex)
        {
            throw new AudioGraphException("Failed to compose audio", ex);
        }
    }

    private Pcm<Stereo32BitFloat> ComposeInternal(TimeRange range, int sampleRate)
    {
        var sampleCount = (int)(range.Duration.TotalSeconds * sampleRate);
        
        // Create master output buffer
        using var masterBuffer = new AudioBuffer(sampleRate, 2, sampleCount);
        masterBuffer.Clear();

        // Process each track
        foreach (var track in _tracks.Where(t => t.IsEnabled && t.Overlaps(range)))
        {
            try
            {
                using var trackBuffer = RenderTrack(track, range, sampleRate);
                MixTrackIntoMaster(trackBuffer, masterBuffer, track.Volume);
            }
            catch (Exception ex)
            {
                // Log error but continue with other tracks
                Console.WriteLine($"Warning: Failed to render track {track.GetType().Name}: {ex.Message}");
            }
        }

        // Apply master effects if any
        ApplyMasterEffects(masterBuffer, range, sampleRate);

        // Convert to output format
        return ConvertToStereo32BitFloat(masterBuffer);
    }

    private AudioBuffer RenderTrack(IAudioTrack track, TimeRange range, int sampleRate)
    {
        var graph = track.GetAudioGraph();
        var context = new AudioProcessContext(range, sampleRate, _animationSampler);
        
        // Prepare animations for this track
        if (track is IAnimatable animatable)
        {
            _animationSampler.PrepareAnimations(animatable, range, sampleRate);
        }

        return graph.Process(context);
    }

    private static void MixTrackIntoMaster(AudioBuffer trackBuffer, AudioBuffer masterBuffer, float volume)
    {
        if (trackBuffer.ChannelCount != masterBuffer.ChannelCount ||
            trackBuffer.SampleCount != masterBuffer.SampleCount)
        {
            throw new AudioBufferException("Track buffer format does not match master buffer format");
        }

        for (int ch = 0; ch < masterBuffer.ChannelCount; ch++)
        {
            var trackData = trackBuffer.GetChannelData(ch);
            var masterData = masterBuffer.GetChannelData(ch);
            
            AudioMath.AddWithGain(trackData, masterData, volume / 100f);
        }
    }

    private static void ApplyMasterEffects(AudioBuffer buffer, TimeRange range, int sampleRate)
    {
        // Apply master limiter to prevent clipping
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            var channelData = buffer.GetChannelData(ch);
            AudioMath.ApplyLimiter(channelData, 1.0f, 10.0f);
        }
    }

    private static unsafe Pcm<Stereo32BitFloat> ConvertToStereo32BitFloat(AudioBuffer buffer)
    {
        var pcm = new Pcm<Stereo32BitFloat>(buffer.SampleRate, buffer.SampleCount);
        var pcmPtr = (Stereo32BitFloat*)pcm.Data;

        if (buffer.ChannelCount == 1)
        {
            // Mono to stereo
            var monoChannel = buffer.GetChannelData(0);
            for (int i = 0; i < buffer.SampleCount; i++)
            {
                float sample = monoChannel[i];
                pcmPtr[i] = new Stereo32BitFloat(sample, sample);
            }
        }
        else if (buffer.ChannelCount >= 2)
        {
            // Stereo or multi-channel (take first two channels)
            var leftChannel = buffer.GetChannelData(0);
            var rightChannel = buffer.GetChannelData(1);
            for (int i = 0; i < buffer.SampleCount; i++)
            {
                pcmPtr[i] = new Stereo32BitFloat(leftChannel[i], rightChannel[i]);
            }
        }

        return pcm;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var track in _tracks)
            {
                track.Dispose();
            }
            _tracks.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// Interface for audio tracks that can be used in the composer
/// </summary>
public interface IAudioTrack : IDisposable
{
    bool IsEnabled { get; }
    float Volume { get; } // 0-100%
    TimeRange TimeRange { get; }
    
    bool Overlaps(TimeRange range);
    AudioGraph GetAudioGraph();
}

/// <summary>
/// Simple implementation of IAudioTrack using GraphSound
/// </summary>
public sealed class SimpleAudioTrack : IAudioTrack
{
    private readonly GraphSound _sound;
    private bool _disposed;

    public SimpleAudioTrack(GraphSound sound, TimeRange timeRange, float volume = 100f)
    {
        _sound = sound ?? throw new ArgumentNullException(nameof(sound));
        TimeRange = timeRange;
        Volume = Math.Clamp(volume, 0f, 100f);
    }

    public bool IsEnabled => _sound.IsEnabled;
    public float Volume { get; set; } = 100f;
    public TimeRange TimeRange { get; set; }

    public bool Overlaps(TimeRange range)
    {
        return TimeRange.Overlaps(range);
    }

    public AudioGraph GetAudioGraph()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _sound.GetOrBuildGraph();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _sound.Dispose();
            _disposed = true;
        }
    }
}
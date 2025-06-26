using System;
using System.Collections.Generic;
using System.Linq;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Animation;
using Beutl.Audio.Graph.Exceptions;
using Beutl.Audio.Graph.Math;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Composing;

public class Composer : IComposer
{
    private readonly List<Sound> _sounds = new();
    private readonly AnimationSampler _animationSampler = new();
    private readonly InstanceClock _instanceClock = new();
    private bool _disposed;

    public Composer()
    {
        SampleRate = 44100;
    }

    ~Composer()
    {
        if (!IsDisposed)
        {
            OnDispose(false);
            IsDisposed = true;
        }
    }

    public IClock Clock => _instanceClock;

    public int SampleRate { get; }

    public bool IsDisposed { get; private set; }

    public bool IsAudioRendering { get; private set; }

    public void AddSound(Sound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        
        _sounds.Add(sound);
    }

    public void RemoveSound(Sound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        
        _sounds.Remove(sound);
    }

    public void ClearSounds()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _sounds.Clear();
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }

    protected virtual void ComposeCore(Beutl.Audio.Audio audio)
    {
        // Legacy method kept for compatibility
        // New implementation uses graph-based processing
    }

    public Pcm<Stereo32BitFloat>? Compose(TimeSpan timeSpan)
    {
        if (!IsAudioRendering)
        {
            try
            {
                IsAudioRendering = true;
                _instanceClock.AudioStartTime = timeSpan;
                
                // Use new graph-based composition
                var range = new TimeRange(timeSpan, TimeSpan.FromSeconds(1));
                return ComposeInternal(range, SampleRate);
            }
            finally
            {
                IsAudioRendering = false;
            }
        }
        else
        {
            return default;
        }
    }

    private Pcm<Stereo32BitFloat> ComposeInternal(TimeRange range, int sampleRate)
    {
        if (_sounds.Count == 0)
        {
            // Return silence
            var sampleCount = (int)(range.Duration.TotalSeconds * sampleRate);
            return new Pcm<Stereo32BitFloat>(sampleRate, sampleCount);
        }

        try
        {
            var sampleCount = (int)(range.Duration.TotalSeconds * sampleRate);
            
            // Create master output buffer
            using var masterBuffer = new AudioBuffer(sampleRate, 2, sampleCount);
            masterBuffer.Clear();

            // Process each sound
            foreach (var sound in _sounds.Where(s => s.IsEnabled))
            {
                try
                {
                    // Apply animations
                    sound.ApplyAnimations(_instanceClock);
                    
                    // Render sound using graph system
                    using var soundPcm = sound.Render(range, sampleRate);
                    
                    // Mix into master buffer
                    MixPcmIntoBuffer(soundPcm, masterBuffer, sound.Gain / 100f);
                }
                catch (Exception ex)
                {
                    // Log error but continue with other sounds
                    Console.WriteLine($"Warning: Failed to render sound {sound.GetType().Name}: {ex.Message}");
                }
            }

            // Apply master effects if any
            ApplyMasterEffects(masterBuffer);

            // Convert to output format
            return ConvertToStereo32BitFloat(masterBuffer);
        }
        catch (Exception ex)
        {
            throw new AudioGraphException("Failed to compose audio", ex);
        }
    }

    private static unsafe void MixPcmIntoBuffer(Pcm<Stereo32BitFloat> pcm, AudioBuffer buffer, float gain)
    {
        var pcmPtr = (Stereo32BitFloat*)pcm.Data;
        var leftChannel = buffer.GetChannelData(0);
        var rightChannel = buffer.GetChannelData(1);
        
        var mixLength = Math.Min(pcm.NumSamples, buffer.SampleCount);
        
        for (int i = 0; i < mixLength; i++)
        {
            leftChannel[i] += pcmPtr[i].Left * gain;
            rightChannel[i] += pcmPtr[i].Right * gain;
        }
    }

    private static void ApplyMasterEffects(AudioBuffer buffer)
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

    protected virtual void OnDispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var sound in _sounds)
            {
                sound.Dispose();
            }
            _sounds.Clear();
        }
    }
}

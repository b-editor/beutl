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
    private readonly AnimationSampler _animationSampler = new();
    private readonly InstanceClock _instanceClock = new();

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

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }

    protected virtual void ComposeCore(AudioContext context)
    {
    }

    public Pcm<Stereo32BitFloat>? Compose(TimeSpan timeSpan)
    {
        if (!IsAudioRendering)
        {
            try
            {
                IsAudioRendering = true;
                _instanceClock.AudioStartTime = timeSpan;

                // Create audio context
                using var context = new AudioContext(SampleRate, 2);

                // Let subclass build the audio graph
                ComposeCore(context);

                // Build and process the graph
                var range = new TimeRange(timeSpan, TimeSpan.FromSeconds(1));
                return ComposeWithContext(context, range);
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

    private Pcm<Stereo32BitFloat> ComposeWithContext(AudioContext context, TimeRange range)
    {
        try
        {
            // Build the audio graph from context
            var graph = context.BuildGraph();

            // Create process context
            var sampleCount = (int)(range.Duration.TotalSeconds * context.SampleRate);
            var processContext = new AudioProcessContext(range, context.SampleRate, _animationSampler);

            // Process the graph
            using var outputBuffer = graph.Process(processContext);

            // Apply master effects
            ApplyMasterEffects(outputBuffer);

            // Convert to output format
            return ConvertToStereo32BitFloat(outputBuffer);
        }
        catch (Exception ex)
        {
            throw new AudioGraphException("Failed to compose audio", ex);
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
        }
    }
}

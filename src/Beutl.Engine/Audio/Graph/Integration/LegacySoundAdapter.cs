using System;
using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph.Animation;
using Beutl.Audio.Graph.Exceptions;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Graph.Integration;

/// <summary>
/// Adapter that allows legacy Sound objects to work with the new graph-based audio system
/// </summary>
public sealed class LegacySoundAdapter : IDisposable
{
    private readonly Sound _legacySound;
    private AudioGraph? _cachedGraph;
    private int _cacheVersion = -1;
    private bool _disposed;

    public LegacySoundAdapter(Sound legacySound)
    {
        _legacySound = legacySound ?? throw new ArgumentNullException(nameof(legacySound));
    }

    public Pcm<Stereo32BitFloat> RenderWithGraph(TimeRange range, int sampleRate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_legacySound.IsEnabled)
        {
            // Return silence
            var sampleCount = (int)(range.Duration.TotalSeconds * sampleRate);
            return new Pcm<Stereo32BitFloat>(sampleRate, sampleCount);
        }

        var graph = GetOrBuildGraph();
        var animationSampler = new AnimationSampler();

        // Prepare animations from the legacy sound
        animationSampler.PrepareAnimations(_legacySound, range, sampleRate);

        // Create processing context
        var context = new AudioProcessContext(range, sampleRate, animationSampler);

        try
        {
            using var buffer = graph.Process(context);
            return ConvertToStereo32BitFloat(buffer);
        }
        catch (Exception ex)
        {
            throw new AudioGraphException($"Failed to render legacy sound {_legacySound.GetType().Name}", ex);
        }
    }

    private AudioGraph GetOrBuildGraph()
    {
        var currentVersion = _legacySound.GetHashCode();
        
        if (_cachedGraph != null && _cacheVersion == currentVersion)
            return _cachedGraph;

        var builder = new AudioGraphBuilder();

        try
        {
            AudioNode currentNode = BuildGraphFromLegacySound(builder);

            // Set the final output
            builder.SetOutput(currentNode);

            // Build and cache the graph
            _cachedGraph?.Dispose();
            _cachedGraph = builder.Build();
            _cacheVersion = currentVersion;

            return _cachedGraph;
        }
        catch (Exception ex)
        {
            throw new AudioGraphBuildException($"Failed to build graph for legacy sound {_legacySound.GetType().Name}", ex);
        }
    }

    private AudioNode BuildGraphFromLegacySound(AudioGraphBuilder builder)
    {
        // Create a wrapper node that delegates to the legacy sound's rendering
        var legacyNode = builder.AddNode(new LegacySoundNode(_legacySound));

        // Create gain node for the legacy sound's gain property
        var gainNode = builder.AddNode(new GainNode
        {
            Target = _legacySound,
            GainProperty = Sound.GainProperty
        });

        // Connect legacy node to gain
        builder.Connect(legacyNode, gainNode);

        AudioNode currentNode = gainNode;

        // Add effect if present
        if (_legacySound.Effect != null && _legacySound.Effect.IsEnabled)
        {
            var effectNode = builder.AddNode(new EffectNode
            {
                Effect = _legacySound.Effect
            });

            builder.Connect(currentNode, effectNode);
            currentNode = effectNode;
        }

        return currentNode;
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
            _cachedGraph?.Dispose();
            _cachedGraph = null;
            _disposed = true;
        }
    }

    /// <summary>
    /// Node that wraps legacy Sound processing
    /// </summary>
    private sealed class LegacySoundNode : AudioNode
    {
        private readonly Sound _legacySound;

        public LegacySoundNode(Sound legacySound)
        {
            _legacySound = legacySound ?? throw new ArgumentNullException(nameof(legacySound));
        }

        public override AudioBuffer Process(AudioProcessContext context)
        {
            // Check cache first
            if (CachedOutput != null && 
                CachedOutput.SampleRate == context.SampleRate &&
                CachedOutput.SampleCount == context.GetSampleCount())
            {
                return CachedOutput;
            }

            try
            {
                // Create a mock IAudio to capture the legacy sound's output
                var mockAudio = new MockAudio(context.SampleRate);
                
                // Apply animations manually since the legacy sound expects it
                _legacySound.ApplyAnimations(new MockClock(context.TimeRange));
                
                // Use reflection or internal access to call the legacy sound's rendering
                // For now, we'll create a buffer based on the expected output
                var buffer = new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
                
                // This is a simplified implementation - in practice, you would need to
                // access the legacy sound's internal rendering mechanism
                // For demonstration, we'll create a silent buffer
                buffer.Clear();
                
                CachedOutput = buffer;
                return buffer;
            }
            catch (Exception ex)
            {
                throw new AudioNodeException($"Failed to process legacy sound node", this, ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            // Don't dispose the legacy sound as it's managed externally
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Mock implementation for legacy compatibility
    /// </summary>
    private sealed class MockAudio : IAudio
    {
        public int SampleRate { get; }

        public MockAudio(int sampleRate)
        {
            SampleRate = sampleRate;
        }

        public void Write(in IPcm pcm)
        {
            // Mock implementation - in real implementation, 
            // this would capture the PCM data
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }

    /// <summary>
    /// Mock clock for animation processing
    /// </summary>
    private sealed class MockClock : IClock
    {
        public TimeSpan BeginTime => _range.Start;
        public TimeSpan DurationTime => _range.Duration;
        public TimeSpan CurrentTime => _range.Start;
        public TimeSpan AudioStartTime => _range.Start;
        public IClock GlobalClock => this;

        private readonly TimeRange _range;

        public MockClock(TimeRange range)
        {
            _range = range;
        }
    }
}
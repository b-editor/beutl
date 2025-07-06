using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Animation;
using Beutl.Audio.Graph.Exceptions;
using Beutl.Audio.Graph.Math;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Composing;

public class Composer : IComposer
{
    private readonly AnimationSampler _animationSampler = new();
    private readonly InstanceClock _instanceClock = new();
    private readonly ConditionalWeakTable<Sound, AudioNodeEntry> _audioCache = [];
    private readonly List<Sound> _currentSounds = new();

    private sealed class AudioNodeEntry
    {
        public AudioGraph? Graph { get; set; }
        public List<AudioNode> Nodes { get; set; } = new();
        public AudioNode? OutputNode { get; set; }
        public bool IsDirty { get; set; } = true;
        public EventHandler<RenderInvalidatedEventArgs>? InvalidatedHandler { get; set; }
    }

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

    protected virtual void ComposeCore()
    {
        // Default implementation: compose all sounds
        foreach (var sound in _currentSounds)
        {
            ComposeSound(sound, Clock);
        }
    }

    protected void AddSound(Sound sound)
    {
        _currentSounds.Add(sound);
    }

    protected void ClearSounds()
    {
        _currentSounds.Clear();
    }

    public Pcm<Stereo32BitFloat>? Compose(TimeSpan timeSpan)
    {
        if (!IsAudioRendering)
        {
            try
            {
                IsAudioRendering = true;
                _instanceClock.AudioStartTime = timeSpan;

                // Clear previous sounds list
                _currentSounds.Clear();

                // Let subclass populate sounds
                ComposeCore();

                // Build final audio graph
                return BuildFinalOutput(timeSpan);
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

    private Pcm<Stereo32BitFloat>? BuildFinalOutput(TimeSpan timeSpan)
    {
        var range = new TimeRange(timeSpan, TimeSpan.FromSeconds(1));

        try
        {
            // Multiple contexts - need to mix
            var buffers = new List<AudioBuffer>();

            // Process each context
            foreach (var kvp in _audioCache)
            {
                if (kvp.Value.Graph is not { } graph) continue;
                var processContext = new AudioProcessContext(range, SampleRate, _animationSampler);
                buffers.Add(graph.Process(processContext));
            }

            // Mix all buffers
            using var mixedBuffer = MixBuffers(buffers);

            // Dispose individual buffers
            foreach (var buffer in buffers)
            {
                buffer.Dispose();
            }

            // Apply master effects
            ApplyMasterEffects(mixedBuffer);

            // Convert to output format
            return ConvertToStereo32BitFloat(mixedBuffer);
        }
        catch (Exception ex)
        {
            throw new AudioGraphException("Failed to compose audio", ex);
        }
    }

    private AudioBuffer MixBuffers(List<AudioBuffer> buffers)
    {
        if (buffers.Count == 0)
            throw new ArgumentException("No buffers to mix");

        var firstBuffer = buffers[0];
        var mixedBuffer = new AudioBuffer(firstBuffer.SampleRate, firstBuffer.ChannelCount, firstBuffer.SampleCount);

        // Mix all buffers
        for (int ch = 0; ch < mixedBuffer.ChannelCount; ch++)
        {
            var mixedChannel = mixedBuffer.GetChannelData(ch);

            foreach (var buffer in buffers)
            {
                if (buffer.ChannelCount > ch)
                {
                    var sourceChannel = buffer.GetChannelData(ch);
                    var sampleCount = Math.Min(mixedBuffer.SampleCount, buffer.SampleCount);

                    for (int i = 0; i < sampleCount; i++)
                    {
                        mixedChannel[i] += sourceChannel[i];
                    }
                }
            }
        }

        return mixedBuffer;
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


    /// <summary>
    /// Invalidates the cache, forcing recreation on next use.
    /// </summary>
    public void InvalidateCache()
    {
        foreach (var kvp in _audioCache)
        {
            kvp.Value.Graph?.Dispose();
        }

        _audioCache.Clear();
    }

    /// <summary>
    /// Composes a sound with caching support and differential updates.
    /// </summary>
    protected void ComposeSound(Sound sound, IClock clock)
    {
        // Apply animations first
        sound.ApplyAnimations(clock);

        // Get or create cache entry
        if (!_audioCache.TryGetValue(sound, out var entry))
        {
            entry = new AudioNodeEntry();
            _audioCache.AddOrUpdate(sound, entry);

            // Register invalidation handler
            var handler = new EventHandler<RenderInvalidatedEventArgs>((s, e) => OnSoundInvalidated(sound, e));
            sound.Invalidated += handler;
            entry.InvalidatedHandler = handler;
        }

        if (entry.IsDirty)
        {
            using var context = new AudioContext(SampleRate, 2);

            // Begin differential update with previous nodes
            context.BeginUpdate(entry.Nodes);

            // Compose the sound
            var outputNode = sound.Compose(context);
            entry.OutputNode = outputNode;

            // Complete differential update
            context.EndUpdate();

            // Capture current nodes
            entry.Nodes.Clear();
            entry.Nodes.AddRange(context.Nodes);

            entry.IsDirty = false;
            entry.Graph = context.BuildGraph();
        }
    }

    private void OnSoundInvalidated(Sound sound, RenderInvalidatedEventArgs e)
    {
        if (_audioCache.TryGetValue(sound, out var entry))
        {
            entry.IsDirty = true;
        }
    }

    /// <summary>
    /// Cleans up cache entries for the given sounds.
    /// </summary>
    protected void CleanupSoundHandlers(IEnumerable<Sound> sounds)
    {
        foreach (var sound in sounds)
        {
            if (_audioCache.TryGetValue(sound, out var entry))
            {
                if (entry.InvalidatedHandler != null)
                {
                    sound.Invalidated -= entry.InvalidatedHandler;
                }

                _audioCache.Remove(sound);
            }
        }
    }

    protected virtual void OnDispose(bool disposing)
    {
        if (disposing)
        {
            // Clean up all contexts and event handlers
            foreach (var kvp in _audioCache)
            {
                if (kvp.Value.InvalidatedHandler != null)
                {
                    kvp.Key.Invalidated -= kvp.Value.InvalidatedHandler;
                }

                kvp.Value.Graph?.Dispose();
            }

            _audioCache.Clear();
            _currentSounds.Clear();
        }
    }
}

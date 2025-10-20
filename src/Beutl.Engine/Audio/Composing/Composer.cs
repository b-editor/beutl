using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Media;

namespace Beutl.Audio.Composing;

public class Composer : IComposer
{
    private readonly AnimationSampler _animationSampler = new();
    private readonly ConditionalWeakTable<Sound, AudioNodeEntry> _audioCache = [];
    private readonly List<Sound> _currentSounds = new();
    private readonly List<AudioNodeEntry> _currentEntry = new();

    private sealed class AudioNodeEntry : IDisposable
    {
        public List<AudioNode> Nodes { get; set; } = new();
        public AudioNode[]? OutputNodes { get; set; }
        public bool IsDirty { get; set; } = true;
        public EventHandler<RenderInvalidatedEventArgs>? InvalidatedHandler { get; set; }

        public void Dispose()
        {
            foreach (var node in Nodes)
            {
                node.Dispose();
            }
            Nodes.Clear();
        }
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

    public int SampleRate { get; init; }

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

    protected virtual void ComposeCore(TimeRange timeRange)
    {
        // Default implementation: compose all sounds
        _currentEntry.Clear();
        foreach (var sound in _currentSounds)
        {
            ComposeSound(sound, timeRange);
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

    public AudioBuffer? Compose(TimeRange timeRange)
    {
        if (!IsAudioRendering)
        {
            try
            {
                IsAudioRendering = true;

                // Clear previous sounds list
                _currentSounds.Clear();

                // Let subclass populate sounds
                ComposeCore(timeRange);

                // Build final audio graph
                return BuildFinalOutput(timeRange);
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

    private AudioBuffer? BuildFinalOutput(TimeRange range)
    {
        // Multiple contexts - need to mix
        var buffers = new List<AudioBuffer>();

        // Process each context
        foreach (var item in _currentEntry)
        {
            if (item.OutputNodes is not { } outputNodes) continue;
            var processContext = new AudioProcessContext(range, SampleRate, _animationSampler, range);
            foreach (var outputNode in outputNodes)
            {
                buffers.Add(outputNode.Process(processContext));
            }
        }

        // Mix all buffers
        var mixedBuffer = MixBuffers(buffers);

        // Dispose individual buffers
        foreach (var buffer in buffers)
        {
            buffer.Dispose();
        }

        if (mixedBuffer == null)
        {
            return new AudioBuffer(SampleRate, 2, (int)(range.Duration.TotalSeconds * SampleRate));
        }
        // Apply master effects
        ApplyMasterEffects(mixedBuffer);

        // Convert to output format
        return mixedBuffer;
    }

    private AudioBuffer? MixBuffers(List<AudioBuffer> buffers)
    {
        if (buffers.Count == 0)
            return null;

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

    /// <summary>
    /// Invalidates the cache, forcing recreation on next use.
    /// </summary>
    public void InvalidateCache()
    {
        foreach (var kvp in _audioCache)
        {
            kvp.Value.Dispose();
        }

        _audioCache.Clear();
    }

    /// <summary>
    /// Composes a sound with caching support and differential updates.
    /// </summary>
    protected void ComposeSound(Sound sound, TimeRange timeRange)
    {
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
            // AudioContextはDisposeしない。AudioNodeが解放されてしまうので
            var context = new AudioContext(SampleRate, 2);

            // Begin differential update with previous nodes
            context.BeginUpdate(entry.Nodes);

            // Compose the sound
            sound.Compose(context);
            entry.OutputNodes = context.GetOutputNodes().ToArray();

            // Complete differential update
            context.EndUpdate();

            // Capture current nodes
            entry.Nodes.Clear();
            entry.Nodes.AddRange(context.Nodes);

            entry.IsDirty = false;
        }

        _currentEntry.Add(entry);
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

                kvp.Value.Dispose();
            }

            _audioCache.Clear();
            _currentSounds.Clear();
        }
    }
}

using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Media;

namespace Beutl.Audio.Composing;

public class Composer : IComposer
{
    private readonly AnimationSampler _animationSampler = new();
    private readonly ConditionalWeakTable<Sound, AudioNodeEntry> _audioCache = [];
    private readonly List<AudioNodeEntry> _currentEntry = new();

    // The entries active in the previous Compose window and that window's range. A sound active last
    // window but not this one ended at the boundary; its graph still holds the latency tail, which the
    // next window flushes (a sound ending exactly on the boundary cannot self-recover — its terminal
    // clip window is full, so ClipNode.AppendFlushedTail has no room).
    private readonly List<AudioNodeEntry> _previousEntry = new();
    private TimeRange? _previousRange;

    private sealed class AudioNodeEntry : IDisposable
    {
        public List<AudioNode> Nodes { get; set; } = new();
        public AudioNode[]? OutputNodes { get; set; }
        public bool IsDirty { get; set; } = true;
        public int Version { get; set; }
        public EventHandler? EditedHandler { get; set; }

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

    public AudioBuffer? Compose(TimeRange timeRange, CompositionFrame frame)
    {
        if (!IsAudioRendering)
        {
            try
            {
                IsAudioRendering = true;

                _currentEntry.Clear();
                foreach (var resource in frame.Objects)
                {
                    if (resource is Sound.Resource sound)
                        ComposeSound(sound, timeRange);
                }

                // Build final audio graph
                var result = BuildFinalOutput(timeRange);

                // Record this window's active set so the next window can flush sounds that just ended.
                _previousEntry.Clear();
                _previousEntry.AddRange(_currentEntry);
                _previousRange = timeRange;

                return result;
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
        AudioBuffer? mixedBuffer = null;
        try
        {
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

            // Recover the latency tail of any sound that ended on the previous window boundary.
            AppendEndedSoundTails(range, buffers);

            // Mix all buffers
            mixedBuffer = MixBuffers(buffers);

            if (mixedBuffer == null)
            {
                return new AudioBuffer(SampleRate, 2, AudioProcessContext.GetSampleCount(range, SampleRate));
            }

            // Apply master effects
            ApplyMasterEffects(mixedBuffer);

            // Convert to output format
            return mixedBuffer;
        }
        catch
        {
            // Don't leak the mix buffer if a step after the mix throws.
            mixedBuffer?.Dispose();
            throw;
        }
        finally
        {
            // Dispose every consumed per-node buffer, even on a throw partway through.
            foreach (var buffer in buffers)
            {
                buffer.Dispose();
            }
        }
    }

    // Flushes the residual latency tail of every sound that was active last window but not this one, so
    // a lookahead limiter's held samples land at the start of the window that follows the clip end (the
    // tail belongs at [windowStart, windowStart + latency)). The drain produces a window-length buffer —
    // tail at the front, silence after — so it mixes like any other branch.
    private void AppendEndedSoundTails(TimeRange range, List<AudioBuffer> buffers)
    {
        // Only when this window continues sequentially from the previous one. After a seek/restart the
        // cached graph no longer abuts the new window (the limiter resets on the discontinuity anyway),
        // so flushing it would inject a stale tail at the wrong time.
        if (_previousRange is not { } previous || !IsContiguous(previous.End, range.Start))
            return;

        foreach (var entry in _previousEntry)
        {
            if (_currentEntry.Contains(entry))
                continue;
            if (entry.OutputNodes is not { } outputNodes)
                continue;

            foreach (var outputNode in outputNodes)
            {
                if (outputNode.GetTotalLatencySamples(SampleRate) <= 0)
                    continue;

                var flushContext = new AudioProcessContext(range, SampleRate, _animationSampler, range);
                buffers.Add(outputNode.Flush(flushContext));
            }
        }
    }

    private static bool IsContiguous(TimeSpan previousEnd, TimeSpan nextStart)
        => Math.Abs((nextStart - previousEnd).Ticks) <= TimeSpan.TicksPerMillisecond;

    private AudioBuffer? MixBuffers(List<AudioBuffer> buffers)
    {
        if (buffers.Count == 0)
            return null;

        var firstBuffer = buffers[0];
        var mixedBuffer = new AudioBuffer(firstBuffer.SampleRate, firstBuffer.ChannelCount, firstBuffer.SampleCount);

        // Dispose the mix buffer rather than leak it if a (possibly disposed) source read throws.
        try
        {
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
        catch
        {
            mixedBuffer.Dispose();
            throw;
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

        // The recorded entries were just disposed; drop them so the next window does not flush a freed
        // graph (and treats the post-invalidate window as a fresh, non-contiguous start).
        _previousEntry.Clear();
        _previousRange = null;
    }

    /// <summary>
    /// Composes a sound with caching support and differential updates.
    /// </summary>
    protected void ComposeSound(Sound.Resource resource, TimeRange timeRange)
    {
        var sound = resource.GetOriginal();
        // Get or create cache entry
        if (!_audioCache.TryGetValue(sound, out var entry))
        {
            entry = new AudioNodeEntry();
            _audioCache.AddOrUpdate(sound, entry);

            // Register invalidation handler
            var handler = new EventHandler((s, e) => OnSoundEdited(sound, e));
            sound.Edited += handler;
            entry.EditedHandler = handler;
        }

        // 今までSoundGroupに子要素が追加されたらEditedが発生していたのでIsDirtyが自動的にtrueになっていたが、
        // Resource側で子要素を追加するようになったので、Editedイベントが発生しなくなった。なので、Versionを比較して変更を検出するようにする
        if (entry.IsDirty || entry.Version != resource.Version)
        {
            // AudioContextはDisposeしない。AudioNodeが解放されてしまうので
            var context = new AudioContext(SampleRate, 2);

            // Begin differential update with previous nodes
            context.BeginUpdate(entry.Nodes);

            // Compose the sound
            sound.Compose(context, resource);
            entry.OutputNodes = context.GetOutputNodes().ToArray();

            // Complete differential update
            context.EndUpdate();

            // Capture current nodes
            entry.Nodes.Clear();
            entry.Nodes.AddRange(context.Nodes);

            entry.Version = resource.Version;
            entry.IsDirty = false;
        }

        _currentEntry.Add(entry);
    }

    private void OnSoundEdited(Sound sound, EventArgs e)
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
                if (entry.EditedHandler != null)
                {
                    sound.Edited -= entry.EditedHandler;
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
                if (kvp.Value.EditedHandler != null)
                {
                    kvp.Key.Edited -= kvp.Value.EditedHandler;
                }

                kvp.Value.Dispose();
            }

            _audioCache.Clear();
        }
    }
}

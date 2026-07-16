using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Audio.Composing;

public class Composer : IComposer
{
    private const int ActiveDisposeState = 0;
    private const int DisposingState = 1;
    private const int DisposedState = 2;

    private readonly AnimationSampler _animationSampler = new();
    private readonly ConditionalWeakTable<Sound, AudioNodeCache> _audioCache = [];
    private readonly List<AudioNodeEntry> _currentEntry = new();
    private int _disposeState;

    private sealed class AudioNodeCache : IDisposable
    {
        public AudioNodeEntry Frame { get; } = new();

        public AudioNodeEntry Auxiliary { get; } = new();

        public EventHandler? EditedHandler { get; set; }

        public AudioNodeEntry Get(RenderPullPurpose pullPurpose)
            => pullPurpose == RenderPullPurpose.Frame ? Frame : Auxiliary;

        public void MarkDirty()
        {
            Frame.IsDirty = true;
            Auxiliary.IsDirty = true;
        }

        public void Dispose()
        {
            Exception? failure = null;
            try
            {
                Frame.Dispose();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                Auxiliary.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }

    private sealed class AudioNodeEntry : IDisposable
    {
        public List<AudioNode> Nodes { get; set; } = new();
        public AudioNode[]? OutputNodes { get; set; }
        public bool IsDirty { get; set; } = true;
        public int Version { get; set; }
        public Sound.Resource? Resource { get; set; }

        public void Dispose()
        {
            AudioNode[] nodes = [.. Nodes];
            Nodes.Clear();
            OutputNodes = null;
            Resource = null;
            Exception? failure = null;
            foreach (var node in nodes)
            {
                try
                {
                    node.Dispose();
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                }
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }

    public Composer()
        : this(Graphics.Rendering.RenderIntent.Preview)
    {
    }

    public Composer(Graphics.Rendering.RenderIntent renderIntent)
    {
        RenderIntent = Graphics.Rendering.RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        SampleRate = 44100;
    }

    ~Composer()
    {
        if (!TryBeginDispose())
            return;

        try
        {
            OnDispose(false);
        }
        catch
        {
            // Finalizers must never allow cleanup failures to escape onto the finalizer thread.
        }
        finally
        {
            CompleteDispose();
        }
    }

    public int SampleRate { get; init; }

    /// <summary>The preview/delivery policy accepted by this composer.</summary>
    public Graphics.Rendering.RenderIntent RenderIntent { get; }

    public bool IsDisposed => Volatile.Read(ref _disposeState) == DisposedState;

    public bool IsAudioRendering { get; private set; }

    public void Dispose()
    {
        if (!TryBeginDispose())
            return;

        try
        {
            OnDispose(true);
        }
        finally
        {
            CompleteDispose();
            GC.SuppressFinalize(this);
        }
    }

    private bool TryBeginDispose()
        => Interlocked.CompareExchange(
            ref _disposeState,
            DisposingState,
            ActiveDisposeState) == ActiveDisposeState;

    private void CompleteDispose()
    {
        Volatile.Write(ref _disposeState, DisposedState);
    }

    public AudioBuffer? Compose(TimeRange timeRange, CompositionFrame frame)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (frame.RenderIntent != RenderIntent)
        {
            throw new ArgumentException(
                $"Composer requires a {RenderIntent} composition frame, but received {frame.RenderIntent}.",
                nameof(frame));
        }

        if (!IsAudioRendering)
        {
            try
            {
                IsAudioRendering = true;

                _currentEntry.Clear();
                foreach (var resource in frame.Objects)
                {
                    if (resource is Sound.Resource sound)
                        ComposeSound(sound, timeRange, frame.PullPurpose);
                }

                // Build final audio graph
                try
                {
                    return BuildFinalOutput(timeRange);
                }
                catch
                {
                    ResetCurrentEntriesAfterProcessingFailure();
                    throw;
                }
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        KeyValuePair<Sound, AudioNodeCache>[] entries = [.. _audioCache];
        _audioCache.Clear();
        CleanupAudioCaches(entries);
    }

    /// <summary>
    /// Composes a sound with caching support and differential updates.
    /// </summary>
    protected void ComposeSound(
        Sound.Resource resource,
        TimeRange timeRange,
        RenderPullPurpose pullPurpose)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var sound = resource.GetOriginal();
        // Get or create cache entry
        if (!_audioCache.TryGetValue(sound, out AudioNodeCache? cache))
        {
            cache = new AudioNodeCache();
            _audioCache.AddOrUpdate(sound, cache);

            // Register invalidation handler
            var handler = new EventHandler((s, e) => OnSoundEdited(sound, e));
            sound.Edited += handler;
            cache.EditedHandler = handler;
        }

        AudioNodeEntry entry = cache.Get(pullPurpose);

        // 今までSoundGroupに子要素が追加されたらEditedが発生していたのでIsDirtyが自動的にtrueになっていたが、
        // Resource側で子要素を追加するようになったので、Editedイベントが発生しなくなった。なので、Versionを比較して変更を検出するようにする
        if (entry.IsDirty
            || entry.Version != resource.Version
            || !ReferenceEquals(entry.Resource, resource))
        {
            var context = new AudioContext(SampleRate, 2);
            try
            {
                // Reused nodes remain owned by entry until the replacement graph commits. If any callback fails,
                // the union of the old and partially built graph is evicted and disposed before retry.
                context.BeginUpdate(entry.Nodes);
                sound.Compose(context, resource);
                AudioNode[] outputNodes = context.GetOutputNodes().ToArray();
                context.EndUpdate();
                AudioNode[] nodes = [.. context.Nodes];

                entry.Nodes.Clear();
                entry.Nodes.AddRange(nodes);
                entry.OutputNodes = outputNodes;
                entry.Version = resource.Version;
                entry.Resource = resource;
                entry.IsDirty = false;
            }
            catch
            {
                ResetFaultedBuild(entry, context);
                throw;
            }
        }

        _currentEntry.Add(entry);
    }

    private static void ResetFaultedBuild(AudioNodeEntry entry, AudioContext context)
    {
        try
        {
            // Until the replacement graph commits, AudioContext owns both its current nodes and all previous nodes
            // it has not already retired. Clearing the cache aliases after that one ownership sweep avoids invoking
            // derived AudioNode cleanup twice when EndUpdate itself was the operation that failed.
            context.Dispose();
        }
        catch
        {
            // Preserve the composition/EndUpdate failure after AudioContext's full best-effort cleanup sweep.
        }

        entry.Nodes.Clear();
        entry.OutputNodes = null;
        entry.Resource = null;
        entry.IsDirty = true;
    }

    private static void ResetFaultedEntry(AudioNodeEntry entry)
    {
        var ownedNodes = new HashSet<AudioNode>(ReferenceEqualityComparer.Instance);
        ownedNodes.UnionWith(entry.Nodes);
        entry.Nodes.Clear();
        entry.OutputNodes = null;
        entry.Resource = null;
        entry.IsDirty = true;

        foreach (AudioNode node in ownedNodes)
        {
            try
            {
                node.Dispose();
            }
            catch
            {
                // The composition callback/EndUpdate failure remains primary after the full cleanup sweep.
            }
        }
    }

    private void ResetCurrentEntriesAfterProcessingFailure()
    {
        var entries = new HashSet<AudioNodeEntry>(ReferenceEqualityComparer.Instance);
        entries.UnionWith(_currentEntry);
        _currentEntry.Clear();
        foreach (AudioNodeEntry entry in entries)
        {
            ResetFaultedEntry(entry);
        }
    }

    private void OnSoundEdited(Sound sound, EventArgs e)
    {
        if (_audioCache.TryGetValue(sound, out AudioNodeCache? cache))
        {
            cache.MarkDirty();
        }
    }

    /// <summary>
    /// Cleans up cache entries for the given sounds.
    /// </summary>
    protected void CleanupSoundHandlers(IEnumerable<Sound> sounds)
    {
        var entries = new List<KeyValuePair<Sound, AudioNodeCache>>();
        foreach (var sound in sounds)
        {
            if (_audioCache.TryGetValue(sound, out AudioNodeCache? cache))
            {
                _audioCache.Remove(sound);
                entries.Add(new KeyValuePair<Sound, AudioNodeCache>(sound, cache));
            }
        }

        CleanupAudioCaches(entries);
    }

    protected virtual void OnDispose(bool disposing)
    {
        if (disposing)
        {
            KeyValuePair<Sound, AudioNodeCache>[] entries = [.. _audioCache];
            _audioCache.Clear();
            CleanupAudioCaches(entries);
        }
    }

    private static void CleanupAudioCaches(
        IEnumerable<KeyValuePair<Sound, AudioNodeCache>> entries)
    {
        Exception? failure = null;
        foreach ((Sound sound, AudioNodeCache cache) in entries)
        {
            if (cache.EditedHandler != null)
            {
                try
                {
                    sound.Edited -= cache.EditedHandler;
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                }
                finally
                {
                    cache.EditedHandler = null;
                }
            }

            try
            {
                cache.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

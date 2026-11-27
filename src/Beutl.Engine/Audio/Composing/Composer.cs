using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Media;

namespace Beutl.Audio.Composing;

public partial class Composer : IComposer
{
    private readonly AnimationSampler _animationSampler = new();
    private readonly ConditionalWeakTable<Sound, AudioNodeEntry> _audioCache = [];
    private readonly List<AudioNodeEntry> _currentEntry = new();

    // Retain the previous window so contiguous windows can drain complete or partially drained tails.
    private readonly List<AudioNodeEntry> _previousEntry = new();
    private TimeRange? _previousRange;
    private CompositionEligibility? _lastEligibility;
    private int _alignmentLatency;
    private Func<int, Guid>? _audioLayerId;

    private readonly record struct MixInput(AudioBuffer? Buffer, AudioDelayLine? Delay, int TrailingSilence);

    private sealed class AudioNodeEntry : IDisposable
    {
        public List<AudioNode> Nodes { get; set; } = new();
        public DuckingNode[] DuckingNodes { get; set; } = [];
        public AudioNode[]? OutputNodes { get; set; }
        public bool IsDirty { get; set; } = true;
        public int Version { get; set; }
        public EventHandler? EditedHandler { get; set; }
        public TimeRange SoundRange { get; set; }
        public int?[]? FixedLatencies { get; set; }
        public Dictionary<AudioNode, AudioDelayLine>? Delays { get; set; }
        internal Dictionary<AudioNode, TailBudget> TailBudgets { get; } = new(ReferenceEqualityComparer.Instance);
        public required Sound Sound { get; init; }

        public void ClearTail()
        {
            TailBudgets.Clear();
            Delays = null;
        }

        public void Dispose()
        {
            foreach (var node in Nodes)
            {
                node.Dispose();
            }

            Nodes.Clear();
            ClearTail();
        }
    }

    public Composer()
    {
        SampleRate = 44100;
    }

    public int SampleRate { get; init; }

    public AudioChannelLayout ChannelLayout { get; init; } = AudioChannelLayout.Stereo;

    public bool IsDisposed { get; private set; }

    public bool IsAudioRendering { get; private set; }

    // A nested scene's mix still goes through its parent's gain and master stage, so clamping it here would flatten
    // peaks that the parent gain could have brought back under full scale.
    internal bool ClampsToFullScale { get; init; } = true;

    /// <summary>
    /// The gain of the whole mix, applied before the master limiter. 1 leaves the mix as it is.
    /// </summary>
    public float MasterGain { get; set; } = 1f;

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            IsDisposed = true;
        }
    }

    public AudioBuffer? Compose(TimeRange timeRange, CompositionFrame frame)
        => Compose(timeRange, frame, playbackLatency: null);

    internal AudioBuffer? Compose(TimeRange timeRange, CompositionFrame frame, int? playbackLatency)
    {
        if (!IsAudioRendering)
        {
            AudioBuffer? result = null;
            try
            {
                IsAudioRendering = true;
                CompositionEligibility eligibility = frame.Eligibility
                    ?? throw new InvalidOperationException(
                        "Audio composition requires an eligibility snapshot.");

                bool contiguous = _previousRange is { } previous
                    && IsContiguous(previous.End, timeRange.Start);
                if (!contiguous)
                    DiscardRetainedState(_previousEntry);
                _lastEligibility = eligibility;
                _audioLayerId = frame.AudioLayerId;

                _currentEntry.Clear();
                foreach (var resource in frame.Objects)
                {
                    if (resource is Sound.Resource sound)
                        ComposeSound(sound, timeRange);
                }

                _alignmentLatency = playbackLatency ?? GetAlignmentTarget();
                result = BuildFinalOutput(timeRange, playbackLatency.HasValue);
                PromoteEntries(timeRange, contiguous);

                return result;
            }
            catch
            {
                result?.Dispose();
                DiscardRetainedState(GetRetainedEntries());
                throw;
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

    /// <summary>
    /// Reports the largest latency among the output nodes retained by the most recent composition.
    /// </summary>
    public int GetTotalLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        int total = 0;
        foreach (var entry in GetRetainedEntries())
        {
            if (!CanFlushEntry(entry))
                continue;

            if (entry.OutputNodes is not { } outputNodes)
                continue;

            total = Math.Max(total, GetEntryLatency(entry, outputNodes, sampleRate));
        }

        return total;
    }

    /// <summary>
    /// Reports the latency still held by each retained output immediately after its terminal process
    /// call. Unlike <see cref="GetTotalLatencySamples"/>, this uses the output graph's drain-specific
    /// report so terminal animation values can shorten a nested scene's compensation window.
    /// </summary>
    public int GetDrainLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        int total = 0;
        foreach (var entry in GetRetainedEntries())
        {
            if (!CanFlushEntry(entry) || entry.OutputNodes is not { } outputNodes)
                continue;

            foreach (AudioNode outputNode in outputNodes)
            {
                int latency = GetDrainOutputLatency(entry, outputNode, sampleRate);
                total = Math.Max(total, IncludeOutputDelay(entry, outputNode, latency, sampleRate));
            }
        }

        return total;
    }

    /// <summary>
    /// Drains the output nodes retained by the most recent composition at the beginning of
    /// <paramref name="range"/> and mixes the recovered tails into a buffer of that range's length.
    /// </summary>
    public AudioBuffer? Flush(TimeRange range, CompositionEligibility? eligibility = null)
    {
        if (IsAudioRendering)
            return default;

        try
        {
            IsAudioRendering = true;
            var buffers = new List<MixInput>();
            AudioBuffer? mixedBuffer = null;
            try
            {
                _lastEligibility = eligibility ?? CompositionEligibility.Empty;

                var context = new AudioProcessContext(range, SampleRate, _animationSampler, range);
                var entries = GetRetainedEntries();
                bool contiguous = _previousRange is { } previous
                    && IsContiguous(previous.End, range.Start);
                int sampleCount = context.GetSampleCount();

                if (!contiguous)
                {
                    DiscardRetainedState(entries);
                    return new AudioBuffer(SampleRate, ChannelLayout.ChannelCount, sampleCount);
                }

                List<AudioNodeEntry> tails = GetFlushableEntries(entries);
                if (_audioLayerId is { } layerId && entries.Exists(HasDucking))
                {
                    ProcessDucking(context, layerId, [], tails, buffers, isPlayback: false);
                }
                else
                {
                    foreach (var entry in tails)
                        DrainEntryTails(entry, entry.OutputNodes!, context, sampleCount, buffers);
                }

                mixedBuffer = MixBuffers(buffers, sampleCount);
                ApplyMasterEffects(mixedBuffer);

                _currentEntry.Clear();
                _previousEntry.Clear();
                foreach (var entry in entries)
                {
                    if (entry.OutputNodes is { } outputNodes
                        && GetEntryLatency(entry, outputNodes, SampleRate) > 0
                        && CanFlushEntry(entry))
                        _previousEntry.Add(entry);
                    else
                        entry.ClearTail();
                }

                _previousRange = _previousEntry.Count > 0 ? range : null;
                return mixedBuffer;
            }
            catch
            {
                mixedBuffer?.Dispose();
                DiscardRetainedState(GetRetainedEntries());
                throw;
            }
            finally
            {
                foreach (var input in buffers)
                {
                    input.Buffer?.Dispose();
                }
            }
        }
        finally
        {
            IsAudioRendering = false;
        }
    }

    private AudioBuffer BuildFinalOutput(TimeRange range, bool isPlayback)
    {
        var buffers = new List<MixInput>();
        var processContext = new AudioProcessContext(range, SampleRate, _animationSampler, range);
        int sampleCount = processContext.GetSampleCount();
        AudioBuffer? mixedBuffer = null;
        try
        {
            List<AudioNodeEntry> tails = GetEndedSoundTails();
            if (_audioLayerId is { } layerId && (_currentEntry.Exists(HasDucking) || tails.Exists(HasDucking)))
            {
                ProcessDucking(processContext, layerId, _currentEntry, tails, buffers, isPlayback);
            }
            else
            {
                foreach (var item in _currentEntry)
                    ProcessEntry(item, processContext, isPlayback, buffers);

                foreach (var entry in tails)
                    DrainEntryTails(entry, entry.OutputNodes!, processContext, sampleCount, buffers);
            }

            mixedBuffer = MixBuffers(buffers, sampleCount);
            if (buffers.Count > 0)
                ApplyMasterEffects(mixedBuffer);
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
            foreach (var input in buffers)
            {
                input.Buffer?.Dispose();
            }
        }
    }

    private void ProcessEntry(AudioNodeEntry entry, AudioProcessContext context, bool isPlayback, List<MixInput> buffers)
    {
        if (entry.OutputNodes is not { } outputNodes) return;
        var latencies = _alignmentLatency > 0 ? GetFixedLatencies(entry) : null;
        for (int i = 0; i < outputNodes.Length; i++)
        {
            AudioNode outputNode = outputNodes[i];
            AudioDelayLine? delay = GetOutputDelay(entry, outputNode, latencies?[i], isPlayback);
            AudioBuffer buffer = outputNode.Process(context);
            int trailingSilence = outputNode is ClipNode { InlineDrainLatencySamples: int latency } clip
                ? Math.Max(0, clip.InlinePaddingSamples - latency)
                : 0;
            buffers.Add(new MixInput(buffer, delay, trailingSilence));
        }

        RecordInlineDrainBudget(entry, outputNodes);
    }

    private List<AudioNodeEntry> GetFlushableEntries(List<AudioNodeEntry> entries)
    {
        var result = new List<AudioNodeEntry>();
        foreach (var entry in entries)
        {
            if (!CanFlushEntry(entry))
                entry.ClearTail();
            else if (entry.OutputNodes is not null)
                result.Add(entry);
        }
        return result;
    }

    // Residual latency tails are heard at the front of the next contiguous window.
    private List<AudioNodeEntry> GetEndedSoundTails()
    {
        // Flush only across contiguous windows; seeks and restarts invalidate cached tail timing.
        if (_previousRange is not { } previous)
            return [];

        return _previousEntry.FindAll(entry => entry.OutputNodes is not null && CanDrainEndedEntry(entry, previous.End));
    }

    private bool CanDrainEndedEntry(AudioNodeEntry entry, MediaTime previousEnd)
        => !_currentEntry.Contains(entry) && CanFlushEntry(entry)
            && entry.SoundRange.End - previousEnd <= AudioProcessContext.TimestampQuantizationTolerance;

    private void PromoteEntries(TimeRange timeRange, bool contiguous)
    {
        var previous = _previousEntry.ToArray();
        _previousEntry.Clear();

        foreach (var entry in _currentEntry)
        {
            _previousEntry.Add(entry);
        }

        foreach (var entry in previous)
        {
            if (_currentEntry.Contains(entry))
                continue;

            if (contiguous
                && CanFlushEntry(entry)
                && entry.OutputNodes is { } outputNodes
                && GetEntryLatency(entry, outputNodes, SampleRate) > 0)
            {
                _previousEntry.Add(entry);
            }
            else
                entry.ClearTail();
        }

        _previousRange = timeRange;
    }

    private List<AudioNodeEntry> GetRetainedEntries()
    {
        var entries = new List<AudioNodeEntry>(_currentEntry.Count + _previousEntry.Count);
        entries.AddRange(_currentEntry);
        foreach (var entry in _previousEntry)
        {
            if (!entries.Contains(entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    internal static bool IsContiguous(MediaTime previousEnd, MediaTime nextStart)
    {
        MediaTime difference = nextStart - previousEnd;
        return difference >= -AudioProcessContext.TimestampQuantizationTolerance
               && difference <= AudioProcessContext.TimestampQuantizationTolerance;
    }

    private bool CanFlushEntry(AudioNodeEntry entry)
    {
        if (entry.IsDirty || !entry.Sound.IsEnabled.Value || entry.Sound.TimeRange.Value != entry.SoundRange)
            return false;

        return _lastEligibility is not { } eligibility || eligibility.Contains(entry.Sound);
    }

    private void DiscardRetainedState(IEnumerable<AudioNodeEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.ClearTail();
        }

        ResetWindowState();
    }

    private void ResetWindowState()
    {
        _currentEntry.Clear();
        _previousEntry.Clear();
        _previousRange = null;
        _lastEligibility = null;
        _alignmentLatency = 0;
        _audioLayerId = null;
    }

    private AudioBuffer MixBuffers(List<MixInput> buffers, int windowSamples)
    {
        AudioBuffer? firstBuffer = null;
        foreach (var input in buffers)
        {
            if (input.Buffer is not { } buffer)
                continue;
            if (buffer.ChannelCount != ChannelLayout.ChannelCount)
                throw new InvalidOperationException("Audio buffer channel count does not match the composer layout.");
            firstBuffer ??= buffer;
        }
        var mixedBuffer = new AudioBuffer(firstBuffer?.SampleRate ?? SampleRate, ChannelLayout.ChannelCount,
            firstBuffer?.SampleCount ?? windowSamples);

        // Dispose the mix buffer rather than leak it if a (possibly disposed) source read throws.
        try
        {
            foreach (var (buffer, delay, trailingSilence) in buffers)
            {
                int sampleCount = Math.Min(mixedBuffer.SampleCount, buffer?.SampleCount ?? 0);
                for (int ch = 0; ch < mixedBuffer.ChannelCount; ch++)
                {
                    Span<float> mixedChannel = mixedBuffer.GetChannelData(ch);
                    ReadOnlySpan<float> sourceChannel = buffer != null ? buffer.GetChannelData(ch)[..sampleCount] : default;
                    if (delay != null)
                    {
                        delay.MixChannel(ch, sourceChannel, mixedChannel, 1f);
                        continue;
                    }

                    for (int i = 0; i < sampleCount; i++)
                    {
                        mixedChannel[i] += sourceChannel[i];
                    }
                }

                if (delay != null)
                {
                    int liveSamples = Math.Min(sampleCount, (buffer?.SampleCount ?? 0) - trailingSilence);
                    delay.Advance(mixedBuffer.SampleCount, mixedBuffer.SampleCount - liveSamples);
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

    private void ApplyMasterEffects(AudioBuffer buffer)
    {
        float gain = MasterGain;
        // Apply master limiter to prevent clipping
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            var channelData = buffer.GetChannelData(ch);
            if (gain != 1f)
            {
                foreach (ref float sample in channelData)
                    sample *= gain;
            }

            AudioMath.ApplyLimiter(channelData, 1.0f, 10.0f);
            // The limiter's soft ratio still lets a loud mix, such as a large gain or a delay fed back at
            // 100%, far past full scale, and it lets NaN through.
            if (ClampsToFullScale)
                AudioMath.ClampToFullScale(channelData);
        }
    }

    /// <summary>
    /// Invalidates the cache, forcing recreation on next use.
    /// </summary>
    public void InvalidateCache()
    {
        foreach (var kvp in _audioCache)
        {
            if (kvp.Value.EditedHandler is { } handler)
                kvp.Key.Edited -= handler;
            kvp.Value.Dispose();
        }

        _audioCache.Clear();

        // Invalidation also clears previous-window state so disposed graphs cannot be flushed.
        ResetWindowState();
    }

    /// <summary>
    /// Composes a sound with caching support and differential updates.
    /// </summary>
    protected void ComposeSound(Sound.Resource resource, TimeRange timeRange)
    {
        var entry = GetOrUpdateEntry(resource);
        entry.SoundRange = entry.Sound.TimeRange.Value;
        entry.TailBudgets.Clear();
        _currentEntry.Add(entry);
    }

    private AudioNodeEntry GetOrUpdateEntry(Sound.Resource resource)
    {
        Sound sound = resource.RequireOriginal();
        if (!_audioCache.TryGetValue(sound, out var entry))
        {
            entry = new AudioNodeEntry { Sound = sound };
            _audioCache.AddOrUpdate(sound, entry);

            var handler = new EventHandler((s, e) => OnSoundEdited(sound, e));
            sound.Edited += handler;
            entry.EditedHandler = handler;
        }

        // Adding children on the Resource side raises no Edited, so compare the Version too.
        if (entry.IsDirty || entry.Version != resource.Version)
        {
            // AudioContextはDisposeしない。AudioNodeが解放されてしまうので
            var context = new AudioContext(SampleRate, ChannelLayout);

            context.BeginUpdate(entry.Nodes);
            sound.Compose(context, resource);
            AudioNode[] outputNodes = context.GetOutputNodes().ToArray();
            entry.OutputNodes = outputNodes;
            context.EndUpdate();

            entry.Nodes.Clear();
            entry.Nodes.AddRange(context.Nodes);
            entry.DuckingNodes = [.. entry.Nodes.OfType<DuckingNode>()];

            entry.FixedLatencies = null;
            if (entry.IsDirty)
            {
                entry.ClearTail();
            }
            else
            {
                entry.TailBudgets.Clear();
                // Reused outputs still own their queued audio when the flow inputs change.
                if (entry.Delays is { } delays)
                {
                    foreach (AudioNode output in delays.Keys.ToArray())
                    {
                        if (!outputNodes.Contains(output, ReferenceEqualityComparer.Instance))
                            delays.Remove(output);
                    }
                }
            }
            entry.Version = resource.Version;
            entry.IsDirty = false;
        }

        return entry;
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
            InvalidateCache();
        }
    }
}

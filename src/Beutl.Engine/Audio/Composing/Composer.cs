using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Media;

namespace Beutl.Audio.Composing;

public class Composer : IComposer
{
    private readonly AnimationSampler _animationSampler = new();
    private readonly ConditionalWeakTable<Sound, AudioNodeEntry> _audioCache = [];
    private readonly List<AudioNodeEntry> _currentEntry = new();

    // Retain the previous window so contiguous windows can drain complete or partially drained tails.
    private readonly List<AudioNodeEntry> _previousEntry = new();
    private TimeRange? _previousRange;
    private CompositionEligibility? _lastEligibility;

    internal sealed class TailBudget
    {
        public int RemainingSamples { get; set; }
        public bool IsKnown { get; set; }
        public bool StopFurtherDrains { get; set; }
        public bool UnknownFollowUpPending { get; set; }
    }

    private sealed class AudioNodeEntry : IDisposable
    {
        public List<AudioNode> Nodes { get; set; } = new();
        public AudioNode[]? OutputNodes { get; set; }
        public bool IsDirty { get; set; } = true;
        public int Version { get; set; }
        public EventHandler? EditedHandler { get; set; }
        public TimeRange SoundRange { get; set; }
        internal Dictionary<AudioNode, TailBudget> TailBudgets { get; } = new(ReferenceEqualityComparer.Instance);
        public required Sound Sound { get; init; }

        public void Dispose()
        {
            foreach (var node in Nodes)
            {
                node.Dispose();
            }

            Nodes.Clear();
            TailBudgets.Clear();
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
                CompositionEligibility eligibility = frame.Eligibility
                    ?? throw new InvalidOperationException(
                        "Audio composition requires an eligibility snapshot.");
                _lastEligibility = eligibility;

                _currentEntry.Clear();
                foreach (var resource in frame.Objects)
                {
                    if (resource is Sound.Resource sound)
                        ComposeSound(sound, timeRange);
                }

                // Build final audio graph
                var result = BuildFinalOutput(timeRange, eligibility);

                bool contiguous = _previousRange is { } previous
                    && IsContiguous(previous.End, timeRange.Start);
                PromoteEntries(timeRange, contiguous);

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
                total = Math.Max(total, GetDrainOutputLatency(entry, outputNode, sampleRate));
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
            var buffers = new List<AudioBuffer>();
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
                    return new AudioBuffer(SampleRate, 2, sampleCount);
                }

                foreach (var entry in entries)
                {
                    if (!CanFlushEntry(entry))
                    {
                        entry.TailBudgets.Clear();
                        continue;
                    }

                    if (entry.OutputNodes is not { } outputNodes)
                        continue;

                    int latency = GetEntryLatency(entry, outputNodes, SampleRate);
                    if (latency <= 0)
                        continue;

                    foreach (AudioNode outputNode in outputNodes)
                    {
                        int outputLatency = GetOutputLatency(entry, outputNode, SampleRate);
                        if (outputLatency <= 0)
                            continue;

                        buffers.Add(FlushTail(outputNode, context, outputLatency, sampleCount, out int drainedSamples));
                        RecordTailAfterDrain(entry, outputNode, outputLatency, drainedSamples);
                    }
                }

                mixedBuffer = MixBuffers(buffers)
                    ?? new AudioBuffer(SampleRate, 2, AudioProcessContext.GetSampleCount(range, SampleRate));
                ApplyMasterEffects(mixedBuffer);

                _currentEntry.Clear();
                _previousEntry.Clear();
                foreach (var entry in entries)
                {
                    if (entry.OutputNodes is { } outputNodes
                        && GetEntryLatency(entry, outputNodes, SampleRate) > 0
                        && CanFlushEntry(entry))
                        _previousEntry.Add(entry);
                }

                _previousRange = _previousEntry.Count > 0 ? range : null;
                return mixedBuffer;
            }
            catch
            {
                mixedBuffer?.Dispose();
                throw;
            }
            finally
            {
                foreach (var buffer in buffers)
                {
                    buffer.Dispose();
                }
            }
        }
        finally
        {
            IsAudioRendering = false;
        }
    }

    private AudioBuffer? BuildFinalOutput(TimeRange range, CompositionEligibility eligibility)
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

                RecordInlineDrainBudget(item, outputNodes);
            }

            AppendEndedSoundTails(range, eligibility, buffers);

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

    // Drain residual latency tails into a window-length buffer with the tail at the front.
    private void AppendEndedSoundTails(
        TimeRange range,
        CompositionEligibility eligibility,
        List<AudioBuffer> buffers)
    {
        // Flush only across contiguous windows; seeks and restarts invalidate cached tail timing.
        if (_previousRange is not { } previous || !IsContiguous(previous.End, range.Start))
            return;

        foreach (var entry in _previousEntry)
        {
            if (_currentEntry.Contains(entry))
                continue;
            if (entry.IsDirty)
                continue;
            if (entry.OutputNodes is not { } outputNodes)
                continue;
            // Entries ending at or before the previous boundary may still hold a partially drained tail.
            if (entry.SoundRange.End.Ticks - previous.End.Ticks > 1)
                continue;
            if (entry.Sound.TimeRange != entry.SoundRange)
                continue;
            if (!eligibility.Contains(entry.Sound))
                continue;

            if (GetEntryLatency(entry, outputNodes, SampleRate) <= 0)
                continue;

            var flushContext = new AudioProcessContext(range, SampleRate, _animationSampler, range);
            int sampleCount = flushContext.GetSampleCount();
            foreach (var outputNode in outputNodes)
            {
                int outputLatency = GetOutputLatency(entry, outputNode, SampleRate);
                if (outputLatency <= 0)
                    continue;

                buffers.Add(FlushTail(outputNode, flushContext, outputLatency, sampleCount, out int drainedSamples));
                RecordTailAfterDrain(entry, outputNode, outputLatency, drainedSamples);
            }
        }
    }

    private AudioBuffer FlushTail(
        AudioNode outputNode,
        AudioProcessContext context,
        int outputLatency,
        int sampleCount,
        out int drainedSamples)
    {
        drainedSamples = Math.Min(outputLatency, sampleCount);
        AudioProcessContext drainContext = drainedSamples == sampleCount
            ? context
            : new AudioProcessContext(
                new TimeRange(
                    context.TimeRange.Start,
                    AudioProcessContext.GetDurationForSampleCount(drainedSamples, SampleRate)),
                SampleRate,
                context.AnimationSampler,
                context.OriginalTimeRange);

        using AudioBuffer drained = outputNode.Flush(drainContext);
        var output = new AudioBuffer(drained.SampleRate, drained.ChannelCount, sampleCount);
        try
        {
            int copyCount = Math.Min(drained.SampleCount, sampleCount);
            if (copyCount > 0)
            {
                drained.CopyTo(output, 0, copyCount);
            }

            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private void RecordInlineDrainBudget(AudioNodeEntry entry, AudioNode[] outputNodes)
    {
        bool hasInlineDrain = outputNodes.Any(
            outputNode => TryGetInlineDrain(outputNode, SampleRate, out _));
        if (!hasInlineDrain)
            return;

        foreach (AudioNode outputNode in outputNodes)
        {
            int outputLatency = outputNode.GetDrainLatencySamples(SampleRate);
            if (outputLatency < 0)
            {
                throw new InvalidOperationException(
                    $"{outputNode.GetType().Name} returned negative total latency {outputLatency}.");
            }

            bool inlineDrainAttempted = TryGetInlineDrain(
                outputNode,
                SampleRate,
                out int inlineDrain,
                outputLatency);
            if (!inlineDrainAttempted)
                continue;

            if (outputLatency == int.MaxValue)
                inlineDrain = 0;
            else
                inlineDrain = Math.Min(outputLatency, inlineDrain);
            SetTailBudget(
                entry,
                outputNode,
                outputLatency,
                inlineDrain,
                allowUnknownFollowUp: inlineDrainAttempted);
        }
    }

    private static bool TryGetInlineDrain(
        AudioNode outputNode,
        int sampleRate,
        out int inlineDrain,
        int outputLatency = 0)
    {
        var branches = new List<InlineDrainBranch>();
        CollectInlineDrainBranches(
            outputNode,
            sampleRate,
            branches,
            new HashSet<AudioNode>(ReferenceEqualityComparer.Instance));
        if (branches.Count == 0)
        {
            inlineDrain = 0;
            return false;
        }

        int remainingLatency = 0;
        foreach (InlineDrainBranch branch in branches)
        {
            int upstreamRemaining = SubtractTail(branch.LatencySamples, branch.DrainedSamples);
            int downstreamRemaining = SubtractTail(branch.DownstreamLatencySamples, branch.PaddingSamples);
            int branchRemaining = AddLatency(upstreamRemaining, downstreamRemaining);
            if (branchRemaining == int.MaxValue)
            {
                remainingLatency = int.MaxValue;
                inlineDrain = 0;
                break;
            }

            remainingLatency = Math.Max(
                remainingLatency,
                ScaleByFactor(branchRemaining, branch.OutputScale));
        }

        inlineDrain = remainingLatency == int.MaxValue
            ? 0
            : Math.Max(0, outputLatency - remainingLatency);
        return true;
    }

    private sealed record InlineDrainBranch(
        int LatencySamples,
        int DrainedSamples,
        int PaddingSamples,
        int DownstreamLatencySamples,
        double OutputScale);

    private static void CollectInlineDrainBranches(
        AudioNode node,
        int sampleRate,
        List<InlineDrainBranch> branches,
        HashSet<AudioNode> visited,
        int downstreamLatency = 0,
        double outputScale = 1d)
    {
        if (!visited.Add(node))
            return;

        try
        {
            if (node is ClipNode { InlineDrainAttempted: true } clipNode)
            {
                int latency = clipNode.GetDrainLatencySamples(sampleRate);
                if (latency < 0)
                {
                    throw new InvalidOperationException(
                        $"{clipNode.GetType().Name} returned negative drain latency {latency}.");
                }

                branches.Add(new InlineDrainBranch(
                    latency,
                    clipNode.InlineDrainedSamples,
                    clipNode.InlinePaddingSamples,
                    downstreamLatency,
                    outputScale));
                return;
            }

            int ownLatency = node.GetLatencySamples(sampleRate);
            if (ownLatency < 0)
            {
                throw new InvalidOperationException(
                    $"{node.GetType().Name} returned negative latency {ownLatency}.");
            }

            int nextDownstreamLatency = AddLatency(downstreamLatency, ownLatency);
            int nextSampleRate = sampleRate;
            double nextOutputScale = outputScale;
            if (node is ResampleNode resampleNode)
            {
                nextDownstreamLatency = ScaleSampleCount(
                    nextDownstreamLatency,
                    sampleRate,
                    resampleNode.SourceSampleRate);
                nextSampleRate = resampleNode.SourceSampleRate;
                nextOutputScale = MultiplyScale(
                    outputScale,
                    sampleRate / (double)resampleNode.SourceSampleRate);
            }
            else if (node is SpeedNode speedNode)
            {
                if (speedNode.TryGetDrainSpeedFactor(sampleRate, out double drainSpeed))
                {
                    nextDownstreamLatency = ScaleByFactor(nextDownstreamLatency, drainSpeed);
                    nextOutputScale = MultiplyScale(outputScale, 1d / drainSpeed);
                }
                else
                {
                    nextDownstreamLatency = int.MaxValue;
                    nextOutputScale = double.PositiveInfinity;
                }
            }

            foreach (AudioNode input in node.Inputs)
                CollectInlineDrainBranches(
                    input,
                    nextSampleRate,
                    branches,
                    visited,
                    nextDownstreamLatency,
                    nextOutputScale);
        }
        finally
        {
            // The same descendant can feed multiple fan-in paths. Keep cycle detection local to the
            // current recursion path so each distinct path contributes its downstream latency.
            visited.Remove(node);
        }
    }

    private static int AddLatency(int first, int second)
    {
        if (first == int.MaxValue || second == int.MaxValue)
            return int.MaxValue;

        long sum = (long)first + second;
        return sum >= int.MaxValue ? int.MaxValue : (int)sum;
    }

    private static int ScaleByFactor(int sampleCount, double factor)
    {
        if (sampleCount == int.MaxValue)
            return int.MaxValue;
        if (sampleCount == 0)
            return 0;
        if (!double.IsFinite(factor) || factor <= 0)
            return int.MaxValue;

        double scaled = sampleCount * factor;
        return !double.IsFinite(scaled) || scaled >= int.MaxValue
            ? int.MaxValue
            : (int)Math.Ceiling(scaled);
    }

    private static double MultiplyScale(double first, double second)
    {
        double product = first * second;
        return double.IsFinite(product) && product > 0
            ? product
            : double.PositiveInfinity;
    }

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
            if (contiguous
                && !_currentEntry.Contains(entry)
                && entry.OutputNodes is { } outputNodes
                && GetEntryLatency(entry, outputNodes, SampleRate) > 0)
            {
                if (CanFlushEntry(entry))
                {
                    _previousEntry.Add(entry);
                }
                else
                {
                    entry.TailBudgets.Clear();
                }
            }
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

    private int GetEntryLatency(AudioNodeEntry entry, AudioNode[] outputNodes, int sampleRate)
    {
        int latency = 0;
        foreach (AudioNode outputNode in outputNodes)
        {
            latency = Math.Max(latency, GetOutputLatency(entry, outputNode, sampleRate));
        }

        return latency;
    }

    private int GetOutputLatency(AudioNodeEntry entry, AudioNode outputNode, int sampleRate)
    {
        if (entry.TailBudgets.TryGetValue(outputNode, out var budget))
        {
            if (budget.StopFurtherDrains)
                return 0;
            if (budget.UnknownFollowUpPending)
                return int.MaxValue;
            if (budget.IsKnown)
                return ScaleSampleCount(budget.RemainingSamples, SampleRate, sampleRate);
        }

        int latency = outputNode.GetTotalLatencySamples(sampleRate);
        if (latency < 0)
        {
            throw new InvalidOperationException(
                $"{outputNode.GetType().Name} returned negative total latency {latency}.");
        }

        return latency;
    }

    private int GetDrainOutputLatency(AudioNodeEntry entry, AudioNode outputNode, int sampleRate)
    {
        if (entry.TailBudgets.TryGetValue(outputNode, out var budget))
        {
            if (budget.StopFurtherDrains)
                return 0;
            if (budget.UnknownFollowUpPending)
                return int.MaxValue;
            if (budget.IsKnown)
                return ScaleSampleCount(budget.RemainingSamples, SampleRate, sampleRate);
        }

        int latency = outputNode.GetDrainLatencySamples(sampleRate);
        if (latency < 0)
        {
            throw new InvalidOperationException(
                $"{outputNode.GetType().Name} returned negative drain latency {latency}.");
        }

        return latency;
    }

    private static void RecordTailAfterDrain(
        AudioNodeEntry entry,
        AudioNode outputNode,
        int outputLatency,
        int drainedSamples)
    {
        SetTailBudget(entry, outputNode, outputLatency, drainedSamples);
    }

    private static void SetTailBudget(
        AudioNodeEntry entry,
        AudioNode outputNode,
        int latency,
        int drainedSamples,
        bool allowUnknownFollowUp = false)
    {
        var budget = entry.TailBudgets.TryGetValue(outputNode, out var existing)
            ? existing
            : new TailBudget();
        budget.IsKnown = true;
        if (latency == int.MaxValue)
        {
            bool preserveUnknownFollowUp = allowUnknownFollowUp
                || (drainedSamples == 0 && budget.UnknownFollowUpPending);
            budget.RemainingSamples = 0;
            budget.UnknownFollowUpPending = preserveUnknownFollowUp;
            budget.StopFurtherDrains = !preserveUnknownFollowUp;
        }
        else
        {
            budget.RemainingSamples = SubtractTail(latency, drainedSamples);
            budget.UnknownFollowUpPending = false;
            budget.StopFurtherDrains = false;
        }

        entry.TailBudgets[outputNode] = budget;
    }

    private static int ScaleSampleCount(int sampleCount, int sourceSampleRate, int destinationSampleRate)
    {
        if (sampleCount == int.MaxValue || sourceSampleRate == destinationSampleRate)
            return sampleCount;

        double scaled = sampleCount * (double)destinationSampleRate / sourceSampleRate;
        return scaled >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(scaled);
    }

    private static int SubtractTail(int latency, int samples)
    {
        if (latency == int.MaxValue)
            return int.MaxValue;

        return Math.Max(0, latency - samples);
    }

    private static bool IsContiguous(TimeSpan previousEnd, TimeSpan nextStart)
        => Math.Abs((nextStart - previousEnd).Ticks)
            <= AudioProcessContext.TimestampQuantizationToleranceTicks;

    private bool CanFlushEntry(AudioNodeEntry entry)
    {
        if (entry.IsDirty || !entry.Sound.IsEnabled || entry.Sound.TimeRange != entry.SoundRange)
            return false;

        return _lastEligibility is not { } eligibility || eligibility.Contains(entry.Sound);
    }

    private void DiscardRetainedState(IEnumerable<AudioNodeEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.TailBudgets.Clear();
        }

        _currentEntry.Clear();
        _previousEntry.Clear();
        _previousRange = null;
        _lastEligibility = null;
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
        foreach (var kvp in _audioCache)
        {
            kvp.Value.Dispose();
        }

        _audioCache.Clear();

        // Invalidation also clears previous-window state so disposed graphs cannot be flushed.
        _currentEntry.Clear();
        _previousEntry.Clear();
        _previousRange = null;
        _lastEligibility = null;
    }

    /// <summary>
    /// Composes a sound with caching support and differential updates.
    /// </summary>
    protected void ComposeSound(Sound.Resource resource, TimeRange timeRange)
    {
        var sound = resource.RequireOriginal();
        // Get or create cache entry
        if (!_audioCache.TryGetValue(sound, out var entry))
        {
            entry = new AudioNodeEntry { Sound = sound };
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

        entry.SoundRange = sound.TimeRange;
        entry.TailBudgets.Clear();
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

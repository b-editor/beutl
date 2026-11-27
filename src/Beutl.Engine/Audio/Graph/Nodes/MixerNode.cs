using System;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed partial class MixerNode : AudioNode
{
    private float[] _gains = Array.Empty<float>();
    private readonly Dictionary<AudioNode, MediaTime> _branchEndTimes =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AudioNode> _processedBranches =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AudioNode> _unknownDrainAttempts =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AudioNode, BranchTailBudget> _branchTailBudgets =
        new(ReferenceEqualityComparer.Instance);
    private MediaTime? _lastTimeRangeEnd;

    private sealed class BranchTailBudget
    {
        public int RemainingSamples { get; set; }
        public int SampleRate { get; set; }
    }

    public float[] Gains
    {
        get => _gains;
        set => _gains = value ?? Array.Empty<float>();
    }

    /// <summary>
    /// Records the group-local time when a connected input branch ends. A branch without an end time is
    /// considered live, so dynamically added inputs keep draining until explicitly configured.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="input"/> is not connected to this mixer.</exception>
    public void SetBranchEndTime(AudioNode input, MediaTime endTime)
    {
        EnsureConnectedInput(input);

        _branchEndTimes[input] = endTime;
        _processedBranches.Remove(input);
        _unknownDrainAttempts.Remove(input);
        _branchTailBudgets.Remove(input);
        _branchDelays?.Remove(input);
    }

    /// <summary>
    /// Removes the recorded end time for a connected input so the branch is considered live again.
    /// </summary>
    /// <returns><see langword="true"/> when an end time was removed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="input"/> is not connected to this mixer.</exception>
    public bool ClearBranchEndTime(AudioNode input)
    {
        EnsureConnectedInput(input);
        _branchTailBudgets.Remove(input);
        _unknownDrainAttempts.Remove(input);
        return _branchEndTimes.Remove(input);
    }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_lastTimeRangeEnd is { } previousEnd && !context.ContinuesFrom(previousEnd))
        {
            _processedBranches.Clear();
            _unknownDrainAttempts.Clear();
            _branchTailBudgets.Clear();
        }

        AudioBuffer result = Mix(context, drain: false);
        _lastTimeRangeEnd = context.TimeRange.End;
        return result;
    }

    // Fan-in flush drains and mixes live branch tails using the normal gain fold.
    public override AudioBuffer Flush(AudioProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Inputs.Count == 0)
            return CreateSilentFlush(context);

        return Mix(context, drain: true);
    }

    public override int GetDrainLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        int total = 0;
        foreach (AudioNode input in Inputs)
        {
            int branchLatency = GetBranchDrainLatencySamples(input, sampleRate);
            if (_branchDelays?.TryGetValue(input, out AudioDelayLine? delay) == true)
            {
                int compensation = branchLatency > 0 ? delay.DelaySamples : delay.PendingSamples;
                branchLatency = AudioLatency.SaturatingAdd(branchLatency,
                    AudioLatency.ScaleSampleCount(compensation, delay.SampleRate, sampleRate));
            }

            total = Math.Max(total, branchLatency);
        }

        return total;
    }

    private int GetBranchDrainLatencySamples(AudioNode input, int sampleRate)
    {
        if (_unknownDrainAttempts.Contains(input))
            return 0;

        if (_branchTailBudgets.TryGetValue(input, out BranchTailBudget? budget))
            return AudioLatency.ScaleSampleCount(budget.RemainingSamples, budget.SampleRate, sampleRate);

        // A branch that was already over when this run began has no retained audio from this run.
        if (_branchEndTimes.ContainsKey(input) && !_processedBranches.Contains(input))
            return 0;

        int latency = input.GetDrainLatencySamples(sampleRate);
        AudioLatency.ThrowIfNegative(input, latency, "drain latency");
        if (latency != int.MaxValue
            && _branchEndTimes.TryGetValue(input, out MediaTime end)
            && _lastTimeRangeEnd is { } processedEnd
            && GetTailEnd(end, latency, sampleRate) <= processedEnd)
        {
            return 0;
        }

        return latency;
    }

    private AudioBuffer Mix(AudioProcessContext context, bool drain)
    {
        if (Inputs.Count == 0)
            throw new InvalidOperationException("Mixer requires at least one input.");

        int targetLatency = GetCompensationTarget(context.SampleRate);
        if (targetLatency != _delayTargetLatency
            || (_lastDelayRangeEnd is { } delayEnd && !context.ContinuesFrom(delayEnd)))
        {
            _branchDelays?.Clear();
        }
        _delayTargetLatency = targetLatency;

        scoped Span<BranchAlignment> alignment = default;
        if (targetLatency > 0)
            alignment = Inputs.Count <= 64 ? stackalloc BranchAlignment[Inputs.Count] : new BranchAlignment[Inputs.Count];

        var buffers = new AudioBuffer?[Inputs.Count];
        try
        {
            for (int i = 0; i < Inputs.Count; i++)
            {
                AudioNode branch = Inputs[i];
                int? fixedLatency = targetLatency > 0 ? branch.GetFixedLatencySamples(context.SampleRate) : null;
                buffers[i] = ReadBranch(branch, context, drain, fixedLatency, out int trailingSilence);
                if (!alignment.IsEmpty)
                {
                    alignment[i] = new BranchAlignment(
                        fixedLatency is { } latency ? targetLatency - latency : 0, trailingSilence);
                }
            }

            AudioBuffer? firstBuffer = null;
            for (int i = 0; i < buffers.Length; i++)
            {
                var buffer = buffers[i];
                if (buffer == null)
                    continue;

                if (firstBuffer == null)
                {
                    firstBuffer = buffer;
                    continue;
                }

                if (buffer.SampleRate != firstBuffer.SampleRate)
                    throw new InvalidOperationException($"All inputs must have the same sample rate. Expected {firstBuffer.SampleRate}, but input {i} has {buffer.SampleRate}.");
                if (buffer.ChannelCount != firstBuffer.ChannelCount)
                    throw new InvalidOperationException($"All inputs must have the same channel count. Expected {firstBuffer.ChannelCount}, but input {i} has {buffer.ChannelCount}.");
                if (buffer.SampleCount != firstBuffer.SampleCount)
                    throw new InvalidOperationException($"All inputs must have the same sample count. Expected {firstBuffer.SampleCount}, but input {i} has {buffer.SampleCount}.");
            }

            if (firstBuffer == null && _branchDelays is not { Count: > 0 })
                return CreateSilentFlush(context);

            var output = firstBuffer == null
                ? CreateSilentFlush(context)
                : new AudioBuffer(firstBuffer.SampleRate, firstBuffer.ChannelCount, firstBuffer.SampleCount);
            try
            {
                for (int i = 0; i < buffers.Length; i++)
                {
                    var inBuffer = buffers[i];
                    AudioDelayLine? delay = alignment.IsEmpty ? null : GetBranchDelay(
                        Inputs[i], alignment[i].DelaySamples, output, inBuffer != null);
                    if (inBuffer == null && delay is not { PendingSamples: > 0 })
                        continue;

                    float gain = i < _gains.Length ? _gains[i] : 1.0f;
                    for (int ch = 0; ch < output.ChannelCount; ch++)
                    {
                        var outData = output.GetChannelData(ch);
                        ReadOnlySpan<float> inData = inBuffer == null ? default : inBuffer.GetChannelData(ch);
                        if (delay != null)
                        {
                            delay.MixChannel(ch, inData, outData, gain);
                            continue;
                        }

                        for (int s = 0; s < output.SampleCount; s++)
                        {
                            outData[s] += inData[s] * gain;
                        }
                    }

                    if (delay != null)
                        delay.Advance(output.SampleCount, inBuffer == null ? output.SampleCount : alignment[i].TrailingSilence);
                }

                if (firstBuffer != null)
                    RecordProcessedChannelCount(output.ChannelCount);
                if (output.SampleCount > 0 && _branchDelays is { Count: > 0 })
                    _lastDelayRangeEnd = context.TimeRange.End;
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
        catch
        {
            _branchDelays?.Clear();
            throw;
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }

    private AudioBuffer? ReadBranch(AudioNode branch, AudioProcessContext context, bool drain, int? fixedLatency,
        out int trailingSilence)
    {
        trailingSilence = 0;
        bool hasEnd = _branchEndTimes.TryGetValue(branch, out MediaTime branchEndTime);
        bool branchEnded = hasEnd && context.TimeRange.Start >= branchEndTime;
        bool processed = _processedBranches.Contains(branch);
        if (drain || (branchEnded && processed))
        {
            if (IsBranchDead(branch, context) || (fixedLatency.HasValue && branchEnded && !processed))
                return null;

            int latency = branch.GetDrainLatencySamples(context.SampleRate);
            AudioBuffer? tail = FlushBranch(branch, context, latency, branchEnded ? branchEndTime : null,
                fixedLatency.HasValue, out trailingSilence);
            if (latency == int.MaxValue && context.GetSampleCount() > 0)
                _unknownDrainAttempts.Add(branch);

            return tail;
        }

        AudioBuffer buffer = branch.Process(context);
        try
        {
            if (branchEnded)
            {
                trailingSilence = buffer.SampleCount;
            }
            else
            {
                _processedBranches.Add(branch);
                if (hasEnd && context.TimeRange.End >= branchEndTime)
                    trailingSilence = RecordProcessedTailBudget(branch, branchEndTime, context.TimeRange.End, context.SampleRate, fixedLatency);
                else if (fixedLatency.HasValue)
                    _branchTailBudgets.Remove(branch);
            }

            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private bool IsBranchDead(AudioNode branch, AudioProcessContext context)
    {
        if (_branchTailBudgets.TryGetValue(branch, out BranchTailBudget? budget)
            && budget.SampleRate == context.SampleRate)
        {
            return budget.RemainingSamples <= 0;
        }

        if (!_branchEndTimes.TryGetValue(branch, out MediaTime branchEndTime))
            return false;

        int branchLatency = branch.GetDrainLatencySamples(context.SampleRate);
        if (branchLatency == int.MaxValue)
            return _unknownDrainAttempts.Contains(branch);

        // A branch is dead once its retained tail ends at or just before this block. A one-tick
        // tolerance absorbs the same timestamp quantization used by contiguous audio ranges.
        MediaTime blockStart = context.TimeRange.Start;
        MediaTime tolerance = AudioProcessContext.TimestampQuantizationTolerance;
        MediaTime deadAfter = blockStart > MediaTime.MaxValue - tolerance ? MediaTime.MaxValue : blockStart + tolerance;

        return GetTailEnd(branchEndTime, branchLatency, context.SampleRate) <= deadAfter;
    }

    private AudioBuffer? FlushBranch(AudioNode branch, AudioProcessContext context, int branchLatency,
        MediaTime? branchEnd, bool trackFixedTail, out int trailingSilence)
    {
        trailingSilence = 0;
        if (branchLatency == int.MaxValue || (branchEnd == null && !trackFixedTail))
            return branch.Flush(context);

        int sampleCount = context.GetSampleCount();
        int remainingSamples;
        if (_branchTailBudgets.TryGetValue(branch, out BranchTailBudget? budget)
            && budget.SampleRate == context.SampleRate)
        {
            remainingSamples = budget.RemainingSamples;
        }
        else
        {
            remainingSamples = branchLatency;
            if (branchEnd is { } end)
            {
                MediaTime remaining = GetTailEnd(end, branchLatency, context.SampleRate) - context.TimeRange.Start;
                remainingSamples = remaining <= MediaTime.Zero
                    ? 0
                    : Math.Min(branchLatency,
                        AudioProcessContext.GetSampleCount(new TimeRange(context.TimeRange.Start, remaining), context.SampleRate));
            }

            budget = new BranchTailBudget
            {
                RemainingSamples = remainingSamples,
                SampleRate = context.SampleRate,
            };
            _branchTailBudgets[branch] = budget;
        }

        if (remainingSamples >= sampleCount)
        {
            budget.RemainingSamples = Math.Max(0, remainingSamples - sampleCount);
            return branch.Flush(context);
        }

        if (remainingSamples <= 0)
        {
            trailingSilence = sampleCount;
            return trackFixedTail ? null : CreateSilentFlush(context);
        }

        trailingSilence = sampleCount - remainingSamples;

        // The full-window and empty cases returned above, so FlushIntoWindow always builds a shortened drain
        // context here.
        AudioBuffer output = AudioLatency.FlushIntoWindow(branch, context, remainingSamples, sampleCount);
        budget.RemainingSamples = 0;
        return output;
    }

    private static MediaTime GetTailEnd(MediaTime branchEndTime, int branchLatency, int sampleRate)
    {
        var latency = MediaTime.FromSamples(branchLatency, sampleRate);
        return branchEndTime > MediaTime.MaxValue - latency ? MediaTime.MaxValue : branchEndTime + latency;
    }

    private int RecordProcessedTailBudget(
        AudioNode branch,
        MediaTime branchEndTime,
        MediaTime processedEnd,
        int sampleRate,
        int? fixedLatency)
    {
        int branchLatency = branch.GetDrainLatencySamples(sampleRate);
        AudioLatency.ThrowIfNegative(branch, branchLatency, "drain latency");

        if (branchLatency == int.MaxValue)
            return 0;

        // A clip that wraps another mixer can already report a reduced budget after inline drain.
        // Measure the padding against its fixed latency, and cap the result by what it still holds.
        if (fixedLatency == null && branchLatency > 0 && processedEnd > branchEndTime)
            fixedLatency = branch.GetFixedLatencySamples(sampleRate);

        MediaTime remaining = GetTailEnd(branchEndTime, fixedLatency ?? branchLatency, sampleRate) - processedEnd;
        int remainingSamples = remaining <= MediaTime.Zero
            ? 0
            : (int)Math.Min(branchLatency, remaining.ToSamples(sampleRate, MidpointRounding.ToPositiveInfinity));
        _branchTailBudgets[branch] = new BranchTailBudget
        {
            RemainingSamples = remainingSamples,
            SampleRate = sampleRate,
        };

        return remaining < MediaTime.Zero
            ? (int)Math.Min(int.MaxValue, (-remaining).ToSamples(sampleRate, MidpointRounding.ToZero))
            : 0;
    }

    protected override void OnInputAdded(AudioNode input, int index)
    {
        AppendDefaultIfConfigured(ref _gains, index, 1f);
    }

    protected override void OnInputRemoved(AudioNode input, int index)
    {
        _gains = RemoveAt(_gains, index);
        _branchEndTimes.Remove(input);
        _processedBranches.Remove(input);
        _unknownDrainAttempts.Remove(input);
        _branchTailBudgets.Remove(input);
        _branchDelays?.Remove(input);
        _lastTimeRangeEnd = null;
    }

    protected override void OnInputsCleared()
    {
        ResetBranchState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ResetBranchState();
        }

        base.Dispose(disposing);
    }

    private void ResetBranchState()
    {
        _gains = Array.Empty<float>();
        _branchEndTimes.Clear();
        _processedBranches.Clear();
        _unknownDrainAttempts.Clear();
        _branchTailBudgets.Clear();
        _branchDelays = null;
        _lastDelayRangeEnd = null;
        _delayTargetLatency = 0;
        _lastTimeRangeEnd = null;
    }

    private void EnsureConnectedInput(AudioNode input)
    {
        ArgumentNullException.ThrowIfNull(input);

        foreach (AudioNode connectedInput in Inputs)
        {
            if (ReferenceEquals(connectedInput, input))
                return;
        }

        throw new ArgumentException("The input must be connected before configuring its branch end time.", nameof(input));
    }

    private static void AppendDefaultIfConfigured<T>(ref T[] values, int index, T defaultValue)
    {
        if (values.Length == 0 || values.Length > index)
            return;

        int oldLength = values.Length;
        Array.Resize(ref values, index + 1);
        Array.Fill(values, defaultValue, oldLength, values.Length - oldLength);
    }

    private static T[] RemoveAt<T>(T[] values, int index)
    {
        if (index >= values.Length)
            return values;

        var result = new T[values.Length - 1];
        values.AsSpan(0, index).CopyTo(result);
        values.AsSpan(index + 1).CopyTo(result.AsSpan(index));
        return result;
    }
}

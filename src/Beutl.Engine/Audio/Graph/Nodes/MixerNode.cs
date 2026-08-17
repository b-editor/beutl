using System;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed class MixerNode : AudioNode
{
    private float[] _gains = Array.Empty<float>();
    private readonly Dictionary<AudioNode, TimeSpan> _branchEndTimes =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AudioNode> _processedBranches =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AudioNode> _unknownDrainAttempts =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AudioNode, BranchTailBudget> _branchTailBudgets =
        new(ReferenceEqualityComparer.Instance);
    private TimeSpan? _lastTimeRangeEnd;

    private sealed class BranchTailBudget
    {
        public int RemainingSamples { get; set; }
        public int SampleRate { get; set; }
    }

    private sealed record InputState(
        float[] Gains,
        bool HasBranchEndTime,
        TimeSpan BranchEndTime,
        bool WasProcessed,
        bool UnknownDrainAttempted,
        bool HasTailBudget,
        int RemainingTailSamples,
        int TailBudgetSampleRate,
        TimeSpan? LastTimeRangeEnd);

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
    public void SetBranchEndTime(AudioNode input, TimeSpan endTime)
    {
        EnsureConnectedInput(input);

        _branchEndTimes[input] = endTime;
        _processedBranches.Remove(input);
        _unknownDrainAttempts.Remove(input);
        _branchTailBudgets.Remove(input);
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
            if (_unknownDrainAttempts.Contains(input))
                continue;

            if (_branchTailBudgets.TryGetValue(input, out BranchTailBudget? budget))
            {
                if (budget.RemainingSamples <= 0)
                    continue;

                total = Math.Max(total, ScaleSampleCount(budget.RemainingSamples, budget.SampleRate, sampleRate));
                continue;
            }

            int branchLatency = input.GetDrainLatencySamples(sampleRate);
            if (branchLatency < 0)
            {
                throw new InvalidOperationException(
                    $"{input.GetType().Name} returned negative drain latency {branchLatency}.");
            }

            if (branchLatency != int.MaxValue
                && _branchEndTimes.TryGetValue(input, out TimeSpan branchEndTime)
                && _lastTimeRangeEnd is { } processedEnd
                && GetTailEndTicks(branchEndTime, branchLatency, sampleRate) <= processedEnd.Ticks)
            {
                continue;
            }

            total = Math.Max(total, branchLatency);
        }

        return total;
    }

    private AudioBuffer Mix(AudioProcessContext context, bool drain)
    {
        if (Inputs.Count == 0)
            throw new InvalidOperationException("Mixer requires at least one input.");

        var buffers = new AudioBuffer[Inputs.Count];
        try
        {
            for (int i = 0; i < Inputs.Count; i++)
            {
                bool branchEnded = IsBranchEndedByTime(i, context);
                bool drainBranch = drain || IsBranchEnded(i, context);
                if (drainBranch && IsBranchDead(i, context))
                    continue;

                bool unknownDrain = drainBranch
                    && Inputs[i].GetDrainLatencySamples(context.SampleRate) == int.MaxValue;
                buffers[i] = drainBranch
                    ? FlushBranch(i, context, out _)
                    : Inputs[i].Process(context);
                if (unknownDrain && context.GetSampleCount() > 0)
                    _unknownDrainAttempts.Add(Inputs[i]);
                if (!drainBranch && !branchEnded)
                    _processedBranches.Add(Inputs[i]);
            }

            AudioBuffer? firstBuffer = null;
            foreach (var buffer in buffers)
            {
                if (buffer != null)
                {
                    firstBuffer = buffer;
                    break;
                }
            }

            if (firstBuffer == null)
                return CreateSilentFlush(context);

            for (int i = 0; i < buffers.Length; i++)
            {
                var buffer = buffers[i];
                if (buffer == null)
                    continue;
                if (buffer.SampleRate != firstBuffer.SampleRate)
                    throw new InvalidOperationException($"All inputs must have the same sample rate. Expected {firstBuffer.SampleRate}, but input {i} has {buffer.SampleRate}.");
                if (buffer.ChannelCount != firstBuffer.ChannelCount)
                    throw new InvalidOperationException($"All inputs must have the same channel count. Expected {firstBuffer.ChannelCount}, but input {i} has {buffer.ChannelCount}.");
                if (buffer.SampleCount != firstBuffer.SampleCount)
                    throw new InvalidOperationException($"All inputs must have the same sample count. Expected {firstBuffer.SampleCount}, but input {i} has {buffer.SampleCount}.");
            }

            // Create output buffer
            var output = new AudioBuffer(firstBuffer.SampleRate, firstBuffer.ChannelCount, firstBuffer.SampleCount);
            try
            {
                // Mix all channels
                for (int ch = 0; ch < output.ChannelCount; ch++)
                {
                    var outData = output.GetChannelData(ch);

                    // Clear output buffer (already cleared in constructor, but being explicit)
                    outData.Clear();

                    for (int i = 0; i < buffers.Length; i++)
                    {
                        var inBuffer = buffers[i];
                        if (inBuffer == null)
                            continue;

                        var gain = i < _gains.Length ? _gains[i] : 1.0f;
                        var inData = inBuffer.GetChannelData(ch);

                        // Add with gain
                        for (int s = 0; s < output.SampleCount; s++)
                        {
                            outData[s] += inData[s] * gain;
                        }
                    }
                }

                RecordProcessedChannelCount(output.ChannelCount);
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }

    private bool IsBranchDead(int index, AudioProcessContext context)
    {
        AudioNode branch = Inputs[index];
        if (!_branchEndTimes.TryGetValue(branch, out TimeSpan branchEndTime))
            return false;

        if (_branchTailBudgets.TryGetValue(branch, out BranchTailBudget? budget)
            && budget.SampleRate == context.SampleRate)
        {
            return budget.RemainingSamples <= 0;
        }

        int branchLatency = branch.GetDrainLatencySamples(context.SampleRate);
        if (branchLatency == int.MaxValue)
            return _unknownDrainAttempts.Contains(branch);

        // A branch is dead once its retained tail ends at or just before this block. A one-tick
        // tolerance absorbs the same timestamp quantization used by contiguous audio ranges.
        long blockStartTicks = context.TimeRange.Start.Ticks;
        long deadAfterTicks = blockStartTicks > TimeSpan.MaxValue.Ticks - AudioProcessContext.TimestampQuantizationToleranceTicks
            ? TimeSpan.MaxValue.Ticks
            : blockStartTicks + AudioProcessContext.TimestampQuantizationToleranceTicks;

        long tailEndTicks = GetTailEndTicks(branchEndTime, branchLatency, context.SampleRate);
        return tailEndTicks <= deadAfterTicks;
    }

    private AudioBuffer FlushBranch(int index, AudioProcessContext context, out int drainedSamples)
    {
        AudioNode branch = Inputs[index];
        drainedSamples = 0;
        if (!_branchEndTimes.TryGetValue(branch, out TimeSpan branchEndTime)
            || context.TimeRange.Start < branchEndTime)
        {
            return branch.Flush(context);
        }

        int branchLatency = branch.GetDrainLatencySamples(context.SampleRate);
        if (branchLatency == int.MaxValue)
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
            long tailEndTicks = GetTailEndTicks(branchEndTime, branchLatency, context.SampleRate);
            long remainingTicks = tailEndTicks - context.TimeRange.Start.Ticks;
            remainingSamples = remainingTicks <= 0
                ? 0
                : Math.Min(
                    branchLatency,
                    AudioProcessContext.GetSampleCount(
                        new TimeRange(context.TimeRange.Start, TimeSpan.FromTicks(remainingTicks)),
                        context.SampleRate));
            budget = new BranchTailBudget
            {
                RemainingSamples = remainingSamples,
                SampleRate = context.SampleRate,
            };
            _branchTailBudgets[branch] = budget;
        }

        if (remainingSamples >= sampleCount)
        {
            drainedSamples = sampleCount;
            budget.RemainingSamples = Math.Max(0, remainingSamples - drainedSamples);
            return branch.Flush(context);
        }

        if (remainingSamples <= 0)
            return CreateSilentFlush(context);

        var drainContext = new AudioProcessContext(
            new TimeRange(
                context.TimeRange.Start,
                AudioProcessContext.GetDurationForSampleCount(remainingSamples, context.SampleRate)),
            context.SampleRate,
            context.AnimationSampler,
            context.OriginalTimeRange);
        using AudioBuffer drained = branch.Flush(drainContext);
        var output = new AudioBuffer(drained.SampleRate, drained.ChannelCount, sampleCount);
        try
        {
            int copyCount = Math.Min(drained.SampleCount, sampleCount);
            if (copyCount > 0)
                drained.CopyTo(output, 0, copyCount);

            drainedSamples = remainingSamples;
            budget.RemainingSamples = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private static long GetTailEndTicks(TimeSpan branchEndTime, int branchLatency, int sampleRate)
    {
        long latencyTicks = (long)Math.Ceiling(
            branchLatency * (double)TimeSpan.TicksPerSecond / sampleRate);
        return branchEndTime.Ticks > TimeSpan.MaxValue.Ticks - latencyTicks
            ? TimeSpan.MaxValue.Ticks
            : branchEndTime.Ticks + latencyTicks;
    }

    private static int ScaleSampleCount(int sampleCount, int sourceSampleRate, int destinationSampleRate)
    {
        if (sampleCount == int.MaxValue || sourceSampleRate == destinationSampleRate)
            return sampleCount;

        double scaled = sampleCount * (double)destinationSampleRate / sourceSampleRate;
        return scaled >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(scaled);
    }

    private bool IsBranchEnded(int index, AudioProcessContext context)
        => IsBranchEndedByTime(index, context) && _processedBranches.Contains(Inputs[index]);

    private bool IsBranchEndedByTime(int index, AudioProcessContext context)
        => _branchEndTimes.TryGetValue(Inputs[index], out TimeSpan branchEndTime)
            && context.TimeRange.Start >= branchEndTime;

    protected override void OnInputAdded(AudioNode input, int index)
    {
        AppendDefaultIfConfigured(ref _gains, index, 1f);
    }

    protected override object CaptureInputState(AudioNode input, int index)
    {
        return new InputState(
            (float[])_gains.Clone(),
            _branchEndTimes.TryGetValue(input, out TimeSpan branchEndTime),
            branchEndTime,
            _processedBranches.Contains(input),
            _unknownDrainAttempts.Contains(input),
            _branchTailBudgets.TryGetValue(input, out BranchTailBudget? budget),
            budget?.RemainingSamples ?? 0,
            budget?.SampleRate ?? 0,
            _lastTimeRangeEnd);
    }

    protected override void RestoreInputState(AudioNode input, int index, object? state)
    {
        if (state is not InputState snapshot)
            return;

        _gains = (float[])snapshot.Gains.Clone();
        if (snapshot.HasBranchEndTime)
            _branchEndTimes[input] = snapshot.BranchEndTime;
        else
            _branchEndTimes.Remove(input);

        if (snapshot.WasProcessed)
            _processedBranches.Add(input);
        else
            _processedBranches.Remove(input);

        if (snapshot.UnknownDrainAttempted)
            _unknownDrainAttempts.Add(input);
        else
            _unknownDrainAttempts.Remove(input);

        if (snapshot.HasTailBudget)
        {
            _branchTailBudgets[input] = new BranchTailBudget
            {
                RemainingSamples = snapshot.RemainingTailSamples,
                SampleRate = snapshot.TailBudgetSampleRate,
            };
        }
        else
        {
            _branchTailBudgets.Remove(input);
        }

        _lastTimeRangeEnd = snapshot.LastTimeRangeEnd;
    }

    protected override void OnInputRemoved(AudioNode input, int index)
    {
        _gains = RemoveAt(_gains, index);
        _branchEndTimes.Remove(input);
        _processedBranches.Remove(input);
        _unknownDrainAttempts.Remove(input);
        _branchTailBudgets.Remove(input);
        _lastTimeRangeEnd = null;
    }

    protected override void OnInputsCleared()
    {
        _gains = Array.Empty<float>();
        _branchEndTimes.Clear();
        _processedBranches.Clear();
        _unknownDrainAttempts.Clear();
        _branchTailBudgets.Clear();
        _lastTimeRangeEnd = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gains = Array.Empty<float>();
            _branchEndTimes.Clear();
            _processedBranches.Clear();
            _unknownDrainAttempts.Clear();
            _branchTailBudgets.Clear();
            _lastTimeRangeEnd = null;
        }

        base.Dispose(disposing);
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

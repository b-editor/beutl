using System;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed class MixerNode : AudioNode
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

    private sealed record InputState(
        float[] Gains,
        bool HasBranchEndTime,
        MediaTime BranchEndTime,
        bool WasProcessed,
        bool UnknownDrainAttempted,
        bool HasTailBudget,
        int RemainingTailSamples,
        int TailBudgetSampleRate,
        MediaTime? LastTimeRangeEnd);

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
            if (_unknownDrainAttempts.Contains(input))
                continue;

            if (_branchTailBudgets.TryGetValue(input, out BranchTailBudget? budget))
            {
                if (budget.RemainingSamples <= 0)
                    continue;

                total = Math.Max(total, AudioLatency.ScaleSampleCount(budget.RemainingSamples, budget.SampleRate, sampleRate));
                continue;
            }

            // A discontinuous process clears _processedBranches before asking the branch for audio.
            // If the branch has already ended and was not processed in this run, its reported
            // latency belongs to an abandoned timeline rather than the current drain.
            if (_branchEndTimes.ContainsKey(input) && !_processedBranches.Contains(input))
                continue;

            int branchLatency = input.GetDrainLatencySamples(sampleRate);
            AudioLatency.ThrowIfNegative(input, branchLatency, "drain latency");

            if (branchLatency != int.MaxValue
                && _branchEndTimes.TryGetValue(input, out MediaTime branchEndTime)
                && _lastTimeRangeEnd is { } processedEnd
                && GetTailEnd(branchEndTime, branchLatency, sampleRate) <= processedEnd)
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
                    ? FlushBranch(i, context)
                    : Inputs[i].Process(context);
                if (unknownDrain && context.GetSampleCount() > 0)
                    _unknownDrainAttempts.Add(Inputs[i]);
                if (!drainBranch && !branchEnded)
                    _processedBranches.Add(Inputs[i]);
                if (!drainBranch
                    && _branchEndTimes.TryGetValue(Inputs[i], out MediaTime branchEndTime)
                    && !branchEnded
                    && context.TimeRange.End >= branchEndTime)
                {
                    RecordProcessedTailBudget(Inputs[i], branchEndTime, context.TimeRange.End, context.SampleRate);
                }
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

            var output = new AudioBuffer(firstBuffer.SampleRate, firstBuffer.ChannelCount, firstBuffer.SampleCount);
            try
            {
                for (int ch = 0; ch < output.ChannelCount; ch++)
                {
                    var outData = output.GetChannelData(ch);

                    for (int i = 0; i < buffers.Length; i++)
                    {
                        var inBuffer = buffers[i];
                        if (inBuffer == null)
                            continue;

                        var gain = i < _gains.Length ? _gains[i] : 1.0f;
                        var inData = inBuffer.GetChannelData(ch);

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
        if (!_branchEndTimes.TryGetValue(branch, out MediaTime branchEndTime))
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
        MediaTime blockStart = context.TimeRange.Start;
        MediaTime tolerance = AudioProcessContext.TimestampQuantizationTolerance;
        MediaTime deadAfter = blockStart > MediaTime.MaxValue - tolerance ? MediaTime.MaxValue : blockStart + tolerance;

        return GetTailEnd(branchEndTime, branchLatency, context.SampleRate) <= deadAfter;
    }

    private AudioBuffer FlushBranch(int index, AudioProcessContext context)
    {
        AudioNode branch = Inputs[index];
        if (!_branchEndTimes.TryGetValue(branch, out MediaTime branchEndTime)
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
            MediaTime remaining = GetTailEnd(branchEndTime, branchLatency, context.SampleRate) - context.TimeRange.Start;
            remainingSamples = remaining <= MediaTime.Zero
                ? 0
                : Math.Min(
                    branchLatency,
                    AudioProcessContext.GetSampleCount(new TimeRange(context.TimeRange.Start, remaining), context.SampleRate));
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
            return CreateSilentFlush(context);

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

    private void RecordProcessedTailBudget(
        AudioNode branch,
        MediaTime branchEndTime,
        MediaTime processedEnd,
        int sampleRate)
    {
        int branchLatency = branch.GetDrainLatencySamples(sampleRate);
        AudioLatency.ThrowIfNegative(branch, branchLatency, "drain latency");

        if (branchLatency == int.MaxValue)
            return;

        MediaTime remaining = GetTailEnd(branchEndTime, branchLatency, sampleRate) - processedEnd;
        int remainingSamples = remaining <= MediaTime.Zero
            ? 0
            : (int)Math.Min(branchLatency, remaining.ToSamples(sampleRate, MidpointRounding.ToPositiveInfinity));
        _branchTailBudgets[branch] = new BranchTailBudget
        {
            RemainingSamples = remainingSamples,
            SampleRate = sampleRate,
        };
    }

    private bool IsBranchEnded(int index, AudioProcessContext context)
        => IsBranchEndedByTime(index, context) && _processedBranches.Contains(Inputs[index]);

    private bool IsBranchEndedByTime(int index, AudioProcessContext context)
        => _branchEndTimes.TryGetValue(Inputs[index], out MediaTime branchEndTime)
            && context.TimeRange.Start >= branchEndTime;

    protected override void OnInputAdded(AudioNode input, int index)
    {
        AppendDefaultIfConfigured(ref _gains, index, 1f);
    }

    protected override object CaptureInputState(AudioNode input, int index)
    {
        return new InputState(
            (float[])_gains.Clone(),
            _branchEndTimes.TryGetValue(input, out MediaTime branchEndTime),
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

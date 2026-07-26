using System;

namespace Beutl.Audio.Graph.Nodes;

public sealed class MixerNode : AudioNode
{
    private const long BranchLivenessToleranceTicks = TimeSpan.TicksPerMillisecond;

    private float[] _gains = Array.Empty<float>();
    private readonly Dictionary<AudioNode, TimeSpan> _branchEndTimes =
        new(ReferenceEqualityComparer.Instance);

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
    }

    /// <summary>
    /// Removes the recorded end time for a connected input so the branch is considered live again.
    /// </summary>
    /// <returns><see langword="true"/> when an end time was removed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="input"/> is not connected to this mixer.</exception>
    public bool ClearBranchEndTime(AudioNode input)
    {
        EnsureConnectedInput(input);
        return _branchEndTimes.Remove(input);
    }

    public override AudioBuffer Process(AudioProcessContext context)
        => Mix(context, drain: false);

    // Fan-in flush: drain every branch and mix the held tails with the same gain fold as Process, so a
    // lookahead tail in any live branch is recovered (the base Flush's single-input path cannot reach
    // here). A branch that ended before this drain block is skipped because its tail was already
    // recovered at the child's own clip end.
    public override AudioBuffer Flush(AudioProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Inputs.Count == 0)
            return CreateSilentFlush(context);

        return Mix(context, drain: true);
    }

    private AudioBuffer Mix(AudioProcessContext context, bool drain)
    {
        if (Inputs.Count == 0)
            throw new InvalidOperationException("Mixer requires at least one input.");

        // A dead branch (its clip ended before this drain block) keeps a null slot: its tail was already
        // recovered at its own clip end, so re-draining it here would leak a stale tail into the group
        // pad. Liveness only applies while draining — Process mixes every branch.
        var buffers = new AudioBuffer[Inputs.Count];
        try
        {
            for (int i = 0; i < Inputs.Count; i++)
            {
                if (drain && IsBranchDead(i, context))
                    continue;

                buffers[i] = drain ? Inputs[i].Flush(context) : Inputs[i].Process(context);
            }

            // The format reference is the first live branch; every branch dead means nothing to drain.
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

            // Validate live buffers have the same format
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

                    // Mix each live input
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
                // Dispose the output the caller never received rather than leak it
                // (inputs are released by the outer finally).
                output.Dispose();
                throw;
            }
        }
        finally
        {
            // Dispose every consumed input (also on the validation-throw / dead-branch path, where
            // slots may be null).
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }

    private bool IsBranchDead(int index, AudioProcessContext context)
    {
        if (!_branchEndTimes.TryGetValue(Inputs[index], out TimeSpan branchEndTime))
            return false;

        // Dead = the branch's clip ended before this drain block. The tolerance absorbs sample-tick
        // rounding so a branch ending exactly at the group end stays live and its tail still drains.
        // Subtract from the block start with saturation instead of adding to the branch end, so even
        // TimeSpan.MaxValue cannot overflow into a negative tick count.
        long blockStartTicks = context.TimeRange.Start.Ticks;
        long deadBeforeTicks = blockStartTicks < TimeSpan.MinValue.Ticks + BranchLivenessToleranceTicks
            ? TimeSpan.MinValue.Ticks
            : blockStartTicks - BranchLivenessToleranceTicks;
        return branchEndTime.Ticks < deadBeforeTicks;
    }

    protected override void OnInputAdded(AudioNode input, int index)
    {
        AppendDefaultIfConfigured(ref _gains, index, 1f);
    }

    protected override void OnInputRemoved(AudioNode input, int index)
    {
        _gains = RemoveAt(_gains, index);
        _branchEndTimes.Remove(input);
    }

    protected override void OnInputsCleared()
    {
        _gains = Array.Empty<float>();
        _branchEndTimes.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gains = Array.Empty<float>();
            _branchEndTimes.Clear();
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

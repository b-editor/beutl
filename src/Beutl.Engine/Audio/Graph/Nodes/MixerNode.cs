using System;
using System.Linq;

namespace Beutl.Audio.Graph.Nodes;

public sealed class MixerNode : AudioNode
{
    private const long BranchLivenessToleranceTicks = TimeSpan.TicksPerMillisecond;

    private float[] _gains = Array.Empty<float>();
    private TimeSpan[] _branchEndTimes = Array.Empty<TimeSpan>();

    public float[] Gains
    {
        get => _gains;
        set => _gains = value ?? Array.Empty<float>();
    }

    // Group-local end time of each input branch, parallel to Inputs (empty = every branch live, so all
    // drain — the back-compat default). A branch whose clip ended before the flush block is dead: its
    // tail was recovered at its own clip end, so Flush must skip it instead of re-emitting it late.
    public TimeSpan[] BranchEndTimes
    {
        get => _branchEndTimes;
        set => _branchEndTimes = value ?? Array.Empty<TimeSpan>();
    }

    public override AudioBuffer Process(AudioProcessContext context)
        => Mix(context, drain: false);

    // Fan-in flush: drain every branch and mix the held tails with the same gain fold as Process, so a
    // lookahead tail in any branch is recovered (the base Flush's single-input path cannot reach here).
    //
    // Known limitation: branches are drained unconditionally. A branch whose clip ended before the
    // group's terminal slice — and whose own terminal block landed exactly on its clip boundary (the
    // chunk-alignment edge where ClipNode could not self-recover) — still holds a stale tail that this
    // emits into the group pad seconds late. Skipping only the branches live through the terminal slice
    // needs per-branch clip-liveness the mixer does not track today; recovering at the child's own end is
    // the chunk-alignment follow-up.
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
        if (index >= _branchEndTimes.Length)
            return false;

        // Dead = the branch's clip ended before this drain block. The tolerance absorbs sample-tick
        // rounding so a branch ending exactly at the group end stays live and its tail still drains.
        return _branchEndTimes[index].Ticks + BranchLivenessToleranceTicks < context.TimeRange.Start.Ticks;
    }
}

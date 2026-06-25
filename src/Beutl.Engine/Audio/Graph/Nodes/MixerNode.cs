using System;
using System.Linq;

namespace Beutl.Audio.Graph.Nodes;

public sealed class MixerNode : AudioNode
{
    private float[] _gains = Array.Empty<float>();

    public float[] Gains
    {
        get => _gains;
        set => _gains = value ?? Array.Empty<float>();
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

        // Process all inputs
        var buffers = new AudioBuffer[Inputs.Count];
        try
        {
            for (int i = 0; i < Inputs.Count; i++)
            {
                buffers[i] = drain ? Inputs[i].Flush(context) : Inputs[i].Process(context);
            }

            // Validate all buffers have the same format
            var firstBuffer = buffers[0];
            for (int i = 1; i < buffers.Length; i++)
            {
                if (buffers[i].SampleRate != firstBuffer.SampleRate)
                    throw new InvalidOperationException($"All inputs must have the same sample rate. Expected {firstBuffer.SampleRate}, but input {i} has {buffers[i].SampleRate}.");
                if (buffers[i].ChannelCount != firstBuffer.ChannelCount)
                    throw new InvalidOperationException($"All inputs must have the same channel count. Expected {firstBuffer.ChannelCount}, but input {i} has {buffers[i].ChannelCount}.");
                if (buffers[i].SampleCount != firstBuffer.SampleCount)
                    throw new InvalidOperationException($"All inputs must have the same sample count. Expected {firstBuffer.SampleCount}, but input {i} has {buffers[i].SampleCount}.");
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

                    // Mix each input
                    for (int i = 0; i < buffers.Length; i++)
                    {
                        var gain = i < _gains.Length ? _gains[i] : 1.0f;
                        var inData = buffers[i].GetChannelData(ch);

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
            // Dispose every consumed input (also on the validation-throw path, where trailing
            // slots may still be null).
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }
}

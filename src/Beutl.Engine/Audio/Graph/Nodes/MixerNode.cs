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
    {
        if (Inputs.Count == 0)
            throw new InvalidOperationException("Mixer requires at least one input.");

        // Process all inputs
        var buffers = new AudioBuffer[Inputs.Count];
        for (int i = 0; i < Inputs.Count; i++)
        {
            buffers[i] = Inputs[i].Process(context);
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
}
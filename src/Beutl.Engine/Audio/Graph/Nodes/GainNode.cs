using Beutl.Engine;

namespace Beutl.Audio.Graph.Nodes;

public sealed class GainNode : AudioNode
{
    public required IProperty<float> Gain { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Gain node requires exactly one input.");

        var input = Inputs[0].Process(context);

        if (Gain.Animation is null)
        {
            return ProcessStaticGain(input);
        }

        return ProcessAnimatedGain(Gain, input, context);
    }

    // Owns and disposes `input`; emits a fresh buffer.
    private static AudioBuffer ProcessAnimatedGain(IProperty<float> gain, AudioBuffer input, AudioProcessContext context)
    {
        using (input)
        {
            // Create output buffer
            var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

            // Sample gain values for each sample
            Span<float> gains = stackalloc float[System.Math.Min(input.SampleCount, 8192)];

            // Process in chunks to avoid stack overflow for large buffers
            int processed = 0;
            while (processed < input.SampleCount)
            {
                int chunkSize = System.Math.Min(gains.Length, input.SampleCount - processed);
                var chunkGains = gains.Slice(0, chunkSize);

                var chunkStart = context.GetTimeForSample(processed);
                var chunkEnd = context.GetTimeForSample(processed + chunkSize);
                // すでにcontext.TimeRange.StartがGetTimeForSampleで加算されている
                var chunkRange = new Media.TimeRange(chunkStart, chunkEnd - chunkStart);

                // Sample animation values
                context.AnimationSampler.SampleBuffer(
                    gain,
                    chunkRange,
                    context.SampleRate,
                    chunkGains);

                // Convert from percentage (0-100) to factor (0-1)
                for (int i = 0; i < chunkSize; i++)
                {
                    chunkGains[i] /= 100f;
                }

                // Apply gain to each channel
                for (int ch = 0; ch < input.ChannelCount; ch++)
                {
                    var inData = input.GetChannelData(ch).Slice(processed, chunkSize);
                    var outData = output.GetChannelData(ch).Slice(processed, chunkSize);

                    for (int i = 0; i < chunkSize; i++)
                    {
                        outData[i] = inData[i] * chunkGains[i];
                    }
                }

                processed += chunkSize;
            }

            return output;
        }
    }

    private AudioBuffer ProcessStaticGain(AudioBuffer input)
    {
        float gain = Gain.CurrentValue / 100f;
        // Unity gain: pass the input through (caller owns it, don't dispose).
        if (System.Math.Abs(gain - 1.0f) < float.Epsilon)
            return input;

        // Otherwise we emit a fresh buffer, so dispose the consumed input.
        using (input)
        {
            var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

            for (int ch = 0; ch < input.ChannelCount; ch++)
            {
                var inData = input.GetChannelData(ch);
                var outData = output.GetChannelData(ch);

                for (int i = 0; i < input.SampleCount; i++)
                {
                    outData[i] = inData[i] * gain;
                }
            }

            return output;
        }
    }
}

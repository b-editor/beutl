using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Beutl.Audio.Graph;

public static class AudioMath
{
    private const float DbToLinearConstant = 0.11512925464970229f; // 1 / (20 * log10(e))
    private const float LinearToDbConstant = 8.6858896380650365f;  // 20 * log10(e)

    public static void AddWithGain(ReadOnlySpan<float> input, Span<float> output, float gain)
    {
        if (input.Length != output.Length)
            throw new ArgumentException("Input and output spans must have the same length.");

        if (Vector.IsHardwareAccelerated && input.Length >= Vector<float>.Count)
        {
            AddWithGainVectorized(input, output, gain);
        }
        else
        {
            AddWithGainScalar(input, output, gain);
        }
    }

    public static void MultiplyBuffers(ReadOnlySpan<float> input, ReadOnlySpan<float> gains, Span<float> output)
    {
        if (input.Length != gains.Length || input.Length != output.Length)
            throw new ArgumentException("All spans must have the same length.");

        if (Vector.IsHardwareAccelerated && input.Length >= Vector<float>.Count)
        {
            MultiplyBuffersVectorized(input, gains, output);
        }
        else
        {
            MultiplyBuffersScalar(input, gains, output);
        }
    }

    public static void ApplyGain(Span<float> buffer, float gain)
    {
        if (Vector.IsHardwareAccelerated && buffer.Length >= Vector<float>.Count)
        {
            ApplyGainVectorized(buffer, gain);
        }
        else
        {
            ApplyGainScalar(buffer, gain);
        }
    }

    public static void MixBuffers(ReadOnlySpan<float> input1, ReadOnlySpan<float> input2, Span<float> output, float mix1 = 0.5f, float mix2 = 0.5f)
    {
        if (input1.Length != input2.Length || input1.Length != output.Length)
            throw new ArgumentException("All spans must have the same length.");

        if (Vector.IsHardwareAccelerated && input1.Length >= Vector<float>.Count)
        {
            MixBuffersVectorized(input1, input2, output, mix1, mix2);
        }
        else
        {
            MixBuffersScalar(input1, input2, output, mix1, mix2);
        }
    }

    public static float ConvertDbToLinear(float db)
    {
        return MathF.Pow(10f, db * 0.05f);
    }

    public static float ConvertLinearToDb(float linear)
    {
        return linear > 0f ? 20f * MathF.Log10(linear) : -100f;
    }

    public static void ConvertDbToLinear(ReadOnlySpan<float> dbValues, Span<float> linearValues)
    {
        if (dbValues.Length != linearValues.Length)
            throw new ArgumentException("Input and output spans must have the same length.");

        for (int i = 0; i < dbValues.Length; i++)
        {
            linearValues[i] = ConvertDbToLinear(dbValues[i]);
        }
    }

    public static float CalculateRms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
            return 0f;

        double sum = 0.0;

        if (Vector.IsHardwareAccelerated && samples.Length >= Vector<float>.Count)
        {
            sum = CalculateRmsVectorized(samples);
        }
        else
        {
            for (int i = 0; i < samples.Length; i++)
            {
                float sample = samples[i];
                sum += sample * sample;
            }
        }

        return MathF.Sqrt((float)(sum / samples.Length));
    }

    public static float FindPeak(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
            return 0f;

        float peak = 0f;

        if (Vector.IsHardwareAccelerated && samples.Length >= Vector<float>.Count)
        {
            peak = FindPeakVectorized(samples);
        }
        else
        {
            for (int i = 0; i < samples.Length; i++)
            {
                float abs = MathF.Abs(samples[i]);
                if (abs > peak)
                    peak = abs;
            }
        }

        return peak;
    }

    public static void ApplyLimiter(Span<float> buffer, float threshold = 1.0f, float ratio = 10.0f)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            float abs = MathF.Abs(sample);

            if (abs > threshold)
            {
                float excess = abs - threshold;
                float compressedExcess = excess / ratio;
                float newAbs = threshold + compressedExcess;
                buffer[i] = sample >= 0 ? newAbs : -newAbs;
            }
        }
    }

    public static void ApplySoftClipper(Span<float> buffer, float threshold = 0.8f)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            float abs = MathF.Abs(sample);

            if (abs > threshold)
            {
                // Soft clipping using tanh function
                float normalizedSample = sample / abs;
                float clippedAbs = threshold + (1f - threshold) * MathF.Tanh((abs - threshold) / (1f - threshold));
                buffer[i] = normalizedSample * clippedAbs;
            }
        }
    }

    public static void Normalize(Span<float> buffer, float targetLevel = 1.0f)
    {
        float peak = FindPeak(buffer);
        if (peak > 0f && peak != targetLevel)
        {
            float gain = targetLevel / peak;
            ApplyGain(buffer, gain);
        }
    }

    public static void FadeIn(Span<float> buffer, int fadeLength)
    {
        int actualFadeLength = System.Math.Min(fadeLength, buffer.Length);

        for (int i = 0; i < actualFadeLength; i++)
        {
            float gain = (float)i / actualFadeLength;
            buffer[i] *= gain;
        }
    }

    public static void FadeOut(Span<float> buffer, int fadeLength)
    {
        int actualFadeLength = System.Math.Min(fadeLength, buffer.Length);
        int startIndex = buffer.Length - actualFadeLength;

        for (int i = 0; i < actualFadeLength; i++)
        {
            float gain = 1f - (float)i / actualFadeLength;
            buffer[startIndex + i] *= gain;
        }
    }

    #region Vectorized Implementations

    private static void AddWithGainVectorized(ReadOnlySpan<float> input, Span<float> output, float gain)
    {
        var gainVector = new Vector<float>(gain);
        int vectorSize = Vector<float>.Count;
        int vectorCount = input.Length / vectorSize;

        for (int i = 0; i < vectorCount; i++)
        {
            int offset = i * vectorSize;
            var inputVector = new Vector<float>(input.Slice(offset, vectorSize));
            var outputVector = new Vector<float>(output.Slice(offset, vectorSize));
            var result = outputVector + inputVector * gainVector;
            result.CopyTo(output.Slice(offset, vectorSize));
        }

        // Handle remaining elements
        int remaining = input.Length % vectorSize;
        if (remaining > 0)
        {
            int offset = vectorCount * vectorSize;
            AddWithGainScalar(input.Slice(offset), output.Slice(offset), gain);
        }
    }

    private static void MultiplyBuffersVectorized(ReadOnlySpan<float> input, ReadOnlySpan<float> gains, Span<float> output)
    {
        int vectorSize = Vector<float>.Count;
        int vectorCount = input.Length / vectorSize;

        for (int i = 0; i < vectorCount; i++)
        {
            int offset = i * vectorSize;
            var inputVector = new Vector<float>(input.Slice(offset, vectorSize));
            var gainsVector = new Vector<float>(gains.Slice(offset, vectorSize));
            var result = inputVector * gainsVector;
            result.CopyTo(output.Slice(offset, vectorSize));
        }

        // Handle remaining elements
        int remaining = input.Length % vectorSize;
        if (remaining > 0)
        {
            int offset = vectorCount * vectorSize;
            MultiplyBuffersScalar(input.Slice(offset), gains.Slice(offset), output.Slice(offset));
        }
    }

    private static void ApplyGainVectorized(Span<float> buffer, float gain)
    {
        var gainVector = new Vector<float>(gain);
        int vectorSize = Vector<float>.Count;
        int vectorCount = buffer.Length / vectorSize;

        for (int i = 0; i < vectorCount; i++)
        {
            int offset = i * vectorSize;
            var bufferVector = new Vector<float>(buffer.Slice(offset, vectorSize));
            var result = bufferVector * gainVector;
            result.CopyTo(buffer.Slice(offset, vectorSize));
        }

        // Handle remaining elements
        int remaining = buffer.Length % vectorSize;
        if (remaining > 0)
        {
            int offset = vectorCount * vectorSize;
            ApplyGainScalar(buffer.Slice(offset), gain);
        }
    }

    private static void MixBuffersVectorized(ReadOnlySpan<float> input1, ReadOnlySpan<float> input2, Span<float> output, float mix1, float mix2)
    {
        var mix1Vector = new Vector<float>(mix1);
        var mix2Vector = new Vector<float>(mix2);
        int vectorSize = Vector<float>.Count;
        int vectorCount = input1.Length / vectorSize;

        for (int i = 0; i < vectorCount; i++)
        {
            int offset = i * vectorSize;
            var input1Vector = new Vector<float>(input1.Slice(offset, vectorSize));
            var input2Vector = new Vector<float>(input2.Slice(offset, vectorSize));
            var result = input1Vector * mix1Vector + input2Vector * mix2Vector;
            result.CopyTo(output.Slice(offset, vectorSize));
        }

        // Handle remaining elements
        int remaining = input1.Length % vectorSize;
        if (remaining > 0)
        {
            int offset = vectorCount * vectorSize;
            MixBuffersScalar(input1.Slice(offset), input2.Slice(offset), output.Slice(offset), mix1, mix2);
        }
    }

    private static double CalculateRmsVectorized(ReadOnlySpan<float> samples)
    {
        double sum = 0.0;
        int vectorSize = Vector<float>.Count;
        int vectorCount = samples.Length / vectorSize;

        for (int i = 0; i < vectorCount; i++)
        {
            int offset = i * vectorSize;
            var vector = new Vector<float>(samples.Slice(offset, vectorSize));
            var squared = vector * vector;

            for (int j = 0; j < vectorSize; j++)
            {
                sum += squared[j];
            }
        }

        // Handle remaining elements
        int remaining = samples.Length % vectorSize;
        if (remaining > 0)
        {
            int offset = vectorCount * vectorSize;
            for (int i = offset; i < samples.Length; i++)
            {
                float sample = samples[i];
                sum += sample * sample;
            }
        }

        return sum;
    }

    private static float FindPeakVectorized(ReadOnlySpan<float> samples)
    {
        float peak = 0f;
        int vectorSize = Vector<float>.Count;
        int vectorCount = samples.Length / vectorSize;

        for (int i = 0; i < vectorCount; i++)
        {
            int offset = i * vectorSize;
            var vector = new Vector<float>(samples.Slice(offset, vectorSize));
            var abs = Vector.Abs(vector);

            for (int j = 0; j < vectorSize; j++)
            {
                if (abs[j] > peak)
                    peak = abs[j];
            }
        }

        // Handle remaining elements
        int remaining = samples.Length % vectorSize;
        if (remaining > 0)
        {
            int offset = vectorCount * vectorSize;
            for (int i = offset; i < samples.Length; i++)
            {
                float abs = MathF.Abs(samples[i]);
                if (abs > peak)
                    peak = abs;
            }
        }

        return peak;
    }

    #endregion

    #region Scalar Implementations

    private static void AddWithGainScalar(ReadOnlySpan<float> input, Span<float> output, float gain)
    {
        for (int i = 0; i < input.Length; i++)
        {
            output[i] += input[i] * gain;
        }
    }

    private static void MultiplyBuffersScalar(ReadOnlySpan<float> input, ReadOnlySpan<float> gains, Span<float> output)
    {
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = input[i] * gains[i];
        }
    }

    private static void ApplyGainScalar(Span<float> buffer, float gain)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= gain;
        }
    }

    private static void MixBuffersScalar(ReadOnlySpan<float> input1, ReadOnlySpan<float> input2, Span<float> output, float mix1, float mix2)
    {
        for (int i = 0; i < input1.Length; i++)
        {
            output[i] = input1[i] * mix1 + input2[i] * mix2;
        }
    }

    #endregion
}

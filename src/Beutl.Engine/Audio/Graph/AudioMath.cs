using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Beutl.Audio.Graph;

public static class AudioMath
{
    private const float DbToLinearConstant = 0.11512925464970229f; // 1 / (20 * log10(e))
    private const float LinearToDbConstant = 8.6858896380650365f;  // 20 * log10(e)

    public static float ConvertDbToLinear(float db)
    {
        return MathF.Pow(10f, db * 0.05f);
    }

    public static float ConvertLinearToDb(float linear)
    {
        return linear > 0f ? 20f * MathF.Log10(linear) : -100f;
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

    /// <summary>
    /// Soft, ratio-based downward compressor applied in place: samples above <paramref name="threshold"/>
    /// are reduced toward <c>threshold + (excess / ratio)</c> but not held at a hard ceiling, so the
    /// output can still exceed the threshold. Despite the name this is NOT a brick-wall peak limiter
    /// (see <see cref="Beutl.Audio.Effects.LimiterEffect"/>); it is the always-on master
    /// clip-protection backstop in <see cref="Beutl.Audio.Composing.Composer"/>.
    /// </summary>
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

    #region Vectorized Implementations

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
}

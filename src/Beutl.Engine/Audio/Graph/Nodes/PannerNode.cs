using Beutl.Animation;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Engine;
using static Beutl.Audio.AudioSpeakers;

namespace Beutl.Audio.Graph.Nodes;

public sealed class PannerNode : AudioNode
{
    private readonly BiQuadFilter _lfeFilter = new();
    private MediaTime? _lastEnd;
    private AudioChannelMatrix? _matrix;
    private AudioChannelLayout _matrixInput;
    private AudioChannelLayout _matrixOutput;
    private BalanceSide[] _balanceSides = [];

    private enum BalanceSide : byte { Other, Left, Right }

    public required IProperty<float> Pan { get; init; }
    public required IProperty<float> SurroundPan { get; init; }
    public required IProperty<float> CenterLevel { get; init; }
    public required IProperty<float> LfeLevel { get; init; }
    public AudioChannelLayout InputLayout { get; set; } = AudioChannelLayout.Stereo;
    public AudioChannelLayout OutputLayout { get; set; } = AudioChannelLayout.Stereo;

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Panner node requires exactly one input.");
        return ProcessTail(Inputs[0].Process(context), context, draining: false);
    }

    protected override AudioBuffer ProcessTail(AudioBuffer input, AudioProcessContext context, bool draining)
    {
        AudioBuffer? output = null;
        try
        {
            if (input.ChannelCount != InputLayout.ChannelCount)
                throw new InvalidOperationException("Input channel count does not match the panner layout.");
            if (_matrix is null || _matrixInput != InputLayout || _matrixOutput != OutputLayout)
            {
                if (_matrix is null || _matrixOutput != OutputLayout)
                {
                    _balanceSides = new BalanceSide[OutputLayout.ChannelCount];
                    for (int channel = 0; channel < _balanceSides.Length; channel++)
                    {
                        _balanceSides[channel] = OutputLayout.GetSpeaker(channel) switch
                        {
                            FrontLeft or FrontLeftOfCenter or SideLeft or BackLeft or TopFrontLeft or TopBackLeft => BalanceSide.Left,
                            FrontRight or FrontRightOfCenter or SideRight or BackRight or TopFrontRight or TopBackRight => BalanceSide.Right,
                            _ => BalanceSide.Other
                        };
                    }
                }
                // One/two-channel clips feed the panner's L/R sides; larger layouts retain positions.
                AudioChannelLayout inputLayout = InputLayout.ChannelCount <= 2 && OutputLayout != AudioChannelLayout.Mono
                    ? AudioChannelLayout.GetDefault(InputLayout.ChannelCount) : InputLayout;
                _matrix = AudioChannelMatrix.Create(inputLayout, OutputLayout);
                _matrixInput = InputLayout;
                _matrixOutput = OutputLayout;
            }

            bool position = input.ChannelCount <= 2
                && (OutputLayout == AudioChannelLayout.Surround51 || OutputLayout == AudioChannelLayout.Surround71);
            if (position)
            {
                if (_lastEnd is not { } end || !context.ContinuesFrom(end))
                    _lfeFilter.Reset();
                // Butterworth Q gives a flat passband with the specified 120 Hz cutoff.
                _lfeFilter.CalculateCoefficients(BiQuadFilterType.LowPass, 120f, 0.70710678f, 0, context.SampleRate);
            }

            bool animated = OutputLayout != AudioChannelLayout.Mono
                && (Pan.GetKeyFrameAnimation() != null || (position
                    && (SurroundPan.GetKeyFrameAnimation() != null || CenterLevel.GetKeyFrameAnimation() != null
                        || LfeLevel.GetKeyFrameAnimation() != null)));
            output = _matrix.IsIdentity ? input : _matrix.Apply(input);
            if (OutputLayout != AudioChannelLayout.Mono)
            {
                if (!animated)
                {
                    ApplyStatic(input, output, Pan.Value, SurroundPan.Value, CenterLevel.Value, LfeLevel.Value, position);
                }
                else
                {
                    ApplyAnimated(input, output, context, position);
                }
            }
            if (input.SampleCount > 0)
                _lastEnd = context.TimeRange.End;
            if (!ReferenceEquals(input, output))
                input.Dispose();
            return RecordProcessedOutput(output);
        }
        catch
        {
            _lastEnd = null;
            if (!ReferenceEquals(input, output))
                output?.Dispose();
            input.Dispose();
            throw;
        }
    }

    private void ApplyStatic(AudioBuffer input, AudioBuffer output,
        float pan, float surround, float center, float lfe, bool position)
    {
        float x = Math.Clamp(pan / 100f, -1, 1);
        float left = Math.Min(1, 1 - x);
        float right = Math.Min(1, 1 + x);
        if (!position)
        {
            if (left == 1 && right == 1)
                return;
            for (int channel = 0; channel < output.ChannelCount; channel++)
            {
                float gain = _balanceSides[channel] switch
                {
                    BalanceSide.Left => left,
                    BalanceSide.Right => right,
                    _ => 1
                };
                if (gain != 1)
                    Scale(output.GetChannelData(channel), gain);
            }
            return;
        }

        float y = Math.Clamp(surround / 100f, 0, 1);
        float cs = Math.Clamp(center / 100f, 0, 1) * (1 - Math.Abs(x));
        float l = Math.Clamp(lfe / 100f, 0, 1);
        bool surround71 = OutputLayout == AudioChannelLayout.Surround71;
        var (front, side, back) = GetSurroundWeights(y, surround71);

        bool mono = input.ChannelCount == 1;
        ReadOnlySpan<float> sourceLeft = input.GetChannelData(0);
        ReadOnlySpan<float> sourceRight = mono ? sourceLeft : input.GetChannelData(1);
        // The default matrix has already copied FL/FR exactly, including signed zeros.
        if (x == 0 && y == 0 && cs == 0 && l == 0)
        {
            for (int i = 0; i < input.SampleCount; i++)
                _lfeFilter.Process(mono ? sourceLeft[i] : (sourceLeft[i] + sourceRight[i]) / 2);
            return;
        }

        Span<float> frontLeft = output.GetChannelData(0), frontRight = output.GetChannelData(1);
        Span<float> centerChannel = output.GetChannelData(2), lfeChannel = output.GetChannelData(3);
        Span<float> backLeft = output.GetChannelData(4), backRight = output.GetChannelData(5);
        Span<float> sideLeft = surround71 ? output.GetChannelData(6) : default;
        Span<float> sideRight = surround71 ? output.GetChannelData(7) : default;
        for (int i = 0; i < input.SampleCount; i++)
        {
            float a = sourceLeft[i], b = sourceRight[i];
            float low = _lfeFilter.Process(mono ? a : (a + b) / 2);
            float balancedLeft = Multiply(a, left), balancedRight = Multiply(b, right);
            float lf = Multiply(balancedLeft, front), rf = Multiply(balancedRight, front);
            frontLeft[i] = Multiply(lf, 1 - cs);
            frontRight[i] = Multiply(rf, 1 - cs);
            centerChannel[i] = cs == 0 ? 0 : cs * (lf + rf) / 2;
            lfeChannel[i] = Multiply(low, l);
            backLeft[i] = Multiply(balancedLeft, back);
            backRight[i] = Multiply(balancedRight, back);
            if (surround71)
            {
                sideLeft[i] = Multiply(balancedLeft, side);
                sideRight[i] = Multiply(balancedRight, side);
            }
        }
    }

    private void ApplyAnimated(AudioBuffer input, AudioBuffer output, AudioProcessContext context, bool position)
    {
        const int maxChunk = 2048;
        // Reuse sampled-property spans for gains, keeping scratch space bounded by the chunk size.
        Span<float> values = stackalloc float[maxChunk * (position ? 7 : 2)];
        bool surround71 = OutputLayout == AudioChannelLayout.Surround71;
        for (int offset = 0; offset < input.SampleCount; offset += maxChunk)
        {
            int count = Math.Min(maxChunk, input.SampleCount - offset);
            Span<float> left = values[..count], right = values.Slice(maxChunk, count);
            Sample(Pan, left);
            if (!position)
            {
                for (int i = 0; i < count; i++)
                {
                    float x = Math.Clamp(left[i] / 100f, -1, 1);
                    left[i] = Math.Min(1, 1 - x);
                    right[i] = Math.Min(1, 1 + x);
                }
                for (int channel = 0; channel < output.ChannelCount; channel++)
                {
                    if (_balanceSides[channel] == BalanceSide.Other)
                        continue;
                    ReadOnlySpan<float> gains = _balanceSides[channel] == BalanceSide.Left ? left : right;
                    Span<float> samples = output.GetChannelData(channel).Slice(offset, count);
                    for (int i = 0; i < count; i++)
                        samples[i] = Multiply(samples[i], gains[i]);
                }
            }
            else
            {
                Span<float> front = values.Slice(maxChunk * 2, count), side = values.Slice(maxChunk * 3, count);
                Span<float> back = values.Slice(maxChunk * 4, count), center = values.Slice(maxChunk * 5, count);
                Span<float> lfe = values.Slice(maxChunk * 6, count);
                Sample(SurroundPan, front);
                Sample(CenterLevel, center);
                Sample(LfeLevel, lfe);
                for (int i = 0; i < count; i++)
                {
                    float x = Math.Clamp(left[i] / 100f, -1, 1);
                    left[i] = Math.Min(1, 1 - x);
                    right[i] = Math.Min(1, 1 + x);
                    (front[i], side[i], back[i]) = GetSurroundWeights(Math.Clamp(front[i] / 100f, 0, 1), surround71);
                    center[i] = Math.Clamp(center[i] / 100f, 0, 1) * (1 - Math.Abs(x));
                    lfe[i] = Math.Clamp(lfe[i] / 100f, 0, 1);
                }
                ApplySurround(input, output, offset, left, right, front, side, back, center, lfe);
            }

            void Sample(IProperty<float> property, Span<float> destination)
            {
                if (property.GetKeyFrameAnimation() is null)
                    destination.Fill(property.Value);
                else
                    context.AnimationSampler.SampleBuffer(property, context.GetChunkRange(offset, count),
                        context.SampleRate, destination);
            }
        }
    }

    private void ApplySurround(AudioBuffer input, AudioBuffer output, int offset,
        ReadOnlySpan<float> left, ReadOnlySpan<float> right, ReadOnlySpan<float> front,
        ReadOnlySpan<float> side, ReadOnlySpan<float> back, ReadOnlySpan<float> center, ReadOnlySpan<float> lfe)
    {
        int count = left.Length;
        bool mono = input.ChannelCount == 1, surround71 = OutputLayout == AudioChannelLayout.Surround71;
        ReadOnlySpan<float> sourceLeft = input.GetChannelData(0).Slice(offset, count);
        ReadOnlySpan<float> sourceRight = mono ? sourceLeft : input.GetChannelData(1).Slice(offset, count);
        Span<float> frontLeft = output.GetChannelData(0).Slice(offset, count), frontRight = output.GetChannelData(1).Slice(offset, count);
        Span<float> centerChannel = output.GetChannelData(2).Slice(offset, count), lfeChannel = output.GetChannelData(3).Slice(offset, count);
        Span<float> backLeft = output.GetChannelData(4).Slice(offset, count), backRight = output.GetChannelData(5).Slice(offset, count);
        Span<float> sideLeft = surround71 ? output.GetChannelData(6).Slice(offset, count) : default;
        Span<float> sideRight = surround71 ? output.GetChannelData(7).Slice(offset, count) : default;
        for (int i = 0; i < count; i++)
        {
            float a = sourceLeft[i], b = sourceRight[i];
            float low = _lfeFilter.Process(mono ? a : (a + b) / 2);
            // Keep the balance, front weight and center share as separate multiplies for exact rounding.
            float balancedLeft = Multiply(a, left[i]), balancedRight = Multiply(b, right[i]);
            float lf = Multiply(balancedLeft, front[i]), rf = Multiply(balancedRight, front[i]);
            float cs = center[i];
            frontLeft[i] = Multiply(lf, 1 - cs);
            frontRight[i] = Multiply(rf, 1 - cs);
            centerChannel[i] = cs == 0 ? 0 : cs * (lf + rf) / 2;
            lfeChannel[i] = Multiply(low, lfe[i]);
            backLeft[i] = Multiply(balancedLeft, back[i]);
            backRight[i] = Multiply(balancedRight, back[i]);
            if (surround71)
            {
                sideLeft[i] = Multiply(balancedLeft, side[i]);
                sideRight[i] = Multiply(balancedRight, side[i]);
            }
        }
    }

    private static (float Front, float Side, float Back) GetSurroundWeights(float y, bool surround71)
    {
        if (!surround71)
        {
            var (front, back) = Crossfade(y);
            return (front, 0, back);
        }
        if (y <= 0.5f)
        {
            var (front, side) = Crossfade(y * 2);
            return (front, side, 0);
        }
        else
        {
            var (side, back) = Crossfade(y * 2 - 1);
            return (0, side, back);
        }
    }

    private static (float Front, float Back) Crossfade(float y) => y switch
    {
        <= 0 => (1, 0),
        >= 1 => (0, 1),
        _ => (MathF.Cos(y * MathF.PI / 2), MathF.Sin(y * MathF.PI / 2))
    };

    private static float Multiply(float value, float gain) => gain == 0 ? 0 : gain == 1 ? value : value * gain;

    private static void Scale(Span<float> samples, float gain)
    {
        if (gain == 1) return;
        if (gain == 0) samples.Clear();
        else for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
    }
}

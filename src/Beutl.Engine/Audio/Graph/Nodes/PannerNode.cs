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
                    Apply(input, output, 0, input.SampleCount, Pan.Value, SurroundPan.Value, CenterLevel.Value, LfeLevel.Value, position);
                }
                else
                {
                    const int maxChunk = 2048;
                    Span<float> values = stackalloc float[maxChunk * 4];
                    for (int offset = 0; offset < input.SampleCount; offset += maxChunk)
                    {
                        int count = Math.Min(maxChunk, input.SampleCount - offset);
                        Span<float> pan = values[..count];
                        Span<float> surround = values.Slice(maxChunk, count);
                        Span<float> center = values.Slice(maxChunk * 2, count);
                        Span<float> lfe = values.Slice(maxChunk * 3, count);
                        Sample(Pan, pan);
                        if (position)
                        {
                            Sample(SurroundPan, surround);
                            Sample(CenterLevel, center);
                            Sample(LfeLevel, lfe);
                        }
                        for (int i = 0; i < count; i++)
                            Apply(input, output, offset + i, 1, pan[i], position ? surround[i] : 0,
                                position ? center[i] : 0, position ? lfe[i] : 0, position);

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

    private void Apply(AudioBuffer input, AudioBuffer output, int offset, int count,
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
                float gain = OutputLayout.GetSpeaker(channel) switch
                {
                    FrontLeft or FrontLeftOfCenter or SideLeft or BackLeft or TopFrontLeft or TopBackLeft => left,
                    FrontRight or FrontRightOfCenter or SideRight or BackRight or TopFrontRight or TopBackRight => right,
                    _ => 1
                };
                Scale(output.GetChannelData(channel).Slice(offset, count), gain);
            }
            return;
        }

        float y = Math.Clamp(surround / 100f, 0, 1);
        float cs = Math.Clamp(center / 100f, 0, 1) * (1 - Math.Abs(x));
        float l = Math.Clamp(lfe / 100f, 0, 1);
        float front, side = 0, back;
        if (OutputLayout == AudioChannelLayout.Surround71)
        {
            if (y <= 0.5f)
            {
                (front, side) = Crossfade(y * 2);
                back = 0;
            }
            else
            {
                front = 0;
                (side, back) = Crossfade(y * 2 - 1);
            }
        }
        else
        {
            (front, back) = Crossfade(y);
        }

        ReadOnlySpan<float> sourceLeft = input.GetChannelData(0).Slice(offset, count);
        ReadOnlySpan<float> sourceRight = input.GetChannelData(input.ChannelCount == 1 ? 0 : 1).Slice(offset, count);
        // The default matrix has already copied FL/FR exactly, including signed zeros.
        bool defaults = x == 0 && y == 0 && cs == 0 && l == 0;
        for (int i = 0; i < count; i++)
        {
            float a = sourceLeft[i], b = sourceRight[i];
            float low = _lfeFilter.Process(input.ChannelCount == 1 ? a : (a + b) / 2);
            if (defaults)
                continue;
            float balancedLeft = Multiply(a, left), balancedRight = Multiply(b, right);
            float lf = Multiply(balancedLeft, front), rf = Multiply(balancedRight, front);
            output.GetChannelData(0)[offset + i] = Multiply(lf, 1 - cs);
            output.GetChannelData(1)[offset + i] = Multiply(rf, 1 - cs);
            output.GetChannelData(2)[offset + i] = cs == 0 ? 0 : cs * (lf + rf) / 2;
            output.GetChannelData(3)[offset + i] = Multiply(low, l);
            output.GetChannelData(4)[offset + i] = Multiply(balancedLeft, back);
            output.GetChannelData(5)[offset + i] = Multiply(balancedRight, back);
            if (OutputLayout == AudioChannelLayout.Surround71)
            {
                output.GetChannelData(6)[offset + i] = Multiply(balancedLeft, side);
                output.GetChannelData(7)[offset + i] = Multiply(balancedRight, side);
            }
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

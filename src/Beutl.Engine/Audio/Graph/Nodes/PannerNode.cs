using System.Runtime.CompilerServices;
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

    // The surround, center and LFE controls need the speakers of these layouts.
    internal static bool PositionsOnto(AudioChannelLayout output)
        => output == AudioChannelLayout.Surround51 || output == AudioChannelLayout.Surround71;

    // A clip of more than two channels keeps its own speaker positions, so the surround controls leave it alone.
    internal static bool IsPositionedClip(int clipChannels) => clipChannels <= 2;

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
                // One/two-channel clips feed the panner's L/R sides; larger layouts retain positions.
                AudioChannelLayout inputLayout = InputLayout.ChannelCount <= 2 && OutputLayout != AudioChannelLayout.Mono
                    ? AudioChannelLayout.GetDefault(InputLayout.ChannelCount) : InputLayout;
                _matrix = AudioChannelMatrix.Create(inputLayout, OutputLayout);
                _matrixInput = InputLayout;
                _matrixOutput = OutputLayout;
            }

            bool position = IsPositionedClip(input.ChannelCount) && PositionsOnto(OutputLayout);
            if (position)
            {
                if (_lastEnd is not { } end || !context.ContinuesFrom(end))
                    _lfeFilter.Reset();
                // Butterworth Q gives a flat passband with the specified 120 Hz cutoff.
                _lfeFilter.CalculateCoefficients(BiQuadFilterType.LowPass, 120f, 0.70710678f, 0, context.SampleRate);
            }

            bool animated = OutputLayout != AudioChannelLayout.Mono
                && (AnimationSampler.IsAnimated(Pan) || (position
                    && (AnimationSampler.IsAnimated(SurroundPan) || AnimationSampler.IsAnimated(CenterLevel)
                        || AnimationSampler.IsAnimated(LfeLevel))));
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
        if (!position)
        {
            var (_, left, right) = GetBalance(pan);
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

        bool surround71 = OutputLayout == AudioChannelLayout.Surround71;
        SurroundGains gains = GetSurroundGains(pan, surround, center, lfe, surround71);
        var writer = new SurroundWriter(input, output, 0, input.SampleCount, surround71, _lfeFilter);
        // The default matrix has already copied FL/FR exactly, including signed zeros.
        if (gains.Left == 1 && gains.Right == 1 && gains.Front == 1 && gains.Side == 0 && gains.Back == 0
            && gains.Center == 0 && gains.Lfe == 0)
        {
            for (int i = 0; i < input.SampleCount; i++)
                writer.AdvanceLfe(i);
            return;
        }

        for (int i = 0; i < input.SampleCount; i++)
            writer.Write(i, gains);
    }

    private void ApplyAnimated(AudioBuffer input, AudioBuffer output, AudioProcessContext context, bool position)
    {
        const int maxChunk = 2048;
        // One span per sampled property, keeping scratch space bounded by the chunk size. The balance-only path
        // reuses the one for Pan for its left gains.
        Span<float> values = stackalloc float[maxChunk * (position ? 4 : 2)];
        bool surround71 = OutputLayout == AudioChannelLayout.Surround71;
        for (int offset = 0; offset < input.SampleCount; offset += maxChunk)
        {
            int count = Math.Min(maxChunk, input.SampleCount - offset);
            var range = context.GetChunkRange(offset, count);
            Span<float> pan = values[..count];
            context.AnimationSampler.SampleBuffer(Pan, range, context.SampleRate, pan);
            if (!position)
            {
                Span<float> left = pan, right = values.Slice(maxChunk, count);
                for (int i = 0; i < count; i++)
                    (_, left[i], right[i]) = GetBalance(pan[i]);
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
                Span<float> surround = values.Slice(maxChunk, count);
                Span<float> center = values.Slice(maxChunk * 2, count), lfe = values.Slice(maxChunk * 3, count);
                context.AnimationSampler.SampleBuffer(SurroundPan, range, context.SampleRate, surround);
                context.AnimationSampler.SampleBuffer(CenterLevel, range, context.SampleRate, center);
                context.AnimationSampler.SampleBuffer(LfeLevel, range, context.SampleRate, lfe);
                var writer = new SurroundWriter(input, output, offset, count, surround71, _lfeFilter);
                for (int i = 0; i < count; i++)
                    writer.Write(i, GetSurroundGains(pan[i], surround[i], center[i], lfe[i], surround71));
            }
        }
    }

    // The balance position in [-1, 1] and the gain it gives each side.
    private static (float X, float Left, float Right) GetBalance(float pan)
    {
        float x = Math.Clamp(pan / 100f, -1, 1);
        return (x, Math.Min(1, 1 - x), Math.Min(1, 1 + x));
    }

    private static SurroundGains GetSurroundGains(float pan, float surround, float center, float lfe, bool surround71)
    {
        var (x, left, right) = GetBalance(pan);
        var (front, side, back) = GetSurroundWeights(Math.Clamp(surround / 100f, 0, 1), surround71);
        return new SurroundGains(left, right, front, side, back,
            Math.Clamp(center / 100f, 0, 1) * (1 - Math.Abs(x)), Math.Clamp(lfe / 100f, 0, 1));
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
        if (gain == 0) samples.Clear();
        else for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
    }

    private readonly record struct SurroundGains(float Left, float Right, float Front, float Side, float Back, float Center, float Lfe);

    // One run of frames of a positioned clip, written with fixed or per-frame gains alike.
    private readonly ref struct SurroundWriter
    {
        private readonly ReadOnlySpan<float> _sourceLeft;
        private readonly ReadOnlySpan<float> _sourceRight;
        private readonly Span<float> _frontLeft;
        private readonly Span<float> _frontRight;
        private readonly Span<float> _centerChannel;
        private readonly Span<float> _lfeChannel;
        private readonly Span<float> _backLeft;
        private readonly Span<float> _backRight;
        private readonly Span<float> _sideLeft;
        private readonly Span<float> _sideRight;
        private readonly bool _mono;
        private readonly bool _surround71;
        private readonly BiQuadFilter _lfeFilter;

        public SurroundWriter(AudioBuffer input, AudioBuffer output, int offset, int count, bool surround71, BiQuadFilter lfeFilter)
        {
            _mono = input.ChannelCount == 1;
            _surround71 = surround71;
            _lfeFilter = lfeFilter;
            _sourceLeft = input.GetChannelData(0).Slice(offset, count);
            _sourceRight = _mono ? _sourceLeft : input.GetChannelData(1).Slice(offset, count);
            _frontLeft = output.GetChannelData(0).Slice(offset, count);
            _frontRight = output.GetChannelData(1).Slice(offset, count);
            _centerChannel = output.GetChannelData(2).Slice(offset, count);
            _lfeChannel = output.GetChannelData(3).Slice(offset, count);
            _backLeft = output.GetChannelData(4).Slice(offset, count);
            _backRight = output.GetChannelData(5).Slice(offset, count);
            _sideLeft = surround71 ? output.GetChannelData(6).Slice(offset, count) : default;
            _sideRight = surround71 ? output.GetChannelData(7).Slice(offset, count) : default;
        }

        // Keeps the filter's state for a frame whose output is left as it is.
        public void AdvanceLfe(int i)
            => _lfeFilter.Process(_mono ? _sourceLeft[i] : (_sourceLeft[i] + _sourceRight[i]) / 2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(int i, in SurroundGains g)
        {
            float a = _sourceLeft[i], b = _sourceRight[i];
            float low = _lfeFilter.Process(_mono ? a : (a + b) / 2);
            // Keep the balance, front weight and center share as separate multiplies for exact rounding.
            float balancedLeft = Multiply(a, g.Left), balancedRight = Multiply(b, g.Right);
            float lf = Multiply(balancedLeft, g.Front), rf = Multiply(balancedRight, g.Front);
            float cs = g.Center;
            _frontLeft[i] = Multiply(lf, 1 - cs);
            _frontRight[i] = Multiply(rf, 1 - cs);
            _centerChannel[i] = cs == 0 ? 0 : cs * (lf + rf) / 2;
            _lfeChannel[i] = Multiply(low, g.Lfe);
            _backLeft[i] = Multiply(balancedLeft, g.Back);
            _backRight[i] = Multiply(balancedRight, g.Back);
            if (_surround71)
            {
                _sideLeft[i] = Multiply(balancedLeft, g.Side);
                _sideRight[i] = Multiply(balancedRight, g.Side);
            }
        }
    }
}

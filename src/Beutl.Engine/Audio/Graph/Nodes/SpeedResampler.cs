using System.Runtime.CompilerServices;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// Streaming stereo windowed-sinc resampler whose source position and low-pass cutoff follow the speed of
/// each output sample. Input reads may be batched without quantizing the speed curve into blocks.
/// </summary>
internal sealed class SpeedResampler
{
    private const int Radius = 64;
    private const int Channels = 2;
    private const int TableResolution = 64;
    private static readonly float[] s_sinc = CreateTable(window: false);
    private static readonly float[] s_window = CreateTable(window: true);
    private static readonly float[] s_unityKernel = CreateKernel(1);

    private float[] _buffer = new float[Radius * Channels];
    private int _bufferedFrames;
    private double _position;
    private double _staticCutoff;
    private float[]? _staticKernel;

    public SpeedResampler() => Reset();

    public void Reset()
    {
        // Zero history before the initial source sample; retain both history and fractional phase
        // between ordinary calls, including transitions between animated and static tail speeds.
        _buffer.AsSpan(0, Radius * Channels).Clear();
        _bufferedFrames = Radius;
        _position = Radius;
    }

    public int Prepare(int outputFrames, double speed, out Span<float> input)
    {
        double cutoff = 1 / Math.Max(1, speed * 1.03);
        if (_staticKernel == null || cutoff != _staticCutoff)
        {
            _staticKernel = cutoff == 1 ? s_unityKernel : CreateKernel(cutoff);
            _staticCutoff = cutoff;
        }

        return Prepare(_position + outputFrames * speed, out input);
    }

    public int Prepare(ReadOnlySpan<double> speeds, out Span<float> input)
    {
        double endPosition = _position;
        foreach (double speed in speeds)
            endPosition += speed;
        return Prepare(endPosition, out input);
    }

    private int Prepare(double endPosition, out Span<float> input)
    {
        int requiredFrames = checked((int)Math.Ceiling(endPosition) + Radius + 1);
        int requestedFrames = Math.Max(0, requiredFrames - _bufferedFrames);
        int requiredLength = checked((_bufferedFrames + requestedFrames) * Channels);
        if (_buffer.Length < requiredLength)
            Array.Resize(ref _buffer, Math.Max(requiredLength, _buffer.Length * 2));

        input = _buffer.AsSpan(_bufferedFrames * Channels, requestedFrames * Channels);
        return requestedFrames;
    }

    public int Process(Span<float> output, int inputFrames, double speed)
        => Process(output, inputFrames, [], speed);

    public int Process(Span<float> output, int inputFrames, ReadOnlySpan<double> speeds)
        => Process(output, inputFrames, speeds, 0);

    private int Process(Span<float> output, int inputFrames, ReadOnlySpan<double> speeds, double staticSpeed)
    {
        _bufferedFrames += inputFrames;
        int outputFrames = output.Length / Channels;
        double position = _position;
        int produced = 0;
        while (produced < outputFrames && position < _bufferedFrames)
        {
            double speed = speeds.IsEmpty ? staticSpeed : speeds[produced];
            // The transition band stays below the output Nyquist frequency during acceleration.
            // This expression also keeps the cutoff continuous when the speed crosses unity.
            double cutoff = 1 / Math.Max(1, speed * 1.03);
            float[]? kernel = cutoff == 1 ? s_unityKernel : speeds.IsEmpty ? _staticKernel : null;
            if (kernel != null)
            {
                // A fixed cutoff can reuse a phase table even while the playback speed changes.
                // Keep constant/slow playback as inexpensive as a fixed-rate sinc resampler.
                SampleFixedKernel(output.Slice(produced * Channels, Channels), position, kernel);
                position += speed;
                produced++;
                continue;
            }

            int center = (int)position;
            double left = 0;
            double right = 0;
            double weightSum = 0;
            for (int tap = center - Radius + 1; tap <= center + Radius; tap++)
            {
                double distance = Math.Abs(tap - position);
                double weight = Lookup(s_sinc, distance * cutoff) * Lookup(s_window, distance);
                weightSum += weight;
                // A short source read is zero-padded for the filter, but no output is produced past
                // its last source sample. Never read stale data left in the reusable allocation.
                if ((uint)tap < (uint)_bufferedFrames)
                {
                    left += _buffer[tap * Channels] * weight;
                    right += _buffer[tap * Channels + 1] * weight;
                }
            }

            output[produced * Channels] = (float)(left / weightSum);
            output[produced * Channels + 1] = (float)(right / weightSum);

            position += speed;
            produced++;
        }

        int consumed = Math.Clamp((int)position - Radius, 0, _bufferedFrames);
        _bufferedFrames -= consumed;
        _buffer.AsSpan(consumed * Channels, _bufferedFrames * Channels).CopyTo(_buffer);
        _position = position - consumed;
        return produced;
    }

    private void SampleFixedKernel(Span<float> output, double position, float[] kernel)
    {
        int center = (int)position;
        double phase = (position - center) * TableResolution;
        int firstPhase = (int)phase;
        double fraction = phase - firstPhase;
        int first = firstPhase * Radius * 2;
        int next = first + Radius * 2;
        double left = 0;
        double right = 0;
        for (int i = 0; i < Radius * 2; i++)
        {
            int tap = center - Radius + 1 + i;
            if ((uint)tap < (uint)_bufferedFrames)
            {
                double weight = kernel[first + i] + (kernel[next + i] - kernel[first + i]) * fraction;
                left += _buffer[tap * Channels] * weight;
                right += _buffer[tap * Channels + 1] * weight;
            }
        }

        output[0] = (float)left;
        output[1] = (float)right;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Lookup(float[] table, double distance)
    {
        double position = distance * TableResolution;
        int index = Math.Min((int)position, table.Length - 2);
        double fraction = position - index;
        return table[index] + (table[index + 1] - table[index]) * fraction;
    }

    private static float[] CreateTable(bool window)
    {
        var table = new float[Radius * TableResolution + 1];
        for (int i = 0; i < table.Length; i++)
        {
            double x = Math.PI * i / TableResolution;
            if (window)
            {
                double phase = x / Radius;
                table[i] = (float)(0.35875 + 0.48829 * Math.Cos(phase)
                    + 0.14128 * Math.Cos(2 * phase) + 0.01168 * Math.Cos(3 * phase));
            }
            else
            {
                table[i] = i == 0 ? 1 : (float)(Math.Sin(x) / x);
            }
        }

        return table;
    }

    private static float[] CreateKernel(double cutoff)
    {
        var kernel = new float[(TableResolution + 1) * Radius * 2];
        for (int phase = 0; phase <= TableResolution; phase++)
        {
            int offset = phase * Radius * 2;
            double weightSum = 0;
            for (int i = 0; i < Radius * 2; i++)
            {
                double distance = Math.Abs(i - Radius + 1 - phase / (double)TableResolution);
                double weight = Lookup(s_sinc, distance * cutoff) * Lookup(s_window, distance);
                kernel[offset + i] = (float)weight;
                weightSum += weight;
            }

            for (int i = 0; i < Radius * 2; i++)
                kernel[offset + i] = (float)(kernel[offset + i] / weightSum);
        }

        return kernel;
    }
}

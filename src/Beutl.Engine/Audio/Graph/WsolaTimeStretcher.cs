using System.Numerics;

namespace Beutl.Audio.Graph;

// Waveform similarity overlap-add (WSOLA). Each grain plays at the original sample rate;
// tempo changes the distance between grains in the source. Aligning the overlap before
// crossfading avoids pitch changes and clicks. All channels share the same alignment.
internal sealed class WsolaTimeStretcher
{
    private readonly int _channels;
    private readonly int _hopFrames;
    private readonly int _searchFrames;
    private readonly float[] _tail;
    private readonly float[] _output;
    private float[] _input;
    private long _inputStart;
    private int _inputFrames;
    private int _outputPosition;
    private int _outputFrames;
    private long _receivedInputFrames;
    private double _sourcePosition;
    private double _sourcePositionCompensation;
    private double _tempo = 1;
    private bool _hasTail;
    private bool _finished;

    public WsolaTimeStretcher(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _channels = channels;
        _hopFrames = Math.Max(16, sampleRate / 50); // 40 ms grains, 20 ms overlaps.
        _searchFrames = Math.Max(1, sampleRate / 100); // Search within 10 ms of the desired position.
        _tail = new float[_hopFrames * channels];
        _output = new float[_hopFrames * channels];
        _input = new float[(_hopFrames * 2 + _searchFrames * 2 + 1024) * channels];
    }

    public double Tempo
    {
        get => _tempo;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Tempo must be positive and finite.");
            _tempo = value;
        }
    }

    public void Clear()
    {
        _inputStart = 0;
        _inputFrames = 0;
        _outputPosition = 0;
        _outputFrames = 0;
        _receivedInputFrames = 0;
        _sourcePosition = 0;
        _sourcePositionCompensation = 0;
        _hasTail = false;
        _finished = false;
    }

    public void PutSamples(ReadOnlySpan<float> input, int frames)
    {
        if (_finished)
            throw new InvalidOperationException("Cannot feed a finished time-stretch stream.");
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        int samples = checked(frames * _channels);
        if (input.Length < samples)
            throw new ArgumentException("Input does not contain the requested frames.", nameof(input));

        EnsureCapacity(checked(_inputFrames + frames));
        input[..samples].CopyTo(_input.AsSpan(_inputFrames * _channels, samples));
        _inputFrames += frames;
        _receivedInputFrames = checked(_receivedInputFrames + frames);
    }

    public void Flush() => _finished = true;

    public int ReceiveSamples(Span<float> output, int frames, ReadOnlySpan<double> speedCurve = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        if (output.Length < checked(frames * _channels))
            throw new ArgumentException("Output does not have space for the requested frames.", nameof(output));
        if (!speedCurve.IsEmpty && speedCurve.Length < frames)
            throw new ArgumentException("Speed curve does not contain the requested frames.", nameof(speedCurve));

        int written = 0;
        while (written < frames)
        {
            if (_sourcePosition >= _receivedInputFrames - 1e-9)
                break;
            if (_outputPosition == _outputFrames && !GenerateGrain())
                break;

            int count = Math.Min(frames - written, _outputFrames - _outputPosition);
            double position = _sourcePosition;
            double compensation = _sourcePositionCompensation;
            int available = 0;
            for (; available < count; available++)
            {
                if (position >= _receivedInputFrames - 1e-9)
                    break;
                double speed = speedCurve.IsEmpty ? _tempo : speedCurve[written + available];
                if (!double.IsFinite(speed) || speed <= 0)
                    throw new ArgumentException("Speed must be positive and finite.", nameof(speedCurve));

                // Integrate the speed of every consumed output sample. Compensated summation keeps
                // the same cursor when callers split the output into differently sized chunks.
                double adjusted = speed - compensation;
                double next = position + adjusted;
                if (!_finished && next > _receivedInputFrames + 1e-9)
                    break;
                compensation = (next - position) - adjusted;
                position = next;
            }
            if (available == 0)
                break;

            // Before EOF, wait for enough real input to cover the source-domain advance. At EOF,
            // allow only the last fractional step, matching ceil(input length / static tempo).
            count = available;
            _output.AsSpan(_outputPosition * _channels, count * _channels)
                .CopyTo(output.Slice(written * _channels, count * _channels));
            _outputPosition += count;
            written += count;
            _sourcePosition = position;
            _sourcePositionCompensation = compensation;
        }
        return written;
    }

    private bool GenerateGrain()
    {
        long target = checked((long)Math.Round(_sourcePosition));
        long first = _hasTail ? Math.Max(0, target - _searchFrames) : 0;
        long last = _hasTail ? target + _searchFrames : 0;
        DiscardBefore(first);

        long requiredEnd = last + _hopFrames * 2;
        long bufferedEnd = _inputStart + _inputFrames;
        if (requiredEnd > bufferedEnd)
        {
            if (!_finished)
                return false;

            // Finishing only pads the analysis window. Padding must not increase the real-input
            // budget that ReceiveSamples uses to limit the emitted output.
            int padding = checked((int)(requiredEnd - bufferedEnd));
            EnsureCapacity(checked(_inputFrames + padding));
            _input.AsSpan(_inputFrames * _channels, padding * _channels).Clear();
            _inputFrames += padding;
        }

        long position = _hasTail ? FindAlignment(first, last, target) : 0;
        int offset = checked((int)(position - _inputStart) * _channels);
        ReadOnlySpan<float> grain = _input.AsSpan(offset, _hopFrames * 2 * _channels);
        if (!_hasTail)
        {
            grain[.._output.Length].CopyTo(_output);
        }
        else
        {
            for (int frame = 0; frame < _hopFrames; frame++)
            {
                float fadeIn = frame / (float)_hopFrames;
                float fadeOut = 1 - fadeIn;
                int index = frame * _channels;
                for (int channel = 0; channel < _channels; channel++)
                    _output[index + channel] = _tail[index + channel] * fadeOut + grain[index + channel] * fadeIn;
            }
        }
        grain.Slice(_output.Length, _tail.Length).CopyTo(_tail);
        _hasTail = true;
        _outputPosition = 0;
        _outputFrames = _hopFrames;
        return true;
    }

    private long FindAlignment(long first, long last, long target)
    {
        int length = _tail.Length;
        double referenceEnergy = Dot(_tail, _tail);
        if (referenceEnergy < 1e-12)
            return Math.Clamp(target, first, last);

        int offset = checked((int)(first - _inputStart) * _channels);
        double energy = Dot(_input.AsSpan(offset, length), _input.AsSpan(offset, length));
        double bestScore = double.NegativeInfinity;
        long bestPosition = Math.Clamp(target, first, last);
        for (long position = first; position <= last; position++, offset += _channels)
        {
            double score = energy > 1e-12
                ? Dot(_tail, _input.AsSpan(offset, length)) / Math.Sqrt(referenceEnergy * energy)
                : 0;
            // Prefer the nearest equally good match to avoid drifting away from the tempo map.
            if (score > bestScore + 1e-6
                || (Math.Abs(score - bestScore) <= 1e-6 && Math.Abs(position - target) < Math.Abs(bestPosition - target)))
            {
                bestScore = score;
                bestPosition = position;
            }

            if (position < last)
            {
                // The correlation windows advance by one frame. Update their energy in O(channels).
                for (int channel = 0; channel < _channels; channel++)
                {
                    float removed = _input[offset + channel];
                    float added = _input[offset + length + channel];
                    energy += (double)added * added - (double)removed * removed;
                }
                energy = Math.Max(0, energy);
            }
        }
        return bestPosition;
    }

    private static double Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var sum = Vector<float>.Zero;
        int width = Vector<float>.Count;
        int index = 0;
        for (; index <= left.Length - width; index += width)
            sum += new Vector<float>(left.Slice(index, width)) * new Vector<float>(right.Slice(index, width));

        double result = Vector.Sum(sum);
        for (; index < left.Length; index++)
            result += (double)left[index] * right[index];
        return result;
    }

    private void DiscardBefore(long frame)
    {
        int count = (int)Math.Min(_inputFrames, Math.Max(0, frame - _inputStart));
        if (count == 0)
            return;
        int remaining = _inputFrames - count;
        _input.AsSpan(count * _channels, remaining * _channels).CopyTo(_input);
        _inputStart += count;
        _inputFrames = remaining;
    }

    private void EnsureCapacity(int frames)
    {
        int samples = checked(frames * _channels);
        if (samples > _input.Length)
            Array.Resize(ref _input, Math.Max(samples, checked(_input.Length * 2)));
    }
}

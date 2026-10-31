using static Beutl.Audio.AudioSpeakers;

namespace Beutl.Audio;

/// <summary>A channel conversion without normalization or clipping, using ITU surround fold coefficients.</summary>
public sealed class AudioChannelMatrix
{
    private const float SurroundGain = 0.70710677f;
    private readonly float[,] _coefficients;
    private readonly int[] _copyInputs;

    private AudioChannelMatrix(float[,] coefficients)
    {
        _coefficients = coefficients;
        OutputChannelCount = coefficients.GetLength(0);
        InputChannelCount = coefficients.GetLength(1);
        _copyInputs = new int[OutputChannelCount];
        IsIdentity = InputChannelCount == OutputChannelCount;
        for (int output = 0; output < OutputChannelCount; output++)
        {
            int nonzeroCount = 0;
            int copyInput = -1;
            for (int input = 0; input < InputChannelCount; input++)
            {
                if (coefficients[output, input] == 0)
                    continue;
                nonzeroCount++;
                if (coefficients[output, input] == 1)
                    copyInput = input;
            }

            _copyInputs[output] = nonzeroCount == 1 ? copyInput : -1;
            IsIdentity &= _copyInputs[output] == output;
        }
    }

    public int InputChannelCount { get; }

    public int OutputChannelCount { get; }

    public bool IsIdentity { get; }

    public float GetCoefficient(int output, int input) => _coefficients[output, input];

    /// <summary>
    /// Equal layouts preserve every channel. Otherwise, discrete layouts use Beutl's default order,
    /// with only FL/FR in the first two channels when no default positions are known.
    /// </summary>
    public static AudioChannelMatrix Create(AudioChannelLayout input, AudioChannelLayout output)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.ChannelCount, nameof(input));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(output.ChannelCount, nameof(output));
        var coefficients = new float[output.ChannelCount, input.ChannelCount];
        if (input == output)
        {
            for (int i = 0; i < input.ChannelCount; i++)
                coefficients[i, i] = 1;
            return new AudioChannelMatrix(coefficients);
        }

        if (!input.HasPositions)
            input = AudioChannelLayout.GetDefault(input.ChannelCount);
        if (!output.HasPositions)
            output = AudioChannelLayout.GetDefault(output.ChannelCount);

        if (output == AudioChannelLayout.Mono && input.ChannelCount > 1)
        {
            var stereo = Create(input, AudioChannelLayout.Stereo);
            for (int i = 0; i < input.ChannelCount; i++)
                coefficients[0, i] = 0.5f * stereo.GetCoefficient(0, i) + 0.5f * stereo.GetCoefficient(1, i);
            return new AudioChannelMatrix(coefficients);
        }

        static bool HasPair(AudioChannelLayout layout, AudioSpeakers left, AudioSpeakers right)
            => IndexOf(layout, left) >= 0 && IndexOf(layout, right) >= 0;

        bool Add(int channel, AudioSpeakers speaker, float gain)
        {
            int index = IndexOf(output, speaker);
            if (index < 0)
                return false;
            coefficients[index, channel] += gain;
            return true;
        }

        void AddPair(int channel, AudioSpeakers left, AudioSpeakers right, float gain)
        {
            Add(channel, left, gain);
            Add(channel, right, gain);
        }

        if (input.ChannelCount == 1)
        {
            if (HasPair(output, FrontLeft, FrontRight))
                AddPair(0, FrontLeft, FrontRight, 1);
            else
                foreach (AudioSpeakers speaker in new[] { FrontLeft, FrontRight, FrontCenter, FrontLeftOfCenter, FrontRightOfCenter })
                    Add(0, speaker, 1);
            return new AudioChannelMatrix(coefficients);
        }

        bool outputBack = HasPair(output, BackLeft, BackRight);
        bool outputSide = HasPair(output, SideLeft, SideRight);
        bool inputBack = HasPair(input, BackLeft, BackRight);
        bool inputSide = HasPair(input, SideLeft, SideRight);
        for (int i = 0; i < input.ChannelCount; i++)
        {
            AudioSpeakers speaker = input.HasPositions ? input.GetSpeaker(i) : i switch
            {
                0 => FrontLeft,
                1 => FrontRight,
                _ => None
            };
            if (speaker == None || Add(i, speaker, 1))
                continue;

            switch (speaker)
            {
                case SideLeft or SideRight when !outputSide && outputBack:
                    Add(i, speaker == SideLeft ? BackLeft : BackRight, inputBack ? SurroundGain : 1);
                    break;
                case BackLeft or BackRight when !outputBack && outputSide:
                    Add(i, speaker == BackLeft ? SideLeft : SideRight, inputSide ? SurroundGain : 1);
                    break;
                case BackCenter:
                    if (outputBack)
                        AddPair(i, BackLeft, BackRight, SurroundGain);
                    else if (outputSide)
                        AddPair(i, SideLeft, SideRight, SurroundGain);
                    else
                        AddPair(i, FrontLeft, FrontRight, 0.5f);
                    break;
                case FrontCenter:
                    AddPair(i, FrontLeft, FrontRight, SurroundGain);
                    break;
                case BackLeft or SideLeft:
                    Add(i, FrontLeft, SurroundGain);
                    break;
                case BackRight or SideRight:
                    Add(i, FrontRight, SurroundGain);
                    break;
                case FrontLeftOfCenter or FrontRightOfCenter:
                    if (!Add(i, speaker == FrontLeftOfCenter ? FrontLeft : FrontRight, 1)
                        && IndexOf(output, FrontLeft) < 0 && IndexOf(output, FrontRight) < 0)
                        Add(i, FrontCenter, SurroundGain);
                    break;
                case TopFrontLeft:
                    Add(i, FrontLeft, SurroundGain);
                    break;
                case TopFrontRight:
                    Add(i, FrontRight, SurroundGain);
                    break;
                case TopCenter or TopFrontCenter:
                    if (!Add(i, FrontCenter, SurroundGain))
                        AddPair(i, FrontLeft, FrontRight, 0.5f);
                    break;
                // Rear height channels use the nearest available horizontal position.
                case TopBackLeft:
                    if (!Add(i, BackLeft, SurroundGain) && !Add(i, SideLeft, SurroundGain))
                        Add(i, FrontLeft, SurroundGain);
                    break;
                case TopBackRight:
                    if (!Add(i, BackRight, SurroundGain) && !Add(i, SideRight, SurroundGain))
                        Add(i, FrontRight, SurroundGain);
                    break;
                case TopBackCenter:
                    // Like the front height center, split at 0.5 when the horizontal center is absent.
                    if (!Add(i, BackCenter, SurroundGain))
                    {
                        if (outputBack)
                            AddPair(i, BackLeft, BackRight, 0.5f);
                        else if (outputSide)
                            AddPair(i, SideLeft, SideRight, 0.5f);
                        else
                            AddPair(i, FrontLeft, FrontRight, 0.5f);
                    }
                    break;
            }
        }

        return new AudioChannelMatrix(coefficients);
    }

    private static int IndexOf(AudioChannelLayout layout, AudioSpeakers speaker)
        => layout.HasPositions ? layout.IndexOf(speaker) : speaker switch
        {
            FrontLeft => 0,
            FrontRight => 1,
            _ => -1
        };

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var output = new AudioBuffer(input.SampleRate, OutputChannelCount, input.SampleCount);
        try
        {
            Apply(input, output);
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public void Apply(AudioBuffer input, AudioBuffer output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (input.ChannelCount != InputChannelCount)
            throw new ArgumentException("Input channel count does not match the matrix.", nameof(input));
        if (output.ChannelCount != OutputChannelCount || output.SampleRate != input.SampleRate || output.SampleCount != input.SampleCount)
            throw new ArgumentException("Output format and length must match the conversion.", nameof(output));

        // Mixing into the input could overwrite a channel still needed by a later row.
        if (ReferenceEquals(input, output) && !IsIdentity)
        {
            using var converted = Apply(input);
            converted.CopyTo(output);
            return;
        }

        for (int row = 0; row < OutputChannelCount; row++)
        {
            Span<float> destination = output.GetChannelData(row);
            if (_copyInputs[row] >= 0)
            {
                input.GetChannelData(_copyInputs[row]).CopyTo(destination);
                continue;
            }

            destination.Clear();
            for (int channel = 0; channel < InputChannelCount; channel++)
            {
                float gain = _coefficients[row, channel];
                if (gain == 0)
                    continue;
                Span<float> source = input.GetChannelData(channel);
                for (int i = 0; i < source.Length; i++)
                    destination[i] += source[i] * gain;
            }
        }
    }
}

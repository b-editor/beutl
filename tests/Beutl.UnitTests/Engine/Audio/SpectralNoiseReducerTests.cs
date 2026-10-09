using Beutl.Audio.Graph;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SpectralNoiseReducerTests
{
    private const int SampleRate = 48000;

    private static readonly NoiseReductionSettings s_default = new(12, 3, 50, 2);

    [TestCase(8000, 256)]
    [TestCase(16000, 512)]
    [TestCase(44100, 1024)]
    [TestCase(48000, 1024)]
    [TestCase(96000, 2048)]
    [TestCase(192000, 4096)]
    public void GetFrameSize_CoversAtLeastTwentyMillisecondsWithAPowerOfTwo(int sampleRate, int expected)
    {
        Assert.That(SpectralNoiseReducer.GetFrameSize(sampleRate), Is.EqualTo(expected));
    }

    [Test]
    public void ZeroReduction_ReproducesTheInputExactly()
    {
        float[] left = Noise(SampleRate * 2, 0.1, 1);
        float[] right = Noise(SampleRate * 2, 0.1, 2);

        var (outLeft, outRight) = Run(left, right, s_default with { ReductionDb = 0 });

        Assert.That(outLeft, Is.EqualTo(left));
        Assert.That(outRight, Is.EqualTo(right));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NoiseEstimate_MatchesTheMeanNoisePower(bool identicalChannels)
    {
        const double sigma = 0.05;
        float[] left = Noise(SampleRate * 6, sigma, 3);
        float[] right = identicalChannels ? left : Noise(SampleRate * 6, sigma, 4);
        var reducer = new SpectralNoiseReducer(SampleRate);

        Run(reducer, left, right, s_default, SampleRate);

        // White noise puts sigma^2 times the window energy (3N/8 for Hann) into every bin.
        double expected = sigma * sigma * reducer.FrameSize * 3 / 8;
        ReadOnlySpan<float> noise = reducer.NoiseEstimate;
        double mean = 0;
        for (int k = 8; k < noise.Length - 8; k++)
            mean += noise[k];
        mean /= noise.Length - 16;
        Assert.That(10 * Math.Log10(mean / expected), Is.EqualTo(0).Within(1));
    }

    [TestCase(6f)]
    [TestCase(12f)]
    [TestCase(24f)]
    public void StationaryNoise_IsAttenuatedByTheReduction(float reduction)
    {
        float[] noise = Noise(SampleRate * 4, 0.05, 5);

        var (output, _) = Run(noise, noise, s_default with { ReductionDb = reduction, SensitivityDb = 6 });

        Assert.That(Decibels(Power(output, SampleRate, SampleRate * 4) / Power(noise, SampleRate, SampleRate * 4)),
            Is.EqualTo(-reduction).Within(0.5));
    }

    [Test]
    public void ToneBursts_SurviveWhileTheNoiseBetweenThemIsRemoved()
    {
        float[] noise = Noise(SampleRate * 6, 0.02, 6);
        float[] bursts = ToneBursts(SampleRate * 6, 0.3f);
        float[] input = Add(noise, bursts);

        var (output, _) = Run(input, input, s_default);

        double burstGain = 0;
        double gapPower = 0;
        double gapInput = 0;
        for (int burst = 2; burst < 12; burst++)
        {
            // Bursts last 200 ms of every 500 ms; compare their middles and the middles of the gaps.
            int start = burst * SampleRate / 2;
            burstGain += ToneAmplitude(output, start + SampleRate / 20, SampleRate / 10)
                         / ToneAmplitude(input, start + SampleRate / 20, SampleRate / 10);
            gapPower += Power(output, start + SampleRate * 3 / 10, start + SampleRate * 9 / 20);
            gapInput += Power(input, start + SampleRate * 3 / 10, start + SampleRate * 9 / 20);
        }

        Assert.That(20 * Math.Log10(burstGain / 10), Is.GreaterThan(-1));
        Assert.That(Decibels(gapPower / gapInput), Is.LessThan(-10));
    }

    [TestCase(1)]
    [TestCase(37)]
    [TestCase(480)]
    [TestCase(4800)]
    public void Output_DoesNotDependOnHowItIsRead(int chunk)
    {
        float[] input = Add(Noise(SampleRate * 2, 0.05, 7), ToneBursts(SampleRate * 2, 0.2f));

        var (whole, _) = Run(input, input, s_default, SampleRate * 2);
        var (chunked, _) = Run(input, input, s_default, chunk);

        Assert.That(chunked, Is.EqualTo(whole));
    }

    [Test]
    public void Gains_AreSharedByBothChannels()
    {
        float[] left = Add(Noise(SampleRate * 2, 0.05, 8), ToneBursts(SampleRate * 2, 0.2f));
        float[] right = left.Select(value => -0.5f * value).ToArray();

        var (outLeft, outRight) = Run(left, right, s_default);

        // A shared gain scales both channels alike, so the stereo image does not move.
        Assert.That(outRight, Is.EqualTo(outLeft.Select(value => -0.5f * value)).Within(1e-5f));
        Assert.That(Power(outLeft, SampleRate, SampleRate * 2), Is.LessThan(Power(left, SampleRate, SampleRate * 2)));
    }

    [Test]
    public void NonFiniteInput_IsTreatedAsSilence()
    {
        float[] clean = Noise(SampleRate, 0.05, 9);
        float[] corrupt = (float[])clean.Clone();
        float[] zeroed = (float[])clean.Clone();
        foreach (int index in new[] { 100, 5000, 20000 })
        {
            corrupt[index] = float.NaN;
            corrupt[index + 1] = float.PositiveInfinity;
            zeroed[index] = 0;
            zeroed[index + 1] = 0;
        }

        var (fromCorrupt, _) = Run(corrupt, corrupt, s_default);
        var (fromZeroed, _) = Run(zeroed, zeroed, s_default);

        Assert.That(fromCorrupt, Is.All.Matches<float>(float.IsFinite));
        Assert.That(fromCorrupt, Is.EqualTo(fromZeroed));
    }

    [Test]
    public void Seed_ReducesNoiseFromTheFirstSample()
    {
        float[] noise = Noise(SampleRate * 2, 0.05, 10);

        var (seeded, _) = Run(noise, noise, s_default, seed: true);
        var (unseeded, _) = Run(noise, noise, s_default, seed: false);

        int head = SampleRate / 20;
        Assert.That(Decibels(Power(seeded, 0, head) / Power(noise, 0, head)), Is.LessThan(-10));

        // Without a seed the floor is unknown until the smoothed power settles, and unknown noise passes.
        Assert.That(Decibels(Power(unseeded, 0, head) / Power(noise, 0, head)), Is.GreaterThan(-1));
    }

    [Test]
    public void Settings_AreEvaluatedPerFrame()
    {
        float[] noise = Noise(SampleRate * 3, 0.05, 11);
        int change = SampleRate * 3 / 2;
        var reducer = new SpectralNoiseReducer(SampleRate);
        var positions = new List<long>();

        var (output, _) = Run(reducer, noise, noise, s_default, SampleRate / 10, position =>
        {
            positions.Add(position);
            return s_default with { ReductionDb = position < change ? 0 : 24 };
        });

        // Frames centered before the change pass everything; their reach ends one frame earlier.
        int frame = reducer.FrameSize;
        Assert.That(output.AsSpan(0, change - frame).ToArray(), Is.EqualTo(noise.AsSpan(0, change - frame).ToArray()));
        Assert.That(Decibels(Power(output, change + frame, noise.Length) / Power(noise, change + frame, noise.Length)),
            Is.LessThan(-20));
        Assert.That(positions, Is.Ordered.Ascending);
        Assert.That(positions.Zip(positions.Skip(1), (a, b) => b - a), Is.All.EqualTo(reducer.HopSize));
    }

    [TestCase(0.5f, 10f)]
    [TestCase(10f, 0.5f)]
    public void ChangingTheAdaptationTime_KeepsTheNoiseFloorCalibrated(float before, float after)
    {
        // Twelve seconds fill even the ten-second window before the change.
        float[] noise = Noise(SampleRate * 13, 0.05, 12);
        var reducer = new SpectralNoiseReducer(SampleRate);
        long change = SampleRate * 12 / reducer.HopSize * reducer.HopSize;
        Func<long, NoiseReductionSettings> settingsAt = center => s_default with
        {
            AdaptationSeconds = center - reducer.FrameSize / 2 < change ? before : after
        };
        reducer.Reset(0, 0);
        reducer.Write(noise.SelectMany(sample => new[] { sample, sample }).ToArray());
        reducer.WritePadding(reducer.FrameSize * 2);
        reducer.Seed(SampleRate);

        // Reading up to the change processes every frame before it; one more hop processes the first
        // frame with the new adaptation time and adds a single frame of evidence.
        reducer.Read(new float[change], new float[change], settingsAt);
        double beforeChange = MeanNoise(reducer);
        reducer.Read(new float[reducer.HopSize], new float[reducer.HopSize], settingsAt);
        double afterChange = MeanNoise(reducer);

        Assert.That(Decibels(afterChange / beforeChange), Is.EqualTo(0).Within(0.25));
    }

    [Test]
    public void Read_ThrowsWithoutEnoughInput()
    {
        var reducer = new SpectralNoiseReducer(SampleRate);
        reducer.Reset(0, 0);
        reducer.Write(new float[SampleRate * 2]);
        var output = new float[SampleRate];

        long required = reducer.GetRequiredInputEnd(SampleRate);
        Assert.That(required, Is.GreaterThan(SampleRate));
        Assert.That(() => reducer.Read(output, new float[SampleRate], _ => s_default), Throws.InvalidOperationException);
    }

    [Test]
    public void Write_AfterPadding_Throws()
    {
        var reducer = new SpectralNoiseReducer(SampleRate);
        reducer.Reset(0, 0);
        reducer.WritePadding(10);

        Assert.That(() => reducer.Write(new float[2]), Throws.InvalidOperationException);
    }

    [Test]
    public void Reset_RejectsAnOriginOffTheHopGrid()
    {
        var reducer = new SpectralNoiseReducer(SampleRate);

        Assert.That(() => reducer.Reset(1, 1), Throws.ArgumentException);
        Assert.That(() => reducer.Reset(-reducer.HopSize, 0), Throws.Nothing);
        Assert.That(() => reducer.Reset(0, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void MinimumBias_GrowsWithTheWindowLength()
    {
        Assert.That(SpectralNoiseReducer.GetMinimumBias(1), Is.EqualTo(1));
        float previous = 1;
        foreach (int frames in new[] { 2, 3, 10, 100, 375, 1000, 4096, 8192 })
        {
            float bias = SpectralNoiseReducer.GetMinimumBias(frames);
            Assert.That(bias, Is.GreaterThan(previous), $"frames={frames}");
            previous = bias;
        }

        // A two-second window at 48 kHz spans 375 frames.
        Assert.That(SpectralNoiseReducer.GetMinimumBias(375), Is.EqualTo(2.19f).Within(0.02f));
    }

    private static (float[] Left, float[] Right) Run(
        float[] left, float[] right, NoiseReductionSettings settings, int chunk = 4800, bool seed = true)
        => Run(new SpectralNoiseReducer(SampleRate), left, right, settings, chunk, _ => settings, seed);

    private static (float[] Left, float[] Right) Run(
        SpectralNoiseReducer reducer, float[] left, float[] right, NoiseReductionSettings settings, int chunk)
        => Run(reducer, left, right, settings, chunk, _ => settings);

    private static (float[] Left, float[] Right) Run(
        SpectralNoiseReducer reducer,
        float[] left,
        float[] right,
        NoiseReductionSettings settings,
        int chunk,
        Func<long, NoiseReductionSettings> settingsAt,
        bool seed = true)
    {
        reducer.Reset(0, 0);
        var interleaved = new float[left.Length * 2];
        for (int i = 0; i < left.Length; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[i * 2 + 1] = right[i];
        }

        reducer.Write(interleaved);
        reducer.WritePadding(reducer.FrameSize * 2);
        if (seed)
            reducer.Seed(SampleRate);

        var outLeft = new float[left.Length];
        var outRight = new float[left.Length];
        for (int position = 0; position < left.Length; position += chunk)
        {
            int count = Math.Min(chunk, left.Length - position);
            reducer.Read(outLeft.AsSpan(position, count), outRight.AsSpan(position, count), settingsAt);
        }

        return (outLeft, outRight);
    }

    internal static float[] Noise(int count, double sigma, int seed)
    {
        var random = new Random(seed);
        var data = new float[count];
        for (int i = 0; i < count; i += 2)
        {
            double radius = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * sigma;
            double angle = 2 * Math.PI * random.NextDouble();
            data[i] = (float)(radius * Math.Cos(angle));
            if (i + 1 < count)
                data[i + 1] = (float)(radius * Math.Sin(angle));
        }

        return data;
    }

    // 1 kHz bursts of 200 ms every 500 ms with 10 ms ramps.
    internal static float[] ToneBursts(int count, float amplitude)
    {
        var data = new float[count];
        int period = SampleRate / 2;
        int length = SampleRate / 5;
        int ramp = SampleRate / 100;
        for (int i = 0; i < count; i++)
        {
            int offset = i % period;
            if (offset >= length)
                continue;

            float envelope = Math.Min(1f, Math.Min(offset, length - offset) / (float)ramp);
            data[i] = amplitude * envelope * MathF.Sin(2 * MathF.PI * 1000 * i / SampleRate);
        }

        return data;
    }

    private static double MeanNoise(SpectralNoiseReducer reducer)
    {
        ReadOnlySpan<float> noise = reducer.NoiseEstimate;
        double sum = 0;
        for (int k = 8; k < noise.Length - 8; k++)
            sum += noise[k];
        return sum / (noise.Length - 16);
    }

    internal static float[] Add(float[] first, float[] second)
        => first.Zip(second, (a, b) => a + b).ToArray();

    internal static double Power(float[] data, int start, int end)
    {
        double sum = 0;
        for (int i = start; i < end; i++)
            sum += data[i] * (double)data[i];
        return sum / (end - start);
    }

    internal static double Decibels(double ratio) => 10 * Math.Log10(ratio);

    private static double ToneAmplitude(float[] data, int start, int count)
    {
        double cos = 0;
        double sin = 0;
        for (int i = start; i < start + count; i++)
        {
            double phase = 2 * Math.PI * 1000 * i / SampleRate;
            cos += data[i] * Math.Cos(phase);
            sin += data[i] * Math.Sin(phase);
        }

        return Math.Sqrt(cos * cos + sin * sin) * 2 / count;
    }
}

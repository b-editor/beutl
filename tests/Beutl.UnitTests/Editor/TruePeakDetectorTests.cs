using Beutl.Editor.Components.AudioVisualizerTab.Utilities;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TruePeakDetectorTests
{
    private const int SampleRate = 48000;
    private const int Taps = 12;

    // The BS.1770-5 Annex 2 phases, apart from the detector's table so a changed coefficient also fails.
    private static readonly float[][] s_phases =
    [
        [
            0.0017089843750f, 0.0109863281250f, -0.0196533203125f, 0.0332031250000f, -0.0594482421875f, 0.1373291015625f,
            0.9721679687500f, -0.1022949218750f, 0.0476074218750f, -0.0266113281250f, 0.0148925781250f, -0.0083007812500f
        ],
        [
            -0.0291748046875f, 0.0292968750000f, -0.0517578125000f, 0.0891113281250f, -0.1665039062500f, 0.4650878906250f,
            0.7797851562500f, -0.2003173828125f, 0.1015625000000f, -0.0582275390625f, 0.0330810546875f, -0.0189208984375f
        ],
        [
            -0.0189208984375f, 0.0330810546875f, -0.0582275390625f, 0.1015625000000f, -0.2003173828125f, 0.7797851562500f,
            0.4650878906250f, -0.1665039062500f, 0.0891113281250f, -0.0517578125000f, 0.0292968750000f, -0.0291748046875f
        ],
        [
            -0.0083007812500f, 0.0148925781250f, -0.0266113281250f, 0.0476074218750f, -0.1022949218750f, 0.9721679687500f,
            0.1373291015625f, -0.0594482421875f, 0.0332031250000f, -0.0196533203125f, 0.0109863281250f, 0.0017089843750f
        ],
    ];

    // A low-frequency sine has no intersample overs, so its true peak equals its amplitude
    // whatever phase the block starts at (#3807: truncated taps at the edges read ~2 dB high).
    [TestCase(0.0)]
    [TestCase(0.5)]
    [TestCase(1.0)]
    [TestCase(2.0)]
    [TestCase(3.0)]
    public void LowFrequencySineReadsItsAmplitude(double phase)
    {
        float[] samples = Sine(997, 0.5, phase, SampleRate * 4 / 10);

        float peak = TruePeakDetector.Detect(samples);

        Assert.That(20 * Math.Log10(peak / 0.5), Is.EqualTo(0).Within(0.1));
    }

    // At fs/4 with a 45° offset every sample sits at ±0.707, while the waveform reaches ±1.
    [Test]
    public void IntersamplePeakIsRecovered()
    {
        float[] samples = Sine(SampleRate / 4.0, 1.0, Math.PI / 4, 4800);

        float peak = TruePeakDetector.Detect(samples);

        Assert.That(samples.Max(Math.Abs), Is.EqualTo(0.7071f).Within(1e-3f));
        Assert.That(20 * Math.Log10(peak), Is.EqualTo(0).Within(0.1));
    }

    [Test]
    public void BlocksShorterThanTheFilterReadTheSamplePeak()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TruePeakDetector.Detect([]), Is.Zero);
            Assert.That(TruePeakDetector.Detect([0.1f, -0.6f, 0.3f]), Is.EqualTo(0.6f));
        });
    }

    // Detect computes the four phases in one vector and ScalarDetect one at a time, so a changed
    // tap order or a fused multiply-add changes the bits of a reading.
    [Test]
    public void RandomBlocksReadTheSameBitsAsTheScalarForm()
    {
        var random = new Random(3807);
        int interpolated = 0;
        foreach (float amplitude in new[] { 1f, 1e-3f, 1e-40f, 1e30f, float.MaxValue })
        {
            for (int block = 0; block < 500; block++)
            {
                // Short blocks, so a reading is one sum rather than the largest of many.
                float[] samples = RandomBlock(random, random.Next(Taps, Taps * 2), amplitude);
                AssertSameBitsAsScalar(samples);
                if (ScalarDetect(samples) > samples.Max(Math.Abs)) interpolated++;
            }

            AssertSameBitsAsScalar(RandomBlock(random, 4800, amplitude));
        }

        Assert.That(interpolated, Is.GreaterThan(0), "Every reading was the sample peak, so no sum was compared.");
    }

    [Test]
    public void NonFiniteSamplesReadTheSameBitsAsTheScalarForm()
    {
        var random = new Random(3807);
        foreach (int length in new[] { 1, Taps - 1, Taps, Taps + 1, 64, 1024 })
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                float[] noisy = RandomBlock(random, length, 1f);
                noisy[random.Next(length)] = value;
                AssertSameBitsAsScalar(noisy);

                var silent = new float[length];
                silent[length / 2] = value;
                AssertSameBitsAsScalar(silent);

                AssertSameBitsAsScalar(Enumerable.Repeat(value, length).ToArray());
            }
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(Taps - 1)]
    [TestCase(Taps)]
    [TestCase(1024)]
    public void SilentBlocksReadTheSameBitsAsTheScalarForm(int length)
    {
        AssertSameBitsAsScalar(new float[length]);
        AssertSameBitsAsScalar(Enumerable.Repeat(-0f, length).ToArray());
        AssertSameBitsAsScalar(Enumerable.Range(0, length).Select(i => i % 2 == 0 ? 0f : -0f).ToArray());
    }

    [Test]
    public void BlocksShorterThanTheFilterReadTheSameBitsAsTheScalarForm()
    {
        var random = new Random(3807);
        for (int length = 1; length < Taps; length++)
        {
            for (int block = 0; block < 50; block++) AssertSameBitsAsScalar(RandomBlock(random, length, 1f));
        }
    }

    private static float ScalarDetect(ReadOnlySpan<float> samples)
    {
        float peak = 0f;
        foreach (float s in samples)
        {
            float a = MathF.Abs(s);
            if (a > peak) peak = a;
        }

        for (int n = Taps - 1; n < samples.Length; n++)
        {
            foreach (float[] phase in s_phases)
            {
                float y = 0f;
                for (int k = 0; k < Taps; k++) y += phase[k] * samples[n - k];
                float a = MathF.Abs(y);
                if (a > peak) peak = a;
            }
        }

        return peak;
    }

    private static void AssertSameBitsAsScalar(float[] samples)
        => Assert.That(BitConverter.SingleToInt32Bits(TruePeakDetector.Detect(samples)),
            Is.EqualTo(BitConverter.SingleToInt32Bits(ScalarDetect(samples))), $"{samples.Length} samples");

    private static float[] RandomBlock(Random random, int length, float amplitude)
    {
        var samples = new float[length];
        for (int i = 0; i < length; i++) samples[i] = (random.NextSingle() * 2 - 1) * amplitude;
        return samples;
    }

    private static float[] Sine(double frequency, double amplitude, double phase, int length)
        => Enumerable.Range(0, length)
            .Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / SampleRate + phase)))
            .ToArray();
}

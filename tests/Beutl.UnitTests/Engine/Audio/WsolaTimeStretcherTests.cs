using System.Reflection;
using Beutl.Audio.Graph;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class WsolaTimeStretcherTests
{
    private const int SampleRate = 48000;

    [TestCase(48000, 1920, 4d, 1024, 256)]
    [TestCase(48000, 94560, 4d, 1024, 256)]
    [TestCase(48000, 95040, 4d, 1024, 256)]
    [TestCase(48000, 95520, 4d, 1024, 37)]
    [TestCase(44100, 1764, 4d, 1024, 37)]
    [TestCase(48000, 240, 0.25d, 1024, 37)]
    [TestCase(48000, 1003, 1.6d, 37, 256)]
    [TestCase(48000, 94560, 1.6d, 1024, 64)]
    [TestCase(48000, 1, 4d, 1, 256)]
    [TestCase(48000, 0, 0.5d, 1024, 37)]
    public void FiniteInput_ProducesExactlyTheScaledLength(
        int sampleRate, int inputFrames, double tempo, int feedFrames, int receiveFrames)
    {
        var stretcher = new WsolaTimeStretcher(sampleRate, 2) { Tempo = tempo };
        float[] input = Signal(inputFrames, sampleRate);
        var output = new float[receiveFrames * 2];
        int expected = (int)Math.Ceiling(inputFrames / tempo);
        int fed = 0;
        int received = 0;
        bool finished = false;
        while (true)
        {
            int made = stretcher.ReceiveSamples(output, receiveFrames);
            received += made;
            Assert.That(received, Is.LessThanOrEqualTo(expected), "Output must not overrun before EOF is known.");
            if (made > 0)
                continue;
            if (fed < inputFrames)
            {
                int count = Math.Min(feedFrames, inputFrames - fed);
                stretcher.PutSamples(input.AsSpan(fed * 2, count * 2), count);
                fed += count;
            }
            else if (!finished)
            {
                stretcher.Flush();
                finished = true;
            }
            else
            {
                break;
            }
        }

        Assert.That(received, Is.EqualTo(expected));
        stretcher.Flush();
        Assert.That(stretcher.ReceiveSamples(output, receiveFrames), Is.Zero);
    }

    [Test]
    public void ReceiveSamples_AdvancesTheCursorByConsumedSamples()
    {
        var stretcher = new WsolaTimeStretcher(SampleRate, 2);
        stretcher.PutSamples(Signal(10000, SampleRate), 10000);
        var output = new float[720 * 2];

        Assert.That(stretcher.ReceiveSamples(output, 240), Is.EqualTo(240));
        Assert.That(SourcePosition(stretcher), Is.EqualTo(240));
        stretcher.Tempo = 4;
        Assert.That(stretcher.ReceiveSamples(output, 720), Is.EqualTo(720));
        Assert.That(SourcePosition(stretcher), Is.EqualTo(3120));
    }

    [Test]
    public void BufferedInput_UsesTheTempoWhenOutputIsConsumed()
    {
        const int inputFrames = 48111;
        const int initialOutputFrames = 1234;
        var stretcher = new WsolaTimeStretcher(SampleRate, 2);
        stretcher.PutSamples(Signal(inputFrames, SampleRate), inputFrames);
        stretcher.Flush();
        var output = new float[initialOutputFrames * 2];

        int received = stretcher.ReceiveSamples(output, initialOutputFrames);
        Assert.That(received, Is.EqualTo(initialOutputFrames));
        stretcher.Tempo = 4;
        int made;
        while ((made = stretcher.ReceiveSamples(output, initialOutputFrames)) > 0)
            received += made;

        int expected = initialOutputFrames + (int)Math.Ceiling((inputFrames - initialOutputFrames) / 4d);
        Assert.That(received, Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(37)]
    [TestCase(128)]
    [TestCase(256)]
    [TestCase(960)]
    public void SpeedCurve_IntegratesTheSamePositionForEveryChunkSize(int receiveFrames)
    {
        var stretcher = new WsolaTimeStretcher(SampleRate, 2);
        stretcher.PutSamples(Signal(SampleRate * 3, SampleRate), SampleRate * 3);
        var speeds = new double[SampleRate];
        for (int i = 0; i < speeds.Length; i++)
            speeds[i] = 0.5 + 1.5 * i / SampleRate;
        var output = new float[receiveFrames * 2];

        int received = 0;
        while (received < SampleRate)
        {
            int count = Math.Min(receiveFrames, SampleRate - received);
            int made = stretcher.ReceiveSamples(output, count, speeds.AsSpan(received, count));
            Assert.That(made, Is.EqualTo(count));
            received += made;
        }

        // Sum of the arithmetic progression 0.5 + 1.5*i/sampleRate over one output second.
        double expected = SampleRate * 1.25 - 0.75;
        Assert.That(SourcePosition(stretcher), Is.EqualTo(expected).Within(1e-8));
    }

    [Test]
    public void FinishedInput_WithSpeedCurveStopsAtTheIntegratedSourceEnd()
    {
        const int inputFrames = 30000;
        var stretcher = new WsolaTimeStretcher(SampleRate, 2);
        stretcher.PutSamples(Signal(inputFrames, SampleRate), inputFrames);
        stretcher.Flush();
        var speeds = new double[SampleRate * 2];
        for (int i = 0; i < speeds.Length; i++)
            speeds[i] = 0.5 + 1.5 * i / SampleRate;
        var output = new float[256 * 2];
        int received = 0;
        int made;
        while ((made = stretcher.ReceiveSamples(output, 256, speeds.AsSpan(received, 256))) > 0)
            received += made;

        // Solve sum(i=0..n-1, 0.5 + step*i) >= inputFrames for the final output sample.
        double step = 1.5 / SampleRate;
        double linear = 0.5 - step / 2;
        int expected = (int)Math.Ceiling((-linear + Math.Sqrt(linear * linear + 2 * step * inputFrames)) / step);
        Assert.That(received, Is.EqualTo(expected));
    }

    [Test]
    public void ShortSlowedInput_RemainsAudibleAcrossItsFullOutputLength()
    {
        var stretcher = new WsolaTimeStretcher(SampleRate, 2) { Tempo = 0.25 };
        stretcher.PutSamples(Signal(240, SampleRate), 240);
        stretcher.Flush();
        var output = new float[960 * 2];

        Assert.That(stretcher.ReceiveSamples(output, 960), Is.EqualTo(960));

        for (int start = 0; start < 960; start += 240)
        {
            double energy = 0;
            for (int i = start; i < start + 240; i++)
                energy += output[i * 2] * output[i * 2];
            Assert.That(Math.Sqrt(energy / 240), Is.GreaterThan(0.2),
                "The analysis padding must not become the synthesized sound.");
        }
    }

    private static double SourcePosition(WsolaTimeStretcher stretcher)
        => (double)typeof(WsolaTimeStretcher)
            .GetField("_sourcePosition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stretcher)!;

    private static float[] Signal(int frames, int sampleRate)
    {
        var input = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float value = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / sampleRate));
            input[i * 2] = value;
            input[i * 2 + 1] = -value;
        }
        return input;
    }
}

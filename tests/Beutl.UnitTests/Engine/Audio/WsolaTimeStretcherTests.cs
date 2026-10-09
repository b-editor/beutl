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

    [TestCase(0.25)]
    [TestCase(0.5)]
    [TestCase(2)]
    [TestCase(4)]
    public void StartupImpulse_IsEmittedAtItsMappedTime(double tempo)
    {
        var stretcher = new WsolaTimeStretcher(SampleRate, 2) { Tempo = tempo };
        var input = new float[4800 * 2];
        input[120 * 2] = input[120 * 2 + 1] = 1;
        stretcher.PutSamples(input, 4800);
        stretcher.Flush();
        var output = new float[2000 * 2];
        int made = stretcher.ReceiveSamples(output, 2000);
        int first = -1;
        for (int i = 0; i < made; i++)
        {
            if (Math.Abs(output[i * 2]) > 1e-5) { first = i; break; }
        }

        Assert.That(first, Is.EqualTo((int)Math.Ceiling(120 / tempo)));
    }

    [TestCase(0.25)]
    [TestCase(1)]
    [TestCase(4)]
    public void StreamingSilence_KeepsTheInputBufferBounded(double tempo)
    {
        const int feedFrames = 1024;
        const int feeds = 512;
        var stretcher = new WsolaTimeStretcher(SampleRate, 2) { Tempo = tempo };
        int initialCapacity = InputBuffer(stretcher).Length;
        var input = new float[feedFrames * 2];
        var output = new float[256 * 2];
        int received = 0;

        for (int i = 0; i < feeds; i++)
        {
            stretcher.PutSamples(input, feedFrames);
            int made;
            while ((made = stretcher.ReceiveSamples(output, 256)) > 0)
                received += made;
        }
        stretcher.Flush();
        int tail;
        while ((tail = stretcher.ReceiveSamples(output, 256)) > 0)
            received += tail;

        Assert.That(received, Is.EqualTo((int)Math.Ceiling(feedFrames * feeds / tempo)));
        Assert.That(output, Is.All.Zero);
        Assert.That(InputBuffer(stretcher).Length, Is.LessThanOrEqualTo(initialCapacity),
            "Consumed silence must not remain in the input buffer.");
    }

    [TestCase(0.25)]
    [TestCase(0.6)]
    [TestCase(4)]
    public void StreamingLeadingSilence_PreservesTheOnsetAfterDiscardingInput(double tempo)
    {
        const int silenceFrames = 1024 * 128 + 200;
        const int toneFrames = 4800;
        var input = new float[(silenceFrames + toneFrames) * 2];
        for (int i = silenceFrames; i < silenceFrames + toneFrames; i++)
        {
            input[i * 2] = 0.5f;
            input[i * 2 + 1] = -0.5f;
        }
        var stretcher = new WsolaTimeStretcher(SampleRate, 2) { Tempo = tempo };
        int initialCapacity = InputBuffer(stretcher).Length;
        float[] output = Stretch(stretcher, input, 1024, 37);
        int expectedOnset = (int)Math.Ceiling(silenceFrames / tempo);

        Assert.That(Array.FindIndex(output, value => Math.Abs(value) > 1e-5), Is.EqualTo(expectedOnset * 2));
        Assert.That(output.Length / 2, Is.EqualTo((int)Math.Ceiling((silenceFrames + toneFrames) / tempo)));
        Assert.That(InputBuffer(stretcher).Length, Is.LessThanOrEqualTo(initialCapacity));
    }

    [TestCase(240, 200, 0.25, 37)]
    [TestCase(240, 200, 0.5, 256)]
    [TestCase(240, 200, 2, 1)]
    [TestCase(240, 200, 4, 37)]
    [TestCase(240, 239, 0.25, 256)]
    [TestCase(240, 239, 0.5, 37)]
    [TestCase(4800, 4760, 0.25, 37)]
    [TestCase(1, 0, 0.25, 1)]
    public void ShortAudibleTail_AfterLeadingSilenceProducesTheScaledLength(
        int inputFrames, int onset, double tempo, int receiveFrames)
    {
        var input = new float[inputFrames * 2];
        for (int i = onset; i < inputFrames; i++)
        {
            input[i * 2] = 0.5f;
            input[i * 2 + 1] = -0.5f;
        }
        var stretcher = new WsolaTimeStretcher(SampleRate, 2) { Tempo = tempo };
        float[] output = Stretch(stretcher, input, 1024, receiveFrames);

        Assert.That(output.Length / 2, Is.EqualTo((int)Math.Ceiling(inputFrames / tempo)));
        Assert.That(Array.FindIndex(output, value => Math.Abs(value) > 1e-5),
            Is.EqualTo((int)Math.Ceiling(onset / tempo) * 2));
        for (int i = 0; i < output.Length / 2; i++)
            Assert.That(output[i * 2 + 1], Is.EqualTo(-output[i * 2]));
        if (inputFrames - onset >= 4)
        {
            for (int i = (int)Math.Ceiling(onset / tempo); i < output.Length / 2; i++)
                Assert.That(output[i * 2], Is.EqualTo(0.5f).Within(1e-6),
                    "The short audible region must remain audible after its mapped onset.");
        }
    }

    private static float[] Stretch(WsolaTimeStretcher stretcher, float[] input, int feedFrames, int receiveFrames)
    {
        var output = new float[receiveFrames * 2];
        var received = new List<float>();
        int fed = 0;
        bool finished = false;
        while (true)
        {
            int made = stretcher.ReceiveSamples(output, receiveFrames);
            if (made > 0)
            {
                received.AddRange(output.AsSpan(0, made * 2));
                continue;
            }
            if (fed < input.Length / 2)
            {
                int count = Math.Min(feedFrames, input.Length / 2 - fed);
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
                return received.ToArray();
            }
        }
    }

    private static float[] InputBuffer(WsolaTimeStretcher stretcher)
        => (float[])typeof(WsolaTimeStretcher)
            .GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stretcher)!;

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

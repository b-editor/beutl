using Beutl.AgentToolkit.Rendering;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class AudioRhythmAnalyzerTests
{
    [Test]
    public void AnalyzePcm_ClickTrackAt120Bpm_DetectsBeatGrid()
    {
        const int sampleRate = 44100;
        const double bpm = 120;
        float[] samples = CreateClickTrack(sampleRate, durationSeconds: 8, bpm);
        var analyzer = new AudioRhythmAnalyzer();

        AudioRhythmAnalysis result = analyzer.AnalyzePcm(samples, sampleRate);

        double[] expectedBeats = Enumerable
            .Range(0, 16)
            .Select(index => index * 0.5)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.EstimatedBpm, Is.EqualTo(120).Within(2));
            Assert.That(result.Confidence, Is.GreaterThan(0.5));
            Assert.That(result.StrongOnsetTimesSeconds, Has.Count.GreaterThanOrEqualTo(12));
            Assert.That(result.BeatTimesSeconds, Has.Count.GreaterThanOrEqualTo(expectedBeats.Length - 1));
        });

        foreach (double expected in expectedBeats)
        {
            double nearest = result.BeatTimesSeconds.Min(actual => Math.Abs(actual - expected));
            Assert.That(nearest, Is.LessThanOrEqualTo(0.020), $"Expected beat near {expected:F3}s.");
        }
    }

    [Test]
    public void AnalyzePcm_Silence_ReturnsNoOnsetsAndLowConfidence()
    {
        const int sampleRate = 44100;
        var analyzer = new AudioRhythmAnalyzer();

        AudioRhythmAnalysis result = analyzer.AnalyzePcm(new float[sampleRate * 4], sampleRate);

        Assert.Multiple(() =>
        {
            Assert.That(result.EstimatedBpm, Is.EqualTo(0));
            Assert.That(result.Confidence, Is.LessThan(0.1));
            Assert.That(result.BeatTimesSeconds, Is.Empty);
            Assert.That(result.StrongOnsetTimesSeconds, Is.Empty);
        });
    }

    private static float[] CreateClickTrack(int sampleRate, double durationSeconds, double bpm)
    {
        int sampleCount = (int)Math.Round(durationSeconds * sampleRate);
        float[] samples = new float[sampleCount];
        int beatSamples = (int)Math.Round(sampleRate * 60d / bpm);
        const int clickLength = 72;
        for (int beatStart = 0; beatStart < sampleCount; beatStart += beatSamples)
        {
            for (int i = 0; i < clickLength && beatStart + i < sampleCount; i++)
            {
                samples[beatStart + i] = (float)(1d - (i / (double)clickLength));
            }
        }

        return samples;
    }
}

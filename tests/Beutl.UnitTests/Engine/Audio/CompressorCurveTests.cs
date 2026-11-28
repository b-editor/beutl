using Beutl.Audio.Effects;

using static Beutl.Audio.Effects.CompressorParameters;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class CompressorCurveTests
{
    // The gain computer exactly as CompressorNode had it before it moved into CompressorParameters.
    // The shared function must reproduce this, so the curve the editor draws is the node's response.
    private static float OldGainReductionDb(float envelopeDb, float thresholdDb, float ratio, float kneeDb)
    {
        float slope = 1f - 1f / ratio;
        if (kneeDb > 0f)
        {
            float halfKnee = kneeDb * 0.5f;
            float diff = envelopeDb - thresholdDb;
            if (diff <= -halfKnee)
            {
                return 0f;
            }
            if (diff < halfKnee)
            {
                float x = diff + halfKnee;
                return slope * x * x / (2f * kneeDb);
            }
            return slope * diff;
        }

        return envelopeDb > thresholdDb ? slope * (envelopeDb - thresholdDb) : 0f;
    }

    [TestCase(0f, 1f, -60f, 0f)]
    [TestCase(0f, 20f, 0f, 6f)]
    [TestCase(24f, 1f, -60f, -24f)]
    [TestCase(24f, 20f, 0f, 24f)]
    [TestCase(6f, 4f, -20f, 3f)]
    [TestCase(0f, 20f, -60f, 0f)]
    [TestCase(24f, 1f, 0f, 0f)]
    public void ComputeOutputDb_MatchesTheNodesFormerFormula(
        float kneeDb, float ratio, float thresholdDb, float makeupDb)
    {
        // Sample the whole input range at 1 dB, including both knee edges and the threshold.
        for (int i = -60; i <= 0; i++)
        {
            float inputDb = i;
            float expected = inputDb - OldGainReductionDb(inputDb, thresholdDb, ratio, kneeDb) + makeupDb;
            float actual = ComputeOutputDb(inputDb, thresholdDb, ratio, kneeDb, makeupDb);

            Assert.That(actual, Is.EqualTo(expected),
                $"knee={kneeDb}, ratio={ratio}, threshold={thresholdDb}, makeup={makeupDb}, input={inputDb}");
        }
    }

    [Test]
    public void ComputeOutputDb_BelowThresholdIsTheInputPlusMakeup()
    {
        // With no knee, below the threshold the transfer curve is a plain offset, so the plot's flat
        // section is the diagonal raised by makeup rather than a compressed line.
        for (int i = -60; i < -20; i++)
        {
            Assert.That(ComputeOutputDb(i, -20f, 20f, 0f, 6f), Is.EqualTo(i + 6f));
        }
    }
}

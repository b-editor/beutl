using Beutl.Audio;

namespace Beutl.UnitTests.Engine;

public class SoundSamplingHelperTests
{
    [Test]
    public void DownsampleMinMax_EmptySamples_ClearsBuffers()
    {
        Span<float> mins = stackalloc float[3];
        Span<float> maxs = stackalloc float[3];
        mins.Fill(0.5f);
        maxs.Fill(0.5f);

        SoundSamplingHelper.DownsampleMinMax(ReadOnlySpan<float>.Empty, mins, maxs);

        for (int i = 0; i < mins.Length; i++)
        {
            Assert.That(mins[i], Is.EqualTo(0f));
            Assert.That(maxs[i], Is.EqualTo(0f));
        }
    }

    [Test]
    public void DownsampleMinMax_ZeroBars_DoesNothing()
    {
        SoundSamplingHelper.DownsampleMinMax(
            new float[] { 1f, 2f, 3f },
            Span<float>.Empty,
            Span<float>.Empty
        );
    }

    [Test]
    public void DownsampleMinMax_LengthMismatch_Throws()
    {
        ReadOnlySpan<float> samples = [1f, 2f, 3f];
        var mins = new float[2];
        var maxs = new float[3];

        try
        {
            SoundSamplingHelper.DownsampleMinMax(samples, mins, maxs);
            Assert.Fail("expected ArgumentException");
        }
        catch (ArgumentException)
        {
            Assert.Pass();
        }
    }

    [Test]
    public void DownsampleMinMax_DistributesSamplesIntoBins()
    {
        float[] samples = [-2f, -1f, 0f, 1f, 2f, 3f];
        var mins = new float[3];
        var maxs = new float[3];

        SoundSamplingHelper.DownsampleMinMax(samples, mins, maxs);

        Assert.That(mins[0], Is.EqualTo(-2f));
        Assert.That(maxs[0], Is.EqualTo(-1f));
        Assert.That(mins[1], Is.EqualTo(0f));
        Assert.That(maxs[1], Is.EqualTo(1f));
        Assert.That(mins[2], Is.EqualTo(2f));
        Assert.That(maxs[2], Is.EqualTo(3f));
    }

    [Test]
    public void DownsampleMinMax_MoreBinsThanSamples_NoInfinitiesLeak()
    {
        float[] samples = [4f, -4f];
        var mins = new float[5];
        var maxs = new float[5];

        SoundSamplingHelper.DownsampleMinMax(samples, mins, maxs);

        // The function ensures sentinels are replaced — no infinities should leak.
        for (int i = 0; i < mins.Length; i++)
        {
            Assert.That(float.IsInfinity(mins[i]), Is.False);
            Assert.That(float.IsInfinity(maxs[i]), Is.False);
            Assert.That(mins[i], Is.LessThanOrEqualTo(maxs[i]));
        }
    }

    [Test]
    public void ExtractWindow_CenteredOnIndex_CopiesNeighborhood()
    {
        float[] samples = [1, 2, 3, 4, 5, 6, 7];
        Span<float> dest = stackalloc float[3];

        SoundSamplingHelper.ExtractWindow(samples, centerIndex: 3, dest);

        // window n=3, start = 3 - 3/2 = 2 -> samples[2..4]
        Assert.That(dest[0], Is.EqualTo(3f));
        Assert.That(dest[1], Is.EqualTo(4f));
        Assert.That(dest[2], Is.EqualTo(5f));
    }

    [Test]
    public void ExtractWindow_OutsideRange_FillsZeros()
    {
        float[] samples = [10, 20];
        Span<float> dest = stackalloc float[5];

        SoundSamplingHelper.ExtractWindow(samples, centerIndex: 0, dest);

        // window n=5, start = 0 - 2 = -2. Indices -2,-1,0,1,2 -> map to 0,0,10,20,0
        Assert.That(dest[0], Is.EqualTo(0f));
        Assert.That(dest[1], Is.EqualTo(0f));
        Assert.That(dest[2], Is.EqualTo(10f));
        Assert.That(dest[3], Is.EqualTo(20f));
        Assert.That(dest[4], Is.EqualTo(0f));
    }

    [Test]
    public void ExtractWindow_EmptyDestination_DoesNothing()
    {
        SoundSamplingHelper.ExtractWindow(new float[] { 1, 2, 3 }, 1, Span<float>.Empty);
    }
}

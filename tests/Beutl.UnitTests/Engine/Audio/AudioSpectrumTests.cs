using Beutl.Audio;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioSpectrumTests
{
    [Test]
    public void HannSpectrumPreservesInputAndFindsTheTone()
    {
        float[] samples = Enumerable.Range(0, 1024).Select(i => MathF.Sin(2 * MathF.PI * 32 * i / 1024)).ToArray();
        float[] original = [.. samples];
        float[] spectrum = new float[512];
        AudioSpectrum.Calculate(samples, spectrum);
        Assert.That(samples, Is.EqualTo(original));
        Assert.That(Array.IndexOf(spectrum, spectrum.Max()), Is.EqualTo(32));
        Assert.That(spectrum[32], Is.EqualTo(1).Within(0.002));
        AudioSpectrum.Calculate(new float[1024], spectrum);
        Assert.That(spectrum, Is.All.Zero);
    }

    [TestCase(0, 0)]
    [TestCase(3, 1)]
    [TestCase(8, 8)]
    public void InvalidSpectrumDimensionsAreRejected(int input, int output)
    {
        Assert.That(() => AudioSpectrum.Calculate(new float[input], new float[output]), Throws.ArgumentException);
    }
}

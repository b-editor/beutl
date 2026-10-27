using Beutl.Audio;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioSpectrumTests
{
    [TestCase(16, 4)]
    [TestCase(1024, 32)]
    public void HannSpectrumPreservesInputAndFindsTheTone(int length, int bin)
    {
        float[] samples = Enumerable.Range(0, length).Select(i => MathF.Sin(2 * MathF.PI * bin * i / length)).ToArray();
        float[] original = [.. samples];
        float[] spectrum = new float[length / 2];
        AudioSpectrum.Calculate(samples, spectrum);
        Assert.That(samples, Is.EqualTo(original));
        Assert.That(Array.IndexOf(spectrum, spectrum.Max()), Is.EqualTo(bin));
        Assert.That(spectrum[bin], Is.EqualTo(1).Within(0.002));
        AudioSpectrum.Calculate(new float[length], spectrum);
        Assert.That(spectrum, Is.All.Zero);
    }

    [TestCase(4)]
    [TestCase(16)]
    [TestCase(1024)]
    public void ConstantInputHasUnitDcMagnitude(int length)
    {
        float[] spectrum = new float[length / 2];
        AudioSpectrum.Calculate(Enumerable.Repeat(1f, length).ToArray(), spectrum);
        Assert.That(spectrum[0], Is.EqualTo(1).Within(0.00001));
    }

    [TestCase(1, 0)]
    [TestCase(2, 1)]
    [TestCase(0, 0)]
    [TestCase(3, 1)]
    [TestCase(8, 8)]
    public void InvalidSpectrumDimensionsAreRejected(int input, int output)
    {
        Assert.That(() => AudioSpectrum.Calculate(new float[input], new float[output]), Throws.ArgumentException);
    }
}

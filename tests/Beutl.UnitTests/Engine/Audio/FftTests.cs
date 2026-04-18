using Beutl.Audio.Graph;

namespace Beutl.UnitTests.Engine.Audio;

public class FftTests
{
    [Test]
    public void IsPowerOfTwo_ReturnsExpected()
    {
        Assert.That(Fft.IsPowerOfTwo(1), Is.True);
        Assert.That(Fft.IsPowerOfTwo(2), Is.True);
        Assert.That(Fft.IsPowerOfTwo(1024), Is.True);
        Assert.That(Fft.IsPowerOfTwo(3), Is.False);
        Assert.That(Fft.IsPowerOfTwo(0), Is.False);
        Assert.That(Fft.IsPowerOfTwo(-4), Is.False);
    }

    [Test]
    public void ClampToPowerOfTwo_ClampsAndRoundsDown()
    {
        Assert.That(Fft.ClampToPowerOfTwo(100), Is.EqualTo(128));
        Assert.That(Fft.ClampToPowerOfTwo(32), Is.EqualTo(64));
        Assert.That(Fft.ClampToPowerOfTwo(50000, 64, 16384), Is.EqualTo(16384));
    }

    [Test]
    public void Forward_DcSignal_EnergyInDcBin()
    {
        const int n = 64;
        var real = new float[n];
        var imag = new float[n];
        for (int i = 0; i < n; i++) real[i] = 1f;

        Fft.Forward(real, imag);

        var mag = new float[n];
        Fft.Magnitudes(real, imag, mag);

        Assert.That(mag[0], Is.EqualTo(n).Within(1e-3));
        for (int i = 1; i < n / 2; i++)
        {
            Assert.That(mag[i], Is.LessThan(1e-3));
        }
    }

    [Test]
    public void Forward_SineWave_PeakAtExpectedBin()
    {
        const int n = 1024;
        const int bin = 64;
        var real = new float[n];
        var imag = new float[n];
        for (int i = 0; i < n; i++)
        {
            real[i] = MathF.Sin(2f * MathF.PI * bin * i / n);
        }

        Fft.Forward(real, imag);

        var mag = new float[n / 2 + 1];
        Fft.Magnitudes(real, imag, mag);

        int maxBin = 0;
        float maxVal = 0f;
        for (int i = 0; i < mag.Length; i++)
        {
            if (mag[i] > maxVal)
            {
                maxVal = mag[i];
                maxBin = i;
            }
        }

        Assert.That(maxBin, Is.EqualTo(bin));
    }

    [Test]
    public void ApplyHann_EndpointsAreZero()
    {
        var window = new float[16];
        for (int i = 0; i < window.Length; i++) window[i] = 1f;

        Fft.ApplyHann(window);

        Assert.That(window[0], Is.EqualTo(0f).Within(1e-6));
        Assert.That(window[^1], Is.EqualTo(0f).Within(1e-6));
        Assert.That(window[window.Length / 2], Is.GreaterThan(0.9f));
    }

    [Test]
    public void Forward_RejectsNonPowerOfTwo()
    {
        var real = new float[30];
        var imag = new float[30];
        Assert.Throws<ArgumentException>(() => Fft.Forward(real, imag));
    }
}

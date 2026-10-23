using System.Text;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class LutEffectDomainTests
{
    // Domain mapping preserves the existing 3D grid spacing tracked in #3511.
    [TestCase(1, "0", "2", 0.214f)]
    [TestCase(3, "0", "2", 0.9912f)]
    [TestCase(1, "0.25", "1.75", 0.214f)]
    [TestCase(3, "0.25", "1.75", 0.9912f)]
    [TestCase(1, "0", "0", 1f)]
    [TestCase(3, "1", "0", 1f)]
    [TestCase(1, "0", "Infinity", 1f)]
    [TestCase(3, "NaN", "1", 1f)]
    [TestCase(1, "-3.4e38", "3.4e38", 1f)]
    public void TheLookupUsesTheDeclaredDomainOrDefaultsAnInvalidRange(
        int dimension, string domainMin, string domainMax, float expectedRed)
    {
        string data = dimension == 1
            ? "0 0 0\n1 1 1"
            : "0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1";
        var effect = new LutEffect
        {
            Source =
            {
                Value = CreateSource($"""
                    LUT_{dimension}D_SIZE 2
                    DOMAIN_MIN {domainMin} {domainMin} {domainMin}
                    DOMAIN_MAX {domainMax} {domainMax} {domainMax}
                    {data}
                    """),
            },
        };

        (_, float[] after) = FilterEffectTestHarness.RunOnSolidTarget(effect, SKColors.Red, new PixelSize(2, 2));

        Assert.That(after[0], Is.EqualTo(expectedRed).Within(0.001f));
    }

    private static CubeSource CreateSource(string cubeText)
    {
        var source = new CubeSource();
        source.ReadFrom(new Uri(
            "data:text/plain;base64," + Convert.ToBase64String(Encoding.ASCII.GetBytes(cubeText + "\n"))));
        return source;
    }
}

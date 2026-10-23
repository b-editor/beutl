using System.Text;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class LutEffectDomainTests
{
    // An identity LUT over the domain 0 to 2 reads full red at its middle, 0.5 in sRGB; a domain that does not
    // grow is not valid .cube and is read as the default 0 to 1.
    [TestCase("2 2 2", 0.214f)]
    [TestCase("0 0 0", 1f)]
    public void TheLookupIsScaledToTheDeclaredDomain(string domainMax, float expectedRed)
    {
        var effect = new LutEffect
        {
            Source =
            {
                Value = CreateSource($"""
                    LUT_1D_SIZE 2
                    DOMAIN_MIN 0 0 0
                    DOMAIN_MAX {domainMax}
                    0 0 0
                    1 1 1
                    """),
            },
        };

        (_, float[] after) = FilterEffectTestHarness.RunOnSolidTarget(effect, SKColors.Red, new PixelSize(2, 2));

        Assert.That(after[0], Is.EqualTo(expectedRed).Within(0.01f));
    }

    private static CubeSource CreateSource(string cubeText)
    {
        var source = new CubeSource();
        source.ReadFrom(new Uri(
            "data:text/plain;base64," + Convert.ToBase64String(Encoding.ASCII.GetBytes(cubeText + "\n"))));
        return source;
    }
}

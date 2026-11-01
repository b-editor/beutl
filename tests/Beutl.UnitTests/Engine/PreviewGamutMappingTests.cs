using System.Numerics;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class PreviewGamutMappingTests
{
    private static readonly DisplayPrimaries s_displayP3 = new(0.680f, 0.320f, 0.265f, 0.690f, 0.150f, 0.060f);

    // A display with sRGB's primaries leaves linear sRGB as it is.
    [Test]
    public void For_SrgbPrimaries_IsIdentity()
    {
        PreviewGamutMapping mapping = PreviewGamutMapping.For(DisplayPrimaries.Srgb, withinSrgb: false);

        Assert.That(ToDisplay(mapping, Vector3.UnitX), Is.EqualTo(Vector3.UnitX).Using<Vector3>(Within(1e-4f)));
        Assert.That(ToDisplay(mapping, Vector3.UnitY), Is.EqualTo(Vector3.UnitY).Using<Vector3>(Within(1e-4f)));
        Assert.That(ToDisplay(mapping, Vector3.UnitZ), Is.EqualTo(Vector3.UnitZ).Using<Vector3>(Within(1e-4f)));
    }

    // The rows take linear sRGB to the display's RGB: P3's green, which sRGB needs negative red and blue for, is pure
    // green there, and white stays white, as the shader's walk toward gray needs.
    [Test]
    public void For_DisplayP3_ConvertsSrgbToTheDisplaysRgb()
    {
        PreviewGamutMapping mapping = PreviewGamutMapping.For(s_displayP3, withinSrgb: true);

        Assert.That(ToDisplay(mapping, new Vector3(-0.2247f, 1.0419f, -0.0786f)),
            Is.EqualTo(Vector3.UnitY).Using<Vector3>(Within(1e-3f)));
        Assert.That(ToDisplay(mapping, Vector3.One), Is.EqualTo(Vector3.One).Using<Vector3>(Within(1e-4f)));
        Assert.That(mapping.WithinSrgb, Is.True);
        Assert.That(mapping.IsEnabled, Is.True);
    }

    // Zeros, NaNs, a line, or a triangle that misses the white point describe no display.
    [TestCase(0f, 0f, 0f, 0f, 0f, 0f)]
    [TestCase(float.NaN, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f)]
    [TestCase(0.1f, 0.1f, 0.2f, 0.2f, 0.3f, 0.3f)]
    [TestCase(0.60f, 0.30f, 0.70f, 0.30f, 0.65f, 0.35f)]
    public void For_PrimariesThatAreNoDisplay_IsNone(float rx, float ry, float gx, float gy, float bx, float by)
    {
        PreviewGamutMapping mapping = PreviewGamutMapping.For(new DisplayPrimaries(rx, ry, gx, gy, bx, by), false);

        Assert.That(mapping, Is.EqualTo(PreviewGamutMapping.None));
        Assert.That(mapping.IsEnabled, Is.False);
    }

    // Vulkan guarantees 128 bytes of push constants; the present pipeline's must fit in them.
    [Test]
    public unsafe void PresentPushConstants_FitTheGuaranteedSize()
    {
        Assert.That(sizeof(PresentPushConstants), Is.LessThanOrEqualTo(128));
    }

    private static Vector3 ToDisplay(PreviewGamutMapping mapping, Vector3 srgb)
    {
        return new Vector3(
            Vector3.Dot(mapping.ToDisplay0, srgb),
            Vector3.Dot(mapping.ToDisplay1, srgb),
            Vector3.Dot(mapping.ToDisplay2, srgb));
    }

    private static Func<Vector3, Vector3, bool> Within(float tolerance)
    {
        return (a, b) => MathF.Abs(a.X - b.X) <= tolerance && MathF.Abs(a.Y - b.Y) <= tolerance
            && MathF.Abs(a.Z - b.Z) <= tolerance;
    }
}

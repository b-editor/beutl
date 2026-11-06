using System.Numerics;
using System.Runtime.InteropServices;
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

    // vkCmdPushConstants copies the struct as it is, so each member must sit where the present shader's block reads it.
    [Test]
    public unsafe void PresentPushConstants_LineUpWithTheShadersBlock()
    {
        Assert.That(OffsetOf(nameof(PresentPushConstants.DisplayMapping)), Is.EqualTo(32));
        Assert.That(OffsetOf(nameof(PresentPushConstants.Gamut0)), Is.EqualTo(48));
        Assert.That(OffsetOf(nameof(PresentPushConstants.Gamut1)), Is.EqualTo(64));
        Assert.That(OffsetOf(nameof(PresentPushConstants.Gamut2)), Is.EqualTo(80));
        Assert.That(OffsetOf(nameof(PresentPushConstants.ReferenceWhite)), Is.EqualTo(96));
        Assert.That(OffsetOf(nameof(PresentPushConstants.OutputBt2020)), Is.EqualTo(100));
        Assert.That(OffsetOf(nameof(PresentPushConstants.Overlay)), Is.EqualTo(104));
        Assert.That(OffsetOf(nameof(PresentPushConstants.ComposeInGamma)), Is.EqualTo(108));
        Assert.That(OffsetOf(nameof(PresentPushConstants.SampleNearest)), Is.EqualTo(112));
        Assert.That(sizeof(PresentPushConstants), Is.EqualTo(116));
    }

    // The shader hands displayMapping's x, y, z and w to displayMapGain's knee, ks, maxLum and sourcePq.
    [Test]
    public void PresentPushConstants_TakeADisplayMappingInTheShadersOrder()
    {
        PresentPushConstants constants = default;

        constants.SetDisplayMapping(new PreviewDisplayMapping(Knee: 1f, Ks: 2f, MaxLum: 3f, SourcePq: 4f));

        Assert.That(constants.DisplayMapping, Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
    }

    // The shader's fitGamut takes a row of the matrix from each xyz, returns early when gamut0.w is 0, and keeps colors
    // within sRGB when gamut1.w is not.
    [Test]
    public void PresentPushConstants_TakeAGamutMappingAsRowsAndFlags()
    {
        var row0 = new Vector3(1f, 2f, 3f);
        var row1 = new Vector3(4f, 5f, 6f);
        var row2 = new Vector3(7f, 8f, 9f);
        PresentPushConstants constants = default;

        constants.SetGamut(new PreviewGamutMapping(row0, row1, row2, WithinSrgb: true));

        Assert.That(constants.Gamut0, Is.EqualTo(new Vector4(1f, 2f, 3f, 1f)));
        Assert.That(constants.Gamut1, Is.EqualTo(new Vector4(4f, 5f, 6f, 1f)));
        Assert.That(constants.Gamut2, Is.EqualTo(new Vector4(7f, 8f, 9f, 0f)));

        constants.SetGamut(new PreviewGamutMapping(row0, row1, row2, WithinSrgb: false));

        Assert.That(constants.Gamut0.W, Is.EqualTo(1f));
        Assert.That(constants.Gamut1.W, Is.EqualTo(0f));

        constants.SetGamut(PreviewGamutMapping.None);

        Assert.That(constants.Gamut0.W, Is.EqualTo(0f));
    }

    private static int OffsetOf(string member)
    {
        return Marshal.OffsetOf<PresentPushConstants>(member).ToInt32();
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

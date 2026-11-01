using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class PreviewDisplayMappingTests
{
    // An unknown headroom, or one that already holds the assumed content, leaves values as they are.
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(PreviewDisplayMapping.ContentPeak)]
    [TestCase(16f)]
    public void For_UnknownOrAmpleHeadroom_IsIdentity(float headroom)
    {
        Assert.That(PreviewDisplayMapping.For(headroom), Is.EqualTo(PreviewDisplayMapping.Identity));
    }

    // BT.2390 on an SDR display: 1000 nit content ends at 203 nit, and values below about 88 nit stay put.
    [Test]
    public void For_SdrDisplay_FollowsBt2390()
    {
        PreviewDisplayMapping mapping = PreviewDisplayMapping.For(1f);

        Assert.That(mapping.SourcePq, Is.EqualTo(0.75183f).Within(1e-4f), "PQ signal of 1000 nit");
        Assert.That(mapping.MaxLum, Is.EqualTo(0.77237f).Within(1e-4f), "PQ(203 nit) / PQ(1000 nit)");
        Assert.That(mapping.Ks, Is.EqualTo(1.5f * mapping.MaxLum - 0.5f).Within(1e-6f));
        Assert.That(mapping.Knee, Is.EqualTo(0.43269f).Within(1e-3f));
    }

    // The more the display shows, the less of the range the roll-off needs.
    [Test]
    public void Knee_RisesWithHeadroom()
    {
        float[] headrooms = [1f, 1.2f, 2f, 4f];
        float[] knees = headrooms.Select(h => PreviewDisplayMapping.For(h).Knee).ToArray();

        Assert.That(knees, Is.Ordered.Ascending);
        Assert.That(knees[^1], Is.LessThan(4f));
    }

    // The mapping functions are shared with the SDR preview's SkSL; this proves the GLSL side still compiles.
    [Test]
    public void PresentFragmentShader_Compiles()
    {
        using var compiler = new VulkanShaderCompiler();

        byte[] spirv = compiler.CompileToSpirv(VulkanPresentPipeline.FragmentShaderSource, ShaderStage.Fragment);

        Assert.That(spirv, Is.Not.Empty);
    }
}

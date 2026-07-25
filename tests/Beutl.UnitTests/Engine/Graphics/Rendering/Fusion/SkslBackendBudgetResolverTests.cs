using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
public sealed class SkslBackendBudgetResolverTests
{
    [Test]
    public void PortableProfile_UsesFiniteConservativeFusionLimits()
    {
        SkslBackendBudget budget = SkslBackendBudgetResolver.Portable;

        Assert.Multiple(() =>
        {
            Assert.That(budget.CapabilityClass, Is.EqualTo(SkslBackendCapabilityClass.Portable));
            Assert.That(budget.MaxStages, Is.EqualTo(16));
            Assert.That(budget.MaxUniformVectors, Is.EqualTo(128));
            Assert.That(budget.MaxSamplers, Is.EqualTo(8));
            Assert.That(budget.MaxChildren, Is.EqualTo(8));
            Assert.That(budget.MaxSourceBytes, Is.EqualTo(64 * 1024));
            Assert.That(budget.MaxProgramTokens, Is.EqualTo(16 * 1024));
        });
    }

    [TestCase(GRBackend.Vulkan, "Vulkan")]
    [TestCase(GRBackend.Metal, "Metal")]
    [TestCase(GRBackend.OpenGL, "Portable")]
    [TestCase(GRBackend.Direct3D, "Portable")]
    [TestCase(GRBackend.Dawn, "Portable")]
    [TestCase(GRBackend.Unsupported, "Portable")]
    public void Resolve_MapsBackendToStableCapabilityClass(
        GRBackend backend,
        string expected)
    {
        SkslBackendBudget first = SkslBackendBudgetResolver.Resolve(backend);
        SkslBackendBudget second = SkslBackendBudgetResolver.Resolve(backend);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(second));
            Assert.That(first.CapabilityClass.ToString(), Is.EqualTo(expected));
        });
    }

    [Test]
    public void Resolve_NullAndUnknownBackendUsePortableProfile()
    {
        SkslBackendBudget portable = SkslBackendBudgetResolver.Portable;

        Assert.Multiple(() =>
        {
            Assert.That(SkslBackendBudgetResolver.Resolve(null), Is.SameAs(portable));
            Assert.That(SkslBackendBudgetResolver.Resolve((GRBackend)int.MaxValue), Is.SameAs(portable));
            Assert.That(
                SkslBackendBudgetResolver.Resolve(GRBackend.Vulkan).CapabilityClass,
                Is.Not.EqualTo(portable.CapabilityClass));
            Assert.That(
                SkslBackendBudgetResolver.Resolve(GRBackend.Metal).CapabilityClass,
                Is.Not.EqualTo(portable.CapabilityClass));
        });
    }

    [Test]
    public void ProfilesShareConservativeLimitsButKeepProgramIdentitySeparated()
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");
        var stage = new SkslSnippetStage(description);
        SkslBackendBudget portable = SkslBackendBudgetResolver.Portable;
        SkslBackendBudget vulkan = SkslBackendBudgetResolver.Resolve(GRBackend.Vulkan);
        SkslBackendBudget metal = SkslBackendBudgetResolver.Resolve(GRBackend.Metal);

        SkslMergedProgram portableProgram = SkslSnippetMerger.MergeAndSplit([stage], portable).Single();
        SkslMergedProgram vulkanProgram = SkslSnippetMerger.MergeAndSplit([stage], vulkan).Single();
        SkslMergedProgram metalProgram = SkslSnippetMerger.MergeAndSplit([stage], metal).Single();

        Assert.Multiple(() =>
        {
            Assert.That(vulkan.MaxStages, Is.EqualTo(portable.MaxStages));
            Assert.That(vulkan.MaxUniformVectors, Is.EqualTo(portable.MaxUniformVectors));
            Assert.That(vulkan.MaxSamplers, Is.EqualTo(portable.MaxSamplers));
            Assert.That(vulkan.MaxChildren, Is.EqualTo(portable.MaxChildren));
            Assert.That(vulkan.MaxSourceBytes, Is.EqualTo(portable.MaxSourceBytes));
            Assert.That(vulkan.MaxProgramTokens, Is.EqualTo(portable.MaxProgramTokens));
            Assert.That(portableProgram.Identity, Is.Not.EqualTo(vulkanProgram.Identity));
            Assert.That(portableProgram.Identity, Is.Not.EqualTo(metalProgram.Identity));
            Assert.That(vulkanProgram.Identity, Is.Not.EqualTo(metalProgram.Identity));
        });
    }
}

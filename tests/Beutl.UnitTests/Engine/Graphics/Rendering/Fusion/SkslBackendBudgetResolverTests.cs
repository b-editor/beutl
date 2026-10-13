using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
public sealed class SkslBackendBudgetResolverTests
{
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

    [TestCase("Portable")]
    [TestCase("Vulkan")]
    [TestCase("Metal")]
    public void CapabilityProfiles_UseSupportedBackendFloorWithHeadroom(string capabilityClass)
    {
        SkslBackendBudget budget = capabilityClass switch
        {
            "Portable" => SkslBackendBudgetResolver.Portable,
            "Vulkan" => SkslBackendBudgetResolver.Resolve(GRBackend.Vulkan),
            "Metal" => SkslBackendBudgetResolver.Resolve(GRBackend.Metal),
            _ => throw new ArgumentOutOfRangeException(nameof(capabilityClass)),
        };

        Assert.Multiple(() =>
        {
            Assert.That(budget.CapabilityClass.ToString(), Is.EqualTo(capabilityClass));
            Assert.That(budget.MaxStages, Is.EqualTo(16));
            Assert.That(budget.MaxUniformVectors, Is.EqualTo(128));
            Assert.That(budget.MaxSamplers, Is.EqualTo(12));
            Assert.That(budget.MaxSourceBytes, Is.EqualTo(64 * 1024));
            Assert.That(budget.MaxProgramTokens, Is.EqualTo(16 * 1024));
        });
    }

    [Test]
    public void CapabilityClass_RemainsPartOfBudgetAndCacheIdentityWhenLimitsMatch()
    {
        SkslBackendBudget portable = CreateIdentityBudget(SkslBackendCapabilityClass.Portable);
        SkslBackendBudget vulkan = CreateIdentityBudget(SkslBackendCapabilityClass.Vulkan);
        SkslBackendBudget metal = CreateIdentityBudget(SkslBackendCapabilityClass.Metal);
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");
        SkslMergedProgram portableProgram = SkslSnippetMerger.MergeAndSplit([description], portable).Single();
        SkslMergedProgram vulkanProgram = SkslSnippetMerger.MergeAndSplit([description], vulkan).Single();
        SkslMergedProgram metalProgram = SkslSnippetMerger.MergeAndSplit([description], metal).Single();
        var contextIdentity = new RenderCacheDeviceContextIdentity("device", "context");
        ProgramCacheContextKey portableContext = SkRuntimeEffectProgramCache.CreateContextKey(
            contextIdentity,
            portable);
        ProgramCacheContextKey vulkanContext = SkRuntimeEffectProgramCache.CreateContextKey(
            contextIdentity,
            vulkan);
        ProgramCacheContextKey metalContext = SkRuntimeEffectProgramCache.CreateContextKey(
            contextIdentity,
            metal);

        Assert.Multiple(() =>
        {
            Assert.That(portable, Is.Not.EqualTo(vulkan));
            Assert.That(portable, Is.Not.EqualTo(metal));
            Assert.That(vulkan, Is.Not.EqualTo(metal));
            Assert.That(portableProgram.Identity, Is.Not.EqualTo(vulkanProgram.Identity));
            Assert.That(portableProgram.Identity, Is.Not.EqualTo(metalProgram.Identity));
            Assert.That(vulkanProgram.Identity, Is.Not.EqualTo(metalProgram.Identity));
            Assert.That(portableContext, Is.Not.EqualTo(vulkanContext));
            Assert.That(portableContext, Is.Not.EqualTo(metalContext));
            Assert.That(vulkanContext, Is.Not.EqualTo(metalContext));
        });
    }

    private static SkslBackendBudget CreateIdentityBudget(SkslBackendCapabilityClass capabilityClass)
        => new(
            capabilityClass,
            maxStages: 16,
            maxUniformVectors: 128,
            maxSamplers: 16,
            maxSourceBytes: 64 * 1024,
            maxProgramTokens: 16 * 1024);
}

using System.Collections.Immutable;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public sealed class SpecializationConstantTests
{
    [Test]
    public void ValidateSpecializationConstants_NormalizesDefaultToEmpty()
    {
        ImmutableArray<SpecializationConstant> result = VulkanContext.ValidateSpecializationConstants(
            default,
            "options");

        Assert.That(result, Is.Empty);
        Assert.That(result.IsDefault, Is.False);
    }

    [Test]
    public void ValidateSpecializationConstants_AllowsSameIdInDisjointStages()
    {
        ImmutableArray<SpecializationConstant> constants =
        [
            SpecializationConstant.Create(0, 1, ShaderStage.Vertex),
            SpecializationConstant.Create(0, 2, ShaderStage.Fragment),
        ];

        ImmutableArray<SpecializationConstant> result = VulkanContext.ValidateSpecializationConstants(
            constants,
            "options");

        Assert.That(result, Is.EqualTo(constants));
    }

    [Test]
    public void ValidateSpecializationConstants_RejectsOverlappingStageAndId()
    {
        ImmutableArray<SpecializationConstant> constants =
        [
            SpecializationConstant.Create(7, 1, ShaderStage.Vertex | ShaderStage.Fragment),
            SpecializationConstant.Create(7, 2, ShaderStage.Fragment),
        ];

        Assert.That(
            () => VulkanContext.ValidateSpecializationConstants(constants, "options"),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("options"));
    }

    [TestCase(ShaderStage.None)]
    [TestCase(ShaderStage.Compute)]
    [TestCase(ShaderStage.AllGraphics)]
    public void ValidateSpecializationConstants_RejectsUnsupportedStages(ShaderStage stages)
    {
        ImmutableArray<SpecializationConstant> constants =
        [SpecializationConstant.Create(0, 1, stages)];

        Assert.That(
            () => VulkanContext.ValidateSpecializationConstants(constants, "options"),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("options"));
    }

    [Test]
    public void ValidateSpecializationConstants_RejectsDefaultDescriptor()
    {
        ImmutableArray<SpecializationConstant> constants = [default(SpecializationConstant)];

        Assert.That(
            () => VulkanContext.ValidateSpecializationConstants(constants, "options"),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("options"));
    }
}

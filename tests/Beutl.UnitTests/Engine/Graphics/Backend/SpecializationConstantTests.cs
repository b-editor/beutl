using System.Collections.Immutable;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public sealed class SpecializationConstantTests
{
    [TestCase(TypeCode.Int64, true, false, TestName = "SixtyFourBitClassification_Int64NeedsTheIntegerFeature")]
    [TestCase(TypeCode.Double, false, true, TestName = "SixtyFourBitClassification_Float64NeedsTheFloatFeature")]
    [TestCase(TypeCode.UInt64, true, false, TestName = "AnUnsignedSixtyFourBitConstant_NeedsTheIntegerFeature")]
    public void SixtyFourBitConstants_NameTheFeatureTheyNeed(TypeCode type, bool integer, bool floating)
    {
        SpecializationConstant constant = type switch
        {
            TypeCode.Int64 => SpecializationConstant.Create(0, 1L, ShaderStage.Fragment),
            TypeCode.Double => SpecializationConstant.Create(0, 1d, ShaderStage.Fragment),
            _ => SpecializationConstant.Create(0, ulong.MaxValue, ShaderStage.Vertex),
        };

        Assert.Multiple(() =>
        {
            Assert.That(constant.SizeInBytes, Is.EqualTo(8));
            Assert.That(constant.RequiresShaderInt64, Is.EqualTo(integer));
            Assert.That(constant.RequiresShaderFloat64, Is.EqualTo(floating));
        });
    }

    [Test]
    public void ThirtyTwoBitConstants_NeedNoSixtyFourBitFeature()
    {
        SpecializationConstant[] constants =
        [
            SpecializationConstant.Create(0, true, ShaderStage.Fragment),
            SpecializationConstant.Create(1, -3, ShaderStage.Fragment),
            SpecializationConstant.Create(2, 3u, ShaderStage.Fragment),
            SpecializationConstant.Create(3, 1.5f, ShaderStage.Fragment),
        ];

        Assert.That(constants.Any(static item => item.RequiresShaderInt64 || item.RequiresShaderFloat64), Is.False);
    }

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
    [TestCase((ShaderStage)(1 << 2))]
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

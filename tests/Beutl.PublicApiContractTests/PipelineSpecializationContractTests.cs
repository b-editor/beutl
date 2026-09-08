using System.Reflection;
using Beutl.Graphics.Backend;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class PipelineSpecializationContractTests
{
    [Test]
    public void SettingPushConstants_OffersNoWayToNameTheStages()
    {
        MethodInfo setPushConstants = typeof(IRenderPass3D)
            .GetMethods()
            .Single(static method => method.Name == nameof(IRenderPass3D.SetPushConstants));

        Assert.That(
            setPushConstants.GetParameters().Select(static parameter => parameter.ParameterType),
            Is.EqualTo(new[] { setPushConstants.GetGenericArguments()[0] }),
            "the data is the only thing the caller decides");
    }

    [Test]
    public void ExternalAuthorCanDescribeImmutableTypedSpecializationConstants()
    {
        SpecializationConstant direction = SpecializationConstant.Create(
            3,
            1,
            ShaderStage.Vertex | ShaderStage.Fragment);
        SpecializationConstant ascending = SpecializationConstant.Create(
            4,
            true,
            ShaderStage.Fragment);
        SpecializationConstant opacity = SpecializationConstant.Create(
            5,
            0.625f,
            ShaderStage.Fragment);
        PipelineOptions options = PipelineOptions.Fullscreen;
        options.SpecializationConstants = [direction, ascending, opacity];
        Span<byte> directionValue = stackalloc byte[direction.SizeInBytes];
        Span<byte> ascendingValue = stackalloc byte[ascending.SizeInBytes];
        Span<byte> opacityValue = stackalloc byte[opacity.SizeInBytes];
        direction.CopyValueTo(directionValue);
        ascending.CopyValueTo(ascendingValue);
        opacity.CopyValueTo(opacityValue);
        int copiedDirection = BitConverter.ToInt32(directionValue);
        uint copiedAscending = BitConverter.ToUInt32(ascendingValue);
        float copiedOpacity = BitConverter.ToSingle(opacityValue);

        Assert.Multiple(() =>
        {
            Assert.That(options.SpecializationConstants, Has.Length.EqualTo(3));
            Assert.That(direction.ConstantId, Is.EqualTo(3));
            Assert.That(direction.Stages, Is.EqualTo(ShaderStage.Vertex | ShaderStage.Fragment));
            Assert.That(direction.SizeInBytes, Is.EqualTo(sizeof(int)));
            Assert.That(ascending.SizeInBytes, Is.EqualTo(sizeof(uint)));
            Assert.That(copiedDirection, Is.EqualTo(1));
            Assert.That(copiedAscending, Is.EqualTo(1));
            Assert.That(copiedOpacity, Is.EqualTo(0.625f));
            Assert.That(direction, Is.Not.EqualTo(SpecializationConstant.Create(3, 0, direction.Stages)));
        });
    }
}

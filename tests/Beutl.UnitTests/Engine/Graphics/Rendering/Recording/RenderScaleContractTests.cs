using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RenderScaleContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 80);

    [Test]
    public void MapInputSupply_MapsConcreteSupplyAndPreservesUnbounded()
    {
        RenderScaleContract contract = RenderScaleContract.MapInputSupply(
            DoubleSupply,
            structuralKey: "double-supply");

        Assert.Multiple(() =>
        {
            Assert.That(
                contract.Resolve([EffectiveScale.At(1.5f)], s_bounds, outputScale: 1, maxWorkingScale: 10),
                Is.EqualTo(EffectiveScale.At(3)));
            Assert.That(
                contract.Resolve([EffectiveScale.Unbounded], s_bounds, outputScale: 1, maxWorkingScale: 10),
                Is.EqualTo(EffectiveScale.Unbounded));
            Assert.That(
                contract.Resolve([EffectiveScale.At(3)], s_bounds, outputScale: 1, maxWorkingScale: 4),
                Is.EqualTo(EffectiveScale.At(4)));
        });
    }

    [Test]
    public void MapInputSupply_RequiresAnElementWiseSingleInputTopology()
    {
        RenderScaleContract contract = RenderScaleContract.MapInputSupply(
            static input => input,
            structuralKey: "identity-supply");

        Assert.Multiple(() =>
        {
            Assert.That(
                () => contract.Resolve([], s_bounds, outputScale: 1, maxWorkingScale: 4),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => contract.Resolve(
                    [EffectiveScale.At(1), EffectiveScale.At(2)],
                    s_bounds,
                    outputScale: 1,
                    maxWorkingScale: 4),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => contract.ThrowIfIncompatible(OpaqueRenderTopology.Map, "scale"),
                Throws.Nothing);
            Assert.That(
                () => contract.ThrowIfIncompatible(OpaqueRenderTopology.Source, "scale"),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void MapInputSupply_HasStableKindSpecificStructuralIdentity()
    {
        const string sharedKey = "shared-scale-key";
        RenderScaleContract first = RenderScaleContract.MapInputSupply(DoubleSupply, sharedKey);
        RenderScaleContract second = RenderScaleContract.MapInputSupply(DoubleSupply, sharedKey);
        RenderScaleContract custom = RenderScaleContract.Custom(
            static _ => 2,
            structuralKey: sharedKey);

        Assert.Multiple(() =>
        {
            Assert.That(first.StructuralIdentity, Is.EqualTo(second.StructuralIdentity));
            Assert.That(first.StructuralIdentity, Is.Not.EqualTo(custom.StructuralIdentity));
        });
    }

    [Test]
    public void MapInputSupply_RejectsMutableCallbackCapture()
    {
        var mutable = new List<float> { 2 };

        Assert.That(
            () => RenderScaleContract.MapInputSupply(
                input => input.IsUnbounded
                    ? EffectiveScale.Unbounded
                    : EffectiveScale.At(input.Value * mutable[0])),
            Throws.TypeOf<ArgumentException>());
    }

    private static EffectiveScale DoubleSupply(EffectiveScale input)
        => input.IsUnbounded
            ? EffectiveScale.Unbounded
            : EffectiveScale.At(input.Value * 2);
}

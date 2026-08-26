using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RenderScaleContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 80);

    [Test]
    public void MapInputSupplyPreservingDemand_MapsConcreteSupplyAndPreservesUnbounded()
    {
        RenderScaleContract contract = RenderScaleContract.MapInputSupplyPreservingDemand(
            DoubleSupply);

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
    public void MapInputSupplyPreservingDemand_RequiresAnElementWiseSingleInputTopology()
    {
        RenderScaleContract contract = RenderScaleContract.MapInputSupplyPreservingDemand(
            static input => input);

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
    public void MapInputSupplyPreservingDemand_HasStableKindSpecificStructuralIdentity()
    {
        RenderScaleContract first = RenderScaleContract.MapInputSupplyPreservingDemand(DoubleSupply);
        RenderScaleContract second = RenderScaleContract.MapInputSupplyPreservingDemand(DoubleSupply);
        RenderScaleContract custom = RenderScaleContract.Custom(
            static _ => 2);

        Assert.Multiple(() =>
        {
            Assert.That(first.StructuralIdentity, Is.EqualTo(second.StructuralIdentity));
            Assert.That(first.StructuralIdentity, Is.Not.EqualTo(custom.StructuralIdentity));
        });
    }

    // EffectiveScale.Unbounded is a get-only property this assembly reads as metadata, where a
    // recording callback cannot be shown what its getter answers; snapshotting it here can be.
    private static readonly EffectiveScale s_unbounded = EffectiveScale.Unbounded;

    private static EffectiveScale DoubleSupply(EffectiveScale input)
        => input.IsUnbounded
            ? s_unbounded
            : EffectiveScale.At(input.Value * 2);
}

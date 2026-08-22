using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RenderContractPrimitiveTests
{
    [Test]
    public void RenderValueCardinality_ProvidesInitializedCanonicalValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RenderValueCardinality.None.Minimum, Is.Zero);
            Assert.That(RenderValueCardinality.None.Maximum, Is.Zero);
            Assert.That(RenderValueCardinality.Single.Minimum, Is.EqualTo(1));
            Assert.That(RenderValueCardinality.Single.Maximum, Is.EqualTo(1));
            Assert.That(RenderValueCardinality.ZeroOrOne.Minimum, Is.Zero);
            Assert.That(RenderValueCardinality.ZeroOrOne.Maximum, Is.EqualTo(1));
            Assert.That(RenderValueCardinality.Dynamic.Minimum, Is.Zero);
            Assert.That(RenderValueCardinality.Dynamic.Maximum, Is.Null);
            Assert.That(RenderValueCardinality.Exactly(3), Is.EqualTo(RenderValueCardinality.Range(3, 3)));
        });
    }

    [Test]
    public void RenderValueCardinality_RejectsInvalidRangesAndDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => RenderValueCardinality.Exactly(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => RenderValueCardinality.Range(-1, null), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => RenderValueCardinality.Range(2, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => default(RenderValueCardinality).ThrowIfUninitialized("cardinality"),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("cardinality"));
        });
    }

    [Test]
    public void TargetRegion_SeparatesFullEmptyAndFiniteRegion()
    {
        var region = new Rect(10, 20, 30, 40);

        Assert.Multiple(() =>
        {
            Assert.That(TargetRegion.Full.Kind, Is.EqualTo(TargetRegionKind.Full));
            Assert.That(TargetRegion.Empty.Kind, Is.EqualTo(TargetRegionKind.Empty));
            Assert.That(TargetRegion.Region(region).Kind, Is.EqualTo(TargetRegionKind.Region));
            Assert.That(TargetRegion.Region(region).Value, Is.EqualTo(region));
            Assert.That(TargetRegion.Region(new Rect(10, 20, 0, 40)), Is.EqualTo(TargetRegion.Empty));
            Assert.That(TargetRegion.Region(new Rect(10, 20, 30, 0)), Is.EqualTo(TargetRegion.Empty));
        });
    }

    [Test]
    public void TargetRegion_RejectsInvalidNonFiniteNegativeAndDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => TargetRegion.Region(Rect.Invalid), Throws.TypeOf<ArgumentException>());
            Assert.That(() => TargetRegion.Region(new Rect(0, 0, float.PositiveInfinity, 1)), Throws.TypeOf<ArgumentException>());
            Assert.That(() => TargetRegion.Region(new Rect(0, 0, -1, 1)), Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => default(TargetRegion).ThrowIfUninitialized("region"),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("region"));
        });
    }

    [Test]
    public void RenderBoundsContract_IdentityAndFullInputHaveDistinctBackwardPolicy()
    {
        var bounds = new Rect(1, 2, 30, 40);

        Assert.Multiple(() =>
        {
            Assert.That(RenderBoundsContract.Identity.TransformBounds(bounds), Is.EqualTo(bounds));
            Assert.That(RenderBoundsContract.Identity.GetRequiredInputBounds(bounds), Is.EqualTo(bounds));
            Assert.That(RenderBoundsContract.Identity.RequiresFullInput, Is.False);
            Assert.That(RenderBoundsContract.FullInput.TransformBounds(bounds), Is.EqualTo(bounds));
            Assert.That(RenderBoundsContract.FullInput.GetRequiredInputBounds(bounds), Is.EqualTo(bounds));
            Assert.That(RenderBoundsContract.FullInput.RequiresFullInput, Is.True);
        });
    }

    [Test]
    public void RenderBoundsContract_CustomMapsAreValidated()
    {
        RenderBoundsContract contract = RenderBoundsContract.Create(
            static input => input.Inflate(new Thickness(2, 3)),
            static output => output.Inflate(new Thickness(4, 5)));
        var bounds = new Rect(10, 20, 30, 40);

        Assert.Multiple(() =>
        {
            Assert.That(contract.TransformBounds(bounds), Is.EqualTo(bounds.Inflate(new Thickness(2, 3))));
            Assert.That(contract.GetRequiredInputBounds(bounds), Is.EqualTo(bounds.Inflate(new Thickness(4, 5))));
            Assert.That(contract.RequiresFullInput, Is.False);
            Assert.That(
                RenderBoundsContract.CreateFullInput(static input => input.Translate(new Vector(3, 4)))
                    .RequiresFullInput,
                Is.True);
            Assert.That(
                () => RenderBoundsContract.Create(static _ => Rect.Invalid, static value => value)
                    .TransformBounds(bounds),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => RenderBoundsContract.Create(static value => value, static _ => Rect.Invalid)
                    .GetRequiredInputBounds(bounds),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void RenderBoundsContract_RejectsNullDelegatesAndDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => RenderBoundsContract.Create(null!, static value => value),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => RenderBoundsContract.Create(static value => value, null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => RenderBoundsContract.CreateFullInput(null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => default(RenderBoundsContract).TransformBounds(Rect.Empty),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => default(RenderBoundsContract).ThrowIfUninitialized("bounds"),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("bounds"));
        });
    }

    [Test]
    public void RenderBoundsContract_RejectsMetadataCallbacksThatCaptureLifetimeState()
    {
        using var retained = new MemoryStream();
        Func<Rect, Rect> capturing = value =>
        {
            _ = retained.Position;
            return value;
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                () => RenderBoundsContract.Create(
                    capturing,
                    static value => value),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => RenderBoundsContract.Create(
                    static value => value,
                    capturing),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => RenderBoundsContract.CreateFullInput(
                    capturing),
                Throws.TypeOf<ArgumentException>());
        });
    }

    private sealed class DerivedMutableKey : List<int>;

    private sealed record ImmutableIdentity(string Name, int Version);
}

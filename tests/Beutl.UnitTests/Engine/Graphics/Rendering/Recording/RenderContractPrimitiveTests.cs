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
    public void RenderBoundsContract_ExplicitKeyRetainsBoundsPolicyKind()
    {
        RenderBoundsContract tight = RenderBoundsContract.Create(
            static value => value,
            static value => value,
            structuralKey: "shared");
        RenderBoundsContract full = RenderBoundsContract.CreateFullInput(
            static value => value,
            structuralKey: "shared");
        RenderBoundsContract equivalentTight = RenderBoundsContract.Create(
            static value => value.Translate(new Vector(1, 0)),
            static value => value.Translate(new Vector(-1, 0)),
            structuralKey: "shared");

        Assert.Multiple(() =>
        {
            Assert.That(tight.StructuralIdentity, Is.EqualTo(equivalentTight.StructuralIdentity));
            Assert.That(tight.StructuralIdentity, Is.Not.EqualTo(full.StructuralIdentity));
        });
    }

    [Test]
    public void RenderBoundsContract_RejectsKeysThatCannotBeRetainedSafely()
    {
        int captured = 1;
        Func<int> closure = () => captured;
        Func<int> staticMethod = static () => 1;
        var mutableValues = new List<int> { 1 };

        Assert.Multiple(() =>
        {
            Assert.That(
                () => RenderBoundsContract.Create(
                    static value => value,
                    static value => value,
                    structuralKey: new byte[4]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => RenderBoundsContract.CreateFullInput(
                    static value => value,
                    structuralKey: closure),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => RenderBoundsContract.CreateFullInput(
                    static value => value,
                    structuralKey: staticMethod),
                Throws.Nothing);
            Assert.That(
                () => RenderBoundsContract.CreateFullInput(
                    static value => value,
                    structuralKey: new DerivedMutableKey { 1 }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => RenderBoundsContract.CreateFullInput(
                    static value => value,
                    structuralKey: mutableValues.AsReadOnly()),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => RenderBoundsContract.CreateFullInput(
                    static value => value,
                    structuralKey: new ImmutableIdentity("safe", 1)),
                Throws.Nothing);
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
                    static value => value,
                    structuralKey: "capturing-forward"),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => RenderBoundsContract.Create(
                    static value => value,
                    capturing,
                    structuralKey: "capturing-backward"),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => RenderBoundsContract.CreateFullInput(
                    capturing,
                    structuralKey: "capturing-full-input"),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void RenderScaleUtilities_SanitizesAndResolvesConcreteSupply()
    {
        EffectiveScale[] inputs =
        [
            EffectiveScale.Unbounded,
            EffectiveScale.At(1.5f),
            EffectiveScale.At(2.25f),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(RenderScaleUtilities.SanitizeMaxWorkingScale(float.NaN), Is.EqualTo(float.PositiveInfinity));
            Assert.That(RenderScaleUtilities.SanitizeMaxWorkingScale(0), Is.EqualTo(float.PositiveInfinity));
            Assert.That(RenderScaleUtilities.SanitizeMaxWorkingScale(-1), Is.EqualTo(float.PositiveInfinity));
            Assert.That(RenderScaleUtilities.SanitizeMaxWorkingScale(3), Is.EqualTo(3));
            Assert.That(RenderScaleUtilities.ResolveWorkingScale(inputs, 1), Is.EqualTo(2.25f));
            Assert.That(RenderScaleUtilities.ResolveWorkingScale(inputs, 3), Is.EqualTo(3));
            Assert.That(RenderScaleUtilities.ResolveWorkingScale(inputs, 1, 2), Is.EqualTo(2));
            Assert.That(RenderScaleUtilities.ResolveWorkingScale(inputs, float.NaN), Is.EqualTo(2.25f));
        });
    }

    [Test]
    public void RenderScaleUtilities_ClampsEachAxisToBufferBudget()
    {
        float clamped = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            new Rect(0, 0, 10_000, 100),
            workingScale: 2);

        Assert.Multiple(() =>
        {
            Assert.That(Math.Ceiling(10_000 * clamped), Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(clamped, Is.LessThan(2));
            Assert.That(
                RenderScaleUtilities.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 100, 100), 2),
                Is.EqualTo(2));
            Assert.That(
                () => RenderScaleUtilities.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 100, 100), 2, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    private sealed class DerivedMutableKey : List<int>;

    private sealed record ImmutableIdentity(string Name, int Version);
}

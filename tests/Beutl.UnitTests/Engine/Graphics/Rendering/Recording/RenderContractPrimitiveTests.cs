using System.Collections.Immutable;
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

    /// <remarks>
    /// A metadata callback is evaluated repeatedly and its structural identity is only its MethodInfo, so a
    /// capture the author can still change makes one identity stand for different bounds. The named mutable
    /// collections were the only shape rejected; an ordinary class with a settable field does it too.
    /// </remarks>
    [Test]
    public void RenderBoundsContract_RejectsAMetadataCallbackThatCapturesAnAssignableField()
    {
        var box = new MutableBox { Value = new Rect(0, 0, 4, 4) };

        Assert.That(
            () => RenderBoundsContract.Create(_ => box.Value, static value => value),
            Throws.TypeOf<ArgumentException>().With.InnerException.Message.Contains("MutableBox.Value"));
    }

    [Test]
    public void RenderBoundsContract_RejectsACaptureWhoseReadOnlyFieldHoldsSomethingAssignable()
    {
        var nested = new FixedBox(new MutableBox { Value = new Rect(0, 0, 4, 4) });

        Assert.That(
            () => RenderBoundsContract.Create(_ => nested.Inner.Value, static value => value),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void RenderBoundsContract_AcceptsACaptureNothingCanReassign()
    {
        var fixedValue = new FixedRect(new Rect(0, 0, 4, 4));

        Assert.That(
            () => RenderBoundsContract.Create(_ => fixedValue.Value, static value => value),
            Throws.Nothing);
    }

    [Test]
    public void RenderBoundsContract_AcceptsACaptureHoldingAnImmutableCollection()
    {
        var fixedValue = new FixedRects([new Rect(0, 0, 4, 4)]);

        Assert.That(
            () => RenderBoundsContract.Create(_ => fixedValue.Values[0], static value => value),
            Throws.Nothing);
    }

    /// <remarks>
    /// What an immutable collection fixes is the collection, not what it holds. An element the author can
    /// still assign reads through the array exactly as it reads through a field.
    /// </remarks>
    [Test]
    public void RenderBoundsContract_RejectsACaptureHoldingAnImmutableCollectionOfMutableElements()
    {
        var boxes = ImmutableArray.Create(new MutableBox { Value = new Rect(0, 0, 4, 4) });

        Assert.That(
            () => RenderBoundsContract.Create(_ => boxes[0].Value, static value => value),
            Throws.TypeOf<ArgumentException>());
    }

    /// <remarks>
    /// A field a base type declares privately is not returned by asking the derived type for its fields, so
    /// an object whose changing state lives one level up would reach the walk with nothing to check.
    /// </remarks>
    [Test]
    public void RenderBoundsContract_RejectsACaptureWhoseStateLivesInItsBase()
    {
        var derived = new DerivedBox();
        derived.Set(new Rect(0, 0, 4, 4));

        Assert.That(
            () => RenderBoundsContract.Create(_ => derived.Value, static value => value),
            Throws.TypeOf<ArgumentException>());
    }

    /// <remarks>
    /// A ReadOnlyMemory is a read-only view, not an immutable value: the array it ordinarily wraps stays in
    /// the author's hands and can be written after the callback is recorded.
    /// </remarks>
    [Test]
    public void RenderBoundsContract_RejectsACaptureViewingAnArrayItCannotOwn()
    {
        ReadOnlyMemory<float> view = new float[] { 4f }.AsMemory();

        Assert.That(
            () => RenderBoundsContract.Create(
                _ => new Rect(0, 0, view.Span[0], view.Span[0]),
                static value => value),
            Throws.TypeOf<ArgumentException>());
    }

    /// <remarks>
    /// The same view over a string is accepted, because what it points at cannot be written either.
    /// </remarks>
    [Test]
    public void RenderBoundsContract_AcceptsACaptureViewingSomethingFixed()
    {
        ReadOnlyMemory<char> view = "4".AsMemory();

        Assert.That(
            () => RenderBoundsContract.Create(
                _ => new Rect(0, 0, view.Span[0], view.Span[0]),
                static value => value),
            Throws.Nothing);
    }

    /// <remarks>
    /// Roslyn caches an inner lambda in the closure it shares with the enclosing one, so a contract recorded
    /// from inside any other lambda reaches validation with a delegate field the author never wrote.
    /// </remarks>
    [Test]
    public void RenderBoundsContract_AcceptsAContractRecordedInsideAnotherLambda()
    {
        var fixedValue = new FixedRect(new Rect(0, 0, 4, 4));
        Func<RenderBoundsContract> record =
            () => RenderBoundsContract.Create(_ => fixedValue.Value, static value => value);

        Assert.That(() => record(), Throws.Nothing);
    }

    /// <remarks>
    /// The compiler's own cache is recognised by pointing back at the closure being validated, so a delegate
    /// the author really did capture - one built elsewhere, over state this closure cannot show - still fails.
    /// </remarks>
    [Test]
    public void RenderBoundsContract_RejectsADelegateCapturedFromAnotherClosure()
    {
        Func<Rect, Rect> elsewhere = ReadFrom(new MutableBox { Value = new Rect(0, 0, 4, 4) });

        Assert.That(
            () => RenderBoundsContract.Create(value => elsewhere(value), static value => value),
            Throws.TypeOf<ArgumentException>());
    }

    private static Func<Rect, Rect> ReadFrom(MutableBox box) => _ => box.Value;

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

    private sealed class MutableBox
    {
        public Rect Value;
    }

    private class BoxBase
    {
        private Rect _value;

        public Rect Value => _value;

        public void Set(Rect value) => _value = value;
    }

    private sealed class DerivedBox : BoxBase;

    private sealed class FixedBox(MutableBox inner)
    {
        public readonly MutableBox Inner = inner;
    }

    private sealed class FixedRect(Rect value)
    {
        public readonly Rect Value = value;
    }

    private sealed class FixedRects(ImmutableArray<Rect> values)
    {
        public readonly ImmutableArray<Rect> Values = values;
    }

    private sealed class DerivedMutableKey : List<int>;

    private sealed record ImmutableIdentity(string Name, int Version);
}

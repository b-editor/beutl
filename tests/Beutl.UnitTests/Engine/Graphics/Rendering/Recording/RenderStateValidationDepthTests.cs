using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins how deep a description's <c>state</c> is validated: through <c>ValueTuple</c>/<c>Tuple</c> elements to
/// any nesting depth, and no further.
/// </summary>
[TestFixture]
public sealed class RenderStateValidationDepthTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void ARootCapturingCallbackIsStillRejected()
    {
        var color = Colors.Red;
        Func<Color> capturing = () => color;

        Assert.That(
            () => Create(capturing),
            Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"));
    }

    [Test]
    public void ACapturingDelegateInsideTheStateTupleIsRejected()
    {
        var color = Colors.Red;
        Func<Color> capturing = () => color;

        ArgumentException? rejection = Assert.Throws<ArgumentException>(() => Create((1, capturing)));

        TestContext.Out.WriteLine(rejection!.Message);
        Assert.Multiple(() =>
        {
            Assert.That(rejection.ParamName, Is.EqualTo("state"));
            Assert.That(rejection.Message, Does.Contain("Item2"),
                "the message must name the element that carries the value");
        });
    }

    [Test]
    public void ANonCapturingDelegateInsideTheStateTupleIsAccepted()
    {
        Assert.That(() => Create((1, (Func<Color>)(static () => Colors.Red))), Throws.Nothing);
    }

    [Test]
    public void AMutableCollectionInsideTheStateTupleIsRejected()
    {
        ArgumentException? rejection = Assert.Throws<ArgumentException>(() => Create((1, new List<int>())));

        TestContext.Out.WriteLine(rejection!.Message);
        Assert.That(rejection.ParamName, Is.EqualTo("state"));
    }

    [Test]
    public void ADisposableInsideTheStateTupleIsRejected()
    {
        using var disposable = new DisposablePayload();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => Create((1, disposable)),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"),
                "a concretely typed disposable element is decided from the element type");
            Assert.That(
                () => Create((1, (IDisposable)disposable)),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"),
                "an interface-typed element is decided from the value it holds");
        });
    }

    [Test]
    public void AnExecutionFacadeInsideTheStateTupleIsRejected()
    {
        ArgumentException? rejection = Assert.Throws<ArgumentException>(
            () => Create((1, (OpaqueRenderSession?)null, 2)));

        TestContext.Out.WriteLine(rejection!.Message);
        Assert.Multiple(() =>
        {
            Assert.That(rejection.ParamName, Is.EqualTo("state"));
            Assert.That(rejection.Message, Does.Contain("execution session or facade"));
        });
    }

    [Test]
    public void RejectionReachesEveryNestingLevel()
    {
        using var disposable = new DisposablePayload();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => Create((1, (2, new List<int>()))),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"));
            Assert.That(
                () => Create((1, (2, (3, disposable)))),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"));
            Assert.That(
                () => Create((1, 2, 3, 4, 5, 6, 7, new List<int>())),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"),
                "an eight-element tuple carries its eighth element in a flattened rest chain");
        });
    }

    /// <summary>
    /// The documented stopping point. A custom aggregate is validated as one value, so it stays an enumerated
    /// identity channel rather than a rejection.
    /// </summary>
    [Test]
    public void AHolderObjectIsValidatedAsOneValueAndAccepted()
    {
        var color = Colors.Red;
        var holder = new DelegateHolder(() => color);

        Assert.Multiple(() =>
        {
            Assert.That(() => Create(holder), Throws.Nothing);
            Assert.That(() => Create((1, holder)), Throws.Nothing);
        });
    }

    [Test]
    public void TheProductionStateShapesStayAccepted()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Create((s_bounds, ClipOperation.Intersect)), Throws.Nothing);
            Assert.That(() => Create((Guid.NewGuid(), 3, ClipOperation.Difference)), Throws.Nothing);
            Assert.That(() => Create(("pixels", 4)), Throws.Nothing);
            Assert.That(() => Create(typeof(RenderStateValidationDepthTests)), Throws.Nothing);
        });
    }

    private static OpaqueRenderDescription Create<TState>(TState state)
        where TState : notnull
        => OpaqueRenderDescription.Create(
            state,
            static (_, _) => { },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);

    private sealed class DisposablePayload : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class DelegateHolder(Func<Color> read)
    {
        public Color Read() => read();
    }
}

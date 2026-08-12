using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class FilterEffectRecordingTransactionTests
{
    private const string IdentityShader = "half4 apply(half4 color) { return color; }";

    [Test]
    public void ShaderAndGeometry_UpdateBoundsSynchronouslyInAuthoredOrder()
    {
        using var context = new FilterEffectContext(new Rect(10, 20, 30, 40));
        ShaderDescription currentPixel = ShaderDescription.CurrentPixel(IdentityShader);
        ShaderDescription wholeSource = ShaderDescription.WholeSource(
            "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
            RenderBoundsContract.Create(
                static bounds => bounds.Inflate(new Thickness(2)),
                static bounds => bounds.Inflate(new Thickness(2))));
        GeometryDescription geometry = GeometryDescription.CreateRequestLocal(
            static _ => { },
            RenderBoundsContract.Create(
                static bounds => bounds.Translate(new Vector(3, 4)),
                static bounds => bounds.Translate(new Vector(-3, -4))),
            RenderHitTestContract.AnyInput);

        context.Shader(currentPixel);
        Rect afterCurrentPixel = context.Bounds;
        context.Shader(wholeSource);
        Rect afterWholeSource = context.Bounds;
        context.Geometry(geometry);

        Assert.Multiple(() =>
        {
            Assert.That(afterCurrentPixel, Is.EqualTo(new Rect(10, 20, 30, 40)));
            Assert.That(afterWholeSource, Is.EqualTo(new Rect(8, 18, 34, 44)));
            Assert.That(context.Bounds, Is.EqualTo(new Rect(11, 22, 34, 44)));
            Assert.That(
                context.GetOrderedItems().Select(static item => item.GetType()),
                Is.EqualTo(new[]
                {
                    typeof(FEItem_Shader),
                    typeof(FEItem_Shader),
                    typeof(FEItem_Geometry),
                }));
        });
    }

    [Test]
    public void InvalidOrThrowingDescriptorAppend_IsAtomic()
    {
        using var context = new FilterEffectContext(new Rect(0, 0, 20, 10));
        context.Saturate(0.5f);
        int originalCount = context.GetOrderedItems().Count;
        Rect originalBounds = context.Bounds;
        ShaderDescription throwing = ShaderDescription.WholeSource(
            "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
            RenderBoundsContract.Create(
                static _ => throw new InvalidOperationException("bounds-failure"),
                static bounds => bounds));
        GeometryDescription invalid = GeometryDescription.CreateRequestLocal(
            static _ => { },
            RenderBoundsContract.Create(
                static _ => Rect.Invalid,
                static bounds => bounds),
            RenderHitTestContract.AnyInput);

        Assert.Multiple(() =>
        {
            Assert.That(() => context.Shader(throwing), Throws.Exception.Message.EqualTo("bounds-failure"));
            Assert.That(context.GetOrderedItems(), Has.Count.EqualTo(originalCount));
            Assert.That(context.Bounds, Is.EqualTo(originalBounds));
            Assert.That(() => context.Geometry(invalid), Throws.TypeOf<InvalidOperationException>());
            Assert.That(context.GetOrderedItems(), Has.Count.EqualTo(originalCount));
            Assert.That(context.Bounds, Is.EqualTo(originalBounds));
        });
    }

    [Test]
    public void ThrowingLegacyTransformAppend_IsAtomic()
    {
        using var context = new FilterEffectContext(new Rect(0, 0, 20, 10));
        context.Saturate(0.5f);
        int originalCount = context.GetOrderedItems().Count;
        Rect originalBounds = context.Bounds;

        Assert.Multiple(() =>
        {
            Assert.That(
                () => context.AppendSkiaFilter(
                    0,
                    static (_, input, _) => input,
                    static (_, _) => throw new InvalidOperationException("skia-bounds-failure")),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("skia-bounds-failure"));
            Assert.That(context.GetOrderedItems(), Has.Count.EqualTo(originalCount));
            Assert.That(context.Bounds, Is.EqualTo(originalBounds));
            Assert.That(
                () => context.CustomEffect(
                    0,
                    static (_, _) => { },
                    static (_, _) => throw new InvalidOperationException("custom-bounds-failure")),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("custom-bounds-failure"));
            Assert.That(context.GetOrderedItems(), Has.Count.EqualTo(originalCount));
            Assert.That(context.Bounds, Is.EqualTo(originalBounds));
        });
    }

    [Test]
    public void NestedApplyTransaction_RollsBackEarlierChildrenWhenLaterChildFails()
    {
        using var context = new FilterEffectContext(new Rect(0, 0, 10, 10));
        FilterEffect.Resource resource = new Blur().ToResource(CompositionContext.Default);
        var first = new CallbackFilterEffect((recording, _) =>
            recording.Shader(ShaderDescription.CurrentPixel(IdentityShader)));
        var second = new CallbackFilterEffect((recording, _) =>
        {
            recording.Geometry(GeometryDescription.CreateRequestLocal(
                static _ => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput));
            throw new InvalidOperationException("nested-failure");
        });
        var group = new CallbackFilterEffect((recording, childResource) =>
        {
            recording.ApplyTransactional(first, childResource);
            recording.ApplyTransactional(second, childResource);
        });

        Assert.That(
            () => context.ApplyTransactional(group, resource),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("nested-failure"));
        Assert.Multiple(() =>
        {
            Assert.That(context.GetOrderedItems(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(new Rect(0, 0, 10, 10)));
        });
    }

    [Test]
    public void FilterEffectGroup_DirectApplyRollsBackEarlierChildrenWhenLaterChildFails()
    {
        using var context = new FilterEffectContext(new Rect(0, 0, 10, 10));
        var firstResource = new TrackingDisposable();
        var secondResource = new TrackingDisposable();
        var first = new CallbackFilterEffect((recording, _) =>
        {
            recording.Own(firstResource);
            recording.Shader(ShaderDescription.CurrentPixel(IdentityShader));
        });
        var second = new CallbackFilterEffect((recording, _) =>
        {
            recording.Own(secondResource);
            recording.Geometry(GeometryDescription.CreateRequestLocal(
                static _ => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput));
            throw new InvalidOperationException("group-child-failure");
        });
        var group = new FilterEffectGroup { Children = { first, second } };
        FilterEffect.Resource groupResource = group.ToResource(CompositionContext.Default);

        Assert.That(
            () => group.ApplyTo(context, groupResource),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("group-child-failure"));
        Assert.Multiple(() =>
        {
            Assert.That(context.GetOrderedItems(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(new Rect(0, 0, 10, 10)));
            Assert.That(firstResource.DisposeCount, Is.EqualTo(1));
            Assert.That(secondResource.DisposeCount, Is.EqualTo(1));
        });

        context.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(firstResource.DisposeCount, Is.EqualTo(1));
            Assert.That(secondResource.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ApplyTransaction_RenderNodeBoundaryContinuesCleanupAndPreservesPrimaryFailure()
    {
        using var owner = new RenderRequestOwner();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            owner: owner);
        using var request = new RenderRequest(options);
        var recorder = new RenderRequestRecorder(request);
        var transaction = new NodeRecordingTransaction(recorder, new object(), []);
        var renderContext = new RenderNodeContext(transaction);
        using var context = new FilterEffectContext(new Rect(0, 0, 10, 10), 1, 1, renderContext);
        var earlier = new TrackingDisposable();
        var later = new ThrowingDisposable();
        var primary = new InvalidOperationException("primary-apply-failure");
        var effect = new CallbackFilterEffect((recording, _) =>
        {
            recording.Own(earlier);
            recording.Own(later);
            recording.Shader(ShaderDescription.CurrentPixel(IdentityShader));
            throw primary;
        });
        FilterEffect.Resource resource = new Blur().ToResource(CompositionContext.Default);

        InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(
            () => context.ApplyTransactional(effect, resource));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(primary));
            Assert.That(context.GetOrderedItems(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(new Rect(0, 0, 10, 10)));
            Assert.That(earlier.DisposeCount, Is.EqualTo(1));
            Assert.That(later.DisposeCount, Is.EqualTo(1));
            Assert.That(
                primary.Data["FilterEffectResourceRollbackFailure"],
                Is.TypeOf<AggregateException>());
            Assert.That(owner.CleanupFailures, Has.Length.EqualTo(1));
            Assert.That(owner.CleanupFailures[0].Message, Is.EqualTo("cleanup-failure"));
        });

        Assert.That(() => transaction.Commit(), Throws.Nothing);
        owner.Cleanup();
        Assert.Multiple(() =>
        {
            Assert.That(earlier.DisposeCount, Is.EqualTo(1));
            Assert.That(later.DisposeCount, Is.EqualTo(1));
        });
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            throw new InvalidOperationException("cleanup-failure");
        }
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class CallbackFilterEffect(
    Action<FilterEffectContext, FilterEffect.Resource> apply) : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        => apply(context, resource);

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public Resource()
        {
        }
    }
}

internal static class FilterEffectRecordingTransactionSlots
{
    internal static readonly RenderResourceSlot<object> Shared = new();
}

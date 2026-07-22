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
                static bounds => bounds.Inflate(new Thickness(2)),
                "inflate-two"));
        GeometryDescription geometry = GeometryDescription.Create(
            static _ => { },
            RenderBoundsContract.Create(
                static bounds => bounds.Translate(new Vector(3, 4)),
                static bounds => bounds.Translate(new Vector(-3, -4)),
                "translate"),
            RenderHitTestContract.AnyInput,
            structuralKey: "geometry");

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
                static bounds => bounds,
                "throwing-bounds"));
        GeometryDescription invalid = GeometryDescription.Create(
            static _ => { },
            RenderBoundsContract.Create(
                static _ => Rect.Invalid,
                static bounds => bounds,
                "invalid-bounds"),
            RenderHitTestContract.AnyInput,
            structuralKey: "invalid-geometry");

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
    public void ApplyTransaction_RollsBackItemsBoundsAndOwnedResourcesExactlyOnce()
    {
        using var context = new FilterEffectContext(new Rect(0, 0, 10, 10));
        context.Brightness(0.25f);
        var disposable = new TrackingDisposable();
        var effect = new CallbackFilterEffect((recording, _) =>
        {
            recording.Own(disposable, "owned", 1);
            recording.Shader(ShaderDescription.CurrentPixel(IdentityShader));
            recording.Blur(new Size(3, 3));
            throw new InvalidOperationException("apply-failure");
        });
        FilterEffect.Resource resource = new Blur().ToResource(CompositionContext.Default);
        Rect originalBounds = context.Bounds;
        int originalCount = context.GetOrderedItems().Count;

        Assert.That(
            () => context.ApplyTransactional(effect, resource),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("apply-failure"));
        Assert.Multiple(() =>
        {
            Assert.That(context.Bounds, Is.EqualTo(originalBounds));
            Assert.That(context.GetOrderedItems(), Has.Count.EqualTo(originalCount));
            Assert.That(disposable.DisposeCount, Is.EqualTo(1));
        });

        context.Dispose();
        Assert.That(disposable.DisposeCount, Is.EqualTo(1));
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
            recording.Geometry(GeometryDescription.Create(
                static _ => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: "nested-geometry"));
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
            recording.Own(firstResource, "first-owned", 1);
            recording.Shader(ShaderDescription.CurrentPixel(IdentityShader));
        });
        var second = new CallbackFilterEffect((recording, _) =>
        {
            recording.Own(secondResource, "second-owned", 1);
            recording.Geometry(GeometryDescription.Create(
                static _ => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: "group-geometry"));
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
            recording.Own(earlier, "earlier", 1);
            recording.Own(later, "later", 1);
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
        });

        Assert.That(() => transaction.Commit(), Throws.Nothing);
        owner.Cleanup();
        Assert.Multiple(() =>
        {
            Assert.That(earlier.DisposeCount, Is.EqualTo(1));
            Assert.That(later.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void CloneAndChild_ShareResourceFamilyButKeepDocumentedItemSemantics()
    {
        using var context = new FilterEffectContext(new Rect(1, 2, 30, 40));
        var borrowed = new object();
        RenderResource<object> token = context.Borrow(borrowed, "shared", 1);
        context.Shader(ShaderDescription.CurrentPixel(IdentityShader));
        using FilterEffectContext clone = context.Clone();
        using FilterEffectContext child = context.CreateChildContext();

        GeometryDescription declared = GeometryDescription.Create(
            session => session.UseResource(token, static _ => { }),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            structuralKey: "shared-resource",
            resources: [token]);
        clone.Geometry(declared);
        child.Geometry(declared);

        Assert.Multiple(() =>
        {
            Assert.That(clone.OriginalBounds, Is.EqualTo(context.OriginalBounds));
            Assert.That(clone.GetOrderedItems(), Has.Count.EqualTo(2));
            Assert.That(child.OriginalBounds, Is.EqualTo(context.Bounds));
            Assert.That(child.GetOrderedItems(), Has.Count.EqualTo(1));
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

    public new sealed class Resource : FilterEffect.Resource;
}

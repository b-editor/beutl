using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public sealed class ManualResourceOwnershipGetterTests
{
    [Test]
    public async Task DrawableGroup_ChildrenRejectsConcurrentUpdateThenPublishesCompletedSnapshot()
    {
        var first = new GetterProbeDrawable();
        var blocking = new GetterProbeDrawable { BlockNextAcquisition = true };
        var group = new DrawableGroup();
        group.Children.Add(first);
        var resource = (DrawableGroup.Resource)group.ToResource(CompositionContext.Default);
        IReadOnlyList<Drawable.Resource> retainedSnapshot = resource.Children;
        group.Children.Add(blocking);

        Task<Exception?> updateTask = Task.Run(() => UpdateAndCapture(resource, group));

        Assert.That(blocking.AcquisitionEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(() => _ = resource.Children);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(retainedSnapshot, Has.Count.EqualTo(1));
                Assert.That(retainedSnapshot[0].GetOriginal(), Is.SameAs(first));
            });
        }
        finally
        {
            blocking.ContinueAcquisition.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        IReadOnlyList<Drawable.Resource> completedSnapshot = resource.Children;

        Assert.Multiple(() =>
        {
            Assert.That(completedSnapshot, Has.Count.EqualTo(2));
            Assert.That(completedSnapshot, Is.Not.SameAs(retainedSnapshot));
            Assert.That(completedSnapshot[1].GetOriginal(), Is.SameAs(blocking));
            Assert.That(retainedSnapshot, Has.Count.EqualTo(1),
                "publishing the completed update must not mutate a retained snapshot");
        });

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Children);
            Assert.That(completedSnapshot, Has.Count.EqualTo(2));
            Assert.That(completedSnapshot.All(child => child.IsDisposed), Is.True);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(blocking.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DelayAnimationEffect_OwnershipGettersRejectConcurrentCleanupAndDisposedState()
    {
        var child = new GetterBlockingDisposeFilterEffect();
        var effect = new DelayAnimationEffect();
        effect.Effect.CurrentValue = child;
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);
        FilterEffect.Resource retainedEffect = resource.Effect!;
        FilterEffect.Resource delayed = resource.GetOrCreateDelayedResource(4, child, CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> retainedSnapshot = resource.DelayedResources;

        Task<Exception?> cleanupTask = Task.Run(() => DisposeAndCapture(resource));

        Assert.That(child.DisposeEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() => _ = resource.Delay);
                Assert.Throws<InvalidOperationException>(() => _ = resource.Effect);
                Assert.Throws<InvalidOperationException>(() => _ = resource.GlobalTime);
                Assert.Throws<InvalidOperationException>(() => _ = resource.DisableResourceShare);
                Assert.Throws<InvalidOperationException>(() => _ = resource.PreferProxy);
                Assert.Throws<InvalidOperationException>(() => _ = resource.PreferredProxyPreset);
                Assert.Throws<InvalidOperationException>(() => _ = resource.DelayedResources);
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(retainedSnapshot, Has.Count.EqualTo(1));
                Assert.That(retainedSnapshot[4], Is.SameAs(delayed));
            });
        }
        finally
        {
            child.ContinueDispose.Set();
        }

        Assert.That(await cleanupTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Delay);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Effect);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.GlobalTime);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.DisableResourceShare);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.PreferProxy);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.PreferredProxyPreset);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.DelayedResources);
            Assert.That(retainedEffect.IsDisposed, Is.True);
            Assert.That(retainedSnapshot, Has.Count.EqualTo(1));
            Assert.That(retainedSnapshot[4], Is.SameAs(delayed));
            Assert.That(delayed.IsDisposed, Is.True);
            Assert.That(child.DisposeCount, Is.EqualTo(2));
        });
    }

    private static Exception? UpdateAndCapture(EngineObject.Resource resource, EngineObject owner)
    {
        try
        {
            bool updateOnly = false;
            resource.Update(owner, CompositionContext.Default, ref updateOnly);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static Exception? DisposeAndCapture(EngineObject.Resource resource)
    {
        try
        {
            resource.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}

[SuppressResourceClassGeneration]
internal sealed class GetterProbeDrawable : Drawable
{
    public bool BlockNextAcquisition { get; set; }

    public ManualResetEventSlim AcquisitionEntered { get; } = new();

    public ManualResetEventSlim ContinueAcquisition { get; } = new();

    public int DisposeCount { get; private set; }

    public override Drawable.Resource ToResource(CompositionContext context)
    {
        if (BlockNextAcquisition)
        {
            BlockNextAcquisition = false;
            AcquisitionEntered.Set();
            if (!ContinueAcquisition.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked drawable resource acquisition was not resumed.");
        }

        var resource = new ProbeResource(this);
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => default;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    private sealed class ProbeResource(GetterProbeDrawable owner) : Drawable.Resource
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                owner.DisposeCount++;

            base.Dispose(disposing);
        }
    }
}

[SuppressResourceClassGeneration]
internal sealed class GetterBlockingDisposeFilterEffect : FilterEffect
{
    public ManualResetEventSlim DisposeEntered { get; } = new();

    public ManualResetEventSlim ContinueDispose { get; } = new();

    public int DisposeCount { get; private set; }

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override FilterEffect.Resource ToResource(CompositionContext context)
    {
        var resource = new ProbeResource(this);
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    private sealed class ProbeResource(GetterBlockingDisposeFilterEffect owner) : FilterEffect.Resource
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                owner.DisposeCount++;
                owner.DisposeEntered.Set();
                if (!owner.ContinueDispose.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The blocked filter-effect resource cleanup was not resumed.");
            }

            base.Dispose(disposing);
        }
    }
}

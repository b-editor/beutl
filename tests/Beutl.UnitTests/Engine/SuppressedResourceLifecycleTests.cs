using System.Reflection;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public sealed class SuppressedResourceLifecycleTests
{
    [Test]
    public async Task ParticleEmitter_DisposeWhileManualUpdateAcquiresChild_FailsWithoutMutatingOwnership()
    {
        var emitter = new ParticleEmitter();
        var resource = (ParticleEmitter.Resource)emitter.ToResource(CompositionContext.Default);
        var child = new SuppressedLifecycleBlockingAcquisitionDrawable();
        emitter.ParticleDrawable.CurrentValue = child;

        Task<Exception?> updateTask = Task.Run(() => UpdateAndCapture(resource, emitter));

        Assert.That(child.AcquisitionEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.Throws<InvalidOperationException>(() => _ = resource.ParticleDrawable);
                Assert.That(child.DisposeCount, Is.Zero);
            });
        }
        finally
        {
            child.ContinueAcquisition.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.That(resource.ParticleDrawable, Is.Not.Null);

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.ParticleDrawable);
            Assert.That(child.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ParticleEmitter_DisposeWhileOwnedChildUpdates_RollsBackAndCanRetry()
    {
        var child = new SuppressedLifecycleBlockingDrawable();
        var emitter = new ParticleEmitter();
        emitter.ParticleDrawable.CurrentValue = child;
        var resource = (ParticleEmitter.Resource)emitter.ToResource(CompositionContext.Default);
        var childResource = (SuppressedLifecycleBlockingDrawable.Resource)resource.ParticleDrawable!;
        child.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() => UpdateAndCapture(childResource, child));

        Assert.That(child.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(resource.ParticleDrawable, Is.SameAs(childResource));
                Assert.That(childResource.IsDisposed, Is.False);
                Assert.That(child.ResourceDisposeCount, Is.Zero);
            });
        }
        finally
        {
            child.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.ParticleDrawable);
            Assert.That(childResource.IsDisposed, Is.True);
            Assert.That(child.ResourceDisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DelayAnimationEffect_DisposeWhileManualUpdateAcquiresEffect_FailsWithoutMutatingOwnership()
    {
        var effect = new DelayAnimationEffect();
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);
        FilterEffect.Resource retainedEffect = resource.Effect!;
        var child = new SuppressedLifecycleBlockingAcquisitionFilterEffect();
        effect.Effect.CurrentValue = child;

        Task<Exception?> updateTask = Task.Run(() => UpdateAndCapture(resource, effect));

        Assert.That(child.AcquisitionEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.Throws<InvalidOperationException>(() => _ = resource.Effect);
                Assert.That(retainedEffect.IsDisposed, Is.False);
                Assert.That(child.DisposeCount, Is.Zero);
            });
        }
        finally
        {
            child.ContinueAcquisition.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.That(resource.Effect, Is.Not.SameAs(retainedEffect));
        Assert.That(retainedEffect.IsDisposed, Is.True);

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Effect);
            Assert.That(child.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DelayAnimationEffect_DisposeWhileDelayedResourceIsAcquired_FailsAndCanRetry()
    {
        var effect = new DelayAnimationEffect();
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> retainedSnapshot = resource.DelayedResources;
        var child = new SuppressedLifecycleBlockingAcquisitionFilterEffect();

        Task<Exception?> acquisitionTask = Task.Run(() =>
        {
            try
            {
                resource.GetOrCreateDelayedResource(7, child, CompositionContext.Default);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(child.AcquisitionEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.Throws<InvalidOperationException>(() => _ = resource.DelayedResources);
                Assert.That(retainedSnapshot, Is.Empty);
                Assert.That(child.DisposeCount, Is.Zero);
            });
        }
        finally
        {
            child.ContinueAcquisition.Set();
        }

        Assert.That(await acquisitionTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.That(resource.DelayedResources, Has.Count.EqualTo(1));

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.DelayedResources);
            Assert.That(child.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DelayAnimationEffect_DisposeWhileDelayedChildUpdates_RollsBackWholeGraphAndCanRetry()
    {
        var child = new SuppressedLifecycleBlockingFilterEffect();
        var effect = new DelayAnimationEffect();
        effect.Effect.CurrentValue = child;
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);
        FilterEffect.Resource retainedEffect = resource.Effect!;
        FilterEffect.Resource delayed = resource.GetOrCreateDelayedResource(3, child, CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> retainedSnapshot = resource.DelayedResources;
        child.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() => UpdateAndCapture(delayed, child));

        Assert.That(child.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(resource.Effect, Is.SameAs(retainedEffect));
                Assert.That(resource.DelayedResources, Is.SameAs(retainedSnapshot));
                Assert.That(retainedEffect.IsDisposed, Is.False);
                Assert.That(delayed.IsDisposed, Is.False);
                Assert.That(child.ResourceDisposeCount, Is.Zero);
            });
        }
        finally
        {
            child.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Effect);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.DelayedResources);
            Assert.That(retainedSnapshot, Has.Count.EqualTo(1));
            Assert.That(retainedEffect.IsDisposed, Is.True);
            Assert.That(delayed.IsDisposed, Is.True);
            Assert.That(child.ResourceDisposeCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task DelayAnimationEffect_PruneWhileDelayedChildUpdates_PreservesSnapshotAndCanRetry()
    {
        var child = new SuppressedLifecycleBlockingFilterEffect();
        var effect = new DelayAnimationEffect();
        effect.Effect.CurrentValue = child;
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);
        FilterEffect.Resource retainedEffect = resource.Effect!;
        FilterEffect.Resource stale = resource.GetOrCreateDelayedResource(1, child, CompositionContext.Default);
        FilterEffect.Resource live = resource.GetOrCreateDelayedResource(2, child, CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> retainedSnapshot = resource.DelayedResources;
        child.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() => UpdateAndCapture(stale, child));

        Assert.That(child.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                resource.PruneDelayedResources(new HashSet<int> { 2 }));
            Assert.Multiple(() =>
            {
                Assert.That(resource.Effect, Is.SameAs(retainedEffect));
                Assert.That(resource.DelayedResources, Is.SameAs(retainedSnapshot));
                Assert.That(retainedSnapshot, Has.Count.EqualTo(2));
                Assert.That(retainedSnapshot[1], Is.SameAs(stale));
                Assert.That(retainedSnapshot[2], Is.SameAs(live));
                Assert.That(retainedEffect.IsDisposed, Is.False);
                Assert.That(stale.IsDisposed, Is.False);
                Assert.That(live.IsDisposed, Is.False);
                Assert.That(child.ResourceDisposeCount, Is.Zero);
            });
        }
        finally
        {
            child.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        resource.PruneDelayedResources(new HashSet<int> { 2 });
        IReadOnlyDictionary<int, FilterEffect.Resource> completedSnapshot = resource.DelayedResources;

        Assert.Multiple(() =>
        {
            Assert.That(completedSnapshot, Is.Not.SameAs(retainedSnapshot));
            Assert.That(completedSnapshot.Keys, Is.EquivalentTo(new[] { 2 }));
            Assert.That(completedSnapshot[2], Is.SameAs(live));
            Assert.That(retainedSnapshot, Has.Count.EqualTo(2));
            Assert.That(retainedEffect.IsDisposed, Is.False);
            Assert.That(stale.IsDisposed, Is.True);
            Assert.That(live.IsDisposed, Is.False);
            Assert.That(child.ResourceDisposeCount, Is.EqualTo(1));
        });

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(retainedEffect.IsDisposed, Is.True);
            Assert.That(live.IsDisposed, Is.True);
            Assert.That(child.ResourceDisposeCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task DelayAnimationEffect_ReplacementWhileDelayedChildUpdates_RollsBackAndCanRetry()
    {
        var oldChild = new SuppressedLifecycleBlockingFilterEffect();
        var replacement = new SuppressedLifecycleBlockingAcquisitionFilterEffect();
        replacement.ContinueAcquisition.Set();
        var effect = new DelayAnimationEffect();
        effect.Effect.CurrentValue = oldChild;
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);
        FilterEffect.Resource retainedEffect = resource.Effect!;
        FilterEffect.Resource delayed = resource.GetOrCreateDelayedResource(3, oldChild, CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> retainedSnapshot = resource.DelayedResources;
        oldChild.BlockNextUpdate = true;

        Task<Exception?> delayedUpdateTask = Task.Run(() => UpdateAndCapture(delayed, oldChild));

        Assert.That(oldChild.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        effect.Effect.CurrentValue = replacement;
        try
        {
            Exception? failure = UpdateAndCapture(resource, effect);

            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                Assert.That(resource.Effect, Is.SameAs(retainedEffect));
                Assert.That(resource.DelayedResources, Is.SameAs(retainedSnapshot));
                Assert.That(retainedSnapshot, Has.Count.EqualTo(1));
                Assert.That(retainedSnapshot[3], Is.SameAs(delayed));
                Assert.That(retainedEffect.IsDisposed, Is.False);
                Assert.That(delayed.IsDisposed, Is.False);
                Assert.That(oldChild.ResourceDisposeCount, Is.Zero);
                Assert.That(replacement.DisposeCount, Is.EqualTo(1),
                    "the unpublished replacement must be reclaimed when graph reservation fails");
            });
        }
        finally
        {
            oldChild.ContinueUpdate.Set();
        }

        Assert.That(await delayedUpdateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        Assert.That(UpdateAndCapture(resource, effect), Is.Null);
        FilterEffect.Resource completedEffect = resource.Effect!;
        IReadOnlyDictionary<int, FilterEffect.Resource> completedSnapshot = resource.DelayedResources;

        Assert.Multiple(() =>
        {
            Assert.That(completedEffect, Is.Not.SameAs(retainedEffect));
            Assert.That(completedEffect.GetOriginal(), Is.SameAs(replacement));
            Assert.That(completedSnapshot, Is.Empty);
            Assert.That(completedSnapshot, Is.Not.SameAs(retainedSnapshot));
            Assert.That(retainedSnapshot, Has.Count.EqualTo(1));
            Assert.That(retainedEffect.IsDisposed, Is.True);
            Assert.That(delayed.IsDisposed, Is.True);
            Assert.That(oldChild.ResourceDisposeCount, Is.EqualTo(2));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(completedEffect.IsDisposed, Is.True);
            Assert.That(replacement.DisposeCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void DelayAnimationEffect_DelayedResourcesPublishesImmutableStructuralSnapshots()
    {
        var child = new Blur();
        var effect = new DelayAnimationEffect();
        effect.Effect.CurrentValue = child;
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> empty = resource.DelayedResources;

        FilterEffect.Resource first = resource.GetOrCreateDelayedResource(1, child, CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> afterFirst = resource.DelayedResources;
        FilterEffect.Resource reused = resource.GetOrCreateDelayedResource(1, child, CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> afterReuse = resource.DelayedResources;
        FilterEffect.Resource second = resource.GetOrCreateDelayedResource(5, child, CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> afterSecond = resource.DelayedResources;
        var mutationSurface = (IDictionary<int, FilterEffect.Resource>)afterSecond;

        Assert.Multiple(() =>
        {
            Assert.That(typeof(DelayAnimationEffect.Resource)
                .GetProperty(nameof(DelayAnimationEffect.Resource.DelayedResources))!.SetMethod, Is.Null);
            Assert.That(empty, Is.Empty);
            Assert.That(afterFirst, Is.Not.SameAs(empty));
            Assert.That(afterFirst, Has.Count.EqualTo(1));
            Assert.That(reused, Is.SameAs(first));
            Assert.That(afterReuse, Is.SameAs(afterFirst),
                "reusing an existing branch must not replace an unchanged structural snapshot");
            Assert.That(afterSecond, Is.Not.SameAs(afterFirst));
            Assert.That(afterSecond, Has.Count.EqualTo(2));
            Assert.That(afterFirst, Has.Count.EqualTo(1), "adding a branch must not mutate a retained snapshot");
            Assert.That(mutationSurface.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => mutationSurface.Add(9, first));
            Assert.Throws<NotSupportedException>(mutationSurface.Clear);
        });

        resource.PruneDelayedResources(new HashSet<int> { 5 });
        IReadOnlyDictionary<int, FilterEffect.Resource> afterPrune = resource.DelayedResources;

        Assert.Multiple(() =>
        {
            Assert.That(afterPrune, Is.Not.SameAs(afterSecond));
            Assert.That(afterPrune.Keys, Is.EquivalentTo(new[] { 5 }));
            Assert.That(afterSecond, Has.Count.EqualTo(2), "pruning must not mutate a retained snapshot");
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.IsDisposed, Is.False);
        });

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = resource.DelayedResources);
            Assert.That(afterPrune, Has.Count.EqualTo(1));
            Assert.That(afterPrune[5], Is.SameAs(second));
            Assert.That(second.IsDisposed, Is.True);
        });
    }

    [Test]
    public void DelayAnimationEffect_FirstChildFailure_StillSweepsAndDetachesEveryOwner()
    {
        var failure = new InvalidOperationException("delayed effect child");
        var child = new SuppressedLifecycleThrowingFilterEffect(failure);
        var effect = new DelayAnimationEffect();
        effect.Effect.CurrentValue = child;
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);
        FilterEffect.Resource retainedEffect = resource.Effect!;
        FilterEffect.Resource delayed = resource.GetOrCreateDelayedResource(2, child, CompositionContext.Default);
        IReadOnlyDictionary<int, FilterEffect.Resource> retainedSnapshot = resource.DelayedResources;

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Effect);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.DelayedResources);
            Assert.That(retainedSnapshot, Has.Count.EqualTo(1));
            Assert.That(retainedSnapshot[2], Is.SameAs(delayed));
            Assert.That(retainedEffect.IsDisposed, Is.True);
            Assert.That(delayed.IsDisposed, Is.True);
            Assert.That(child.DisposeCount, Is.EqualTo(2), "cleanup must continue after the first child fails");
        });

        Assert.DoesNotThrow(resource.Dispose);
        Assert.That(child.DisposeCount, Is.EqualTo(2));
    }

    private static Exception? UpdateAndCapture(
        EngineObject.Resource resource,
        EngineObject owner)
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
}

internal sealed partial class SuppressedLifecycleBlockingDrawable : Drawable
{
    public bool BlockNextUpdate { get; set; }

    public ManualResetEventSlim UpdateEntered { get; } = new();

    public ManualResetEventSlim ContinueUpdate { get; } = new();

    public int ResourceDisposeCount { get; private set; }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => default;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public partial class Resource
    {
        partial void PreUpdate(SuppressedLifecycleBlockingDrawable obj, CompositionContext context)
        {
            if (!obj.BlockNextUpdate)
                return;

            obj.UpdateEntered.Set();
            if (!obj.ContinueUpdate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked drawable resource update was not released.");
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                GetOriginal().ResourceDisposeCount++;
        }
    }
}

internal sealed partial class SuppressedLifecycleBlockingFilterEffect : FilterEffect
{
    public bool BlockNextUpdate { get; set; }

    public ManualResetEventSlim UpdateEntered { get; } = new();

    public ManualResetEventSlim ContinueUpdate { get; } = new();

    public int ResourceDisposeCount { get; private set; }

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public partial class Resource
    {
        partial void PreUpdate(SuppressedLifecycleBlockingFilterEffect obj, CompositionContext context)
        {
            if (!obj.BlockNextUpdate)
                return;

            obj.UpdateEntered.Set();
            if (!obj.ContinueUpdate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked filter-effect resource update was not released.");
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                GetOriginal().ResourceDisposeCount++;
        }
    }
}

[SuppressResourceClassGeneration]
internal sealed class SuppressedLifecycleBlockingAcquisitionDrawable : Drawable
{
    public ManualResetEventSlim AcquisitionEntered { get; } = new();

    public ManualResetEventSlim ContinueAcquisition { get; } = new();

    public int DisposeCount { get; private set; }

    public override Drawable.Resource ToResource(CompositionContext context)
    {
        AcquisitionEntered.Set();
        if (!ContinueAcquisition.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The blocked drawable resource acquisition was not released.");

        var resource = new Resource(this);
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => default;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    private new sealed class Resource(SuppressedLifecycleBlockingAcquisitionDrawable owner) : Drawable.Resource
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
internal sealed class SuppressedLifecycleBlockingAcquisitionFilterEffect : FilterEffect
{
    public ManualResetEventSlim AcquisitionEntered { get; } = new();

    public ManualResetEventSlim ContinueAcquisition { get; } = new();

    public int DisposeCount { get; private set; }

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override FilterEffect.Resource ToResource(CompositionContext context)
    {
        AcquisitionEntered.Set();
        if (!ContinueAcquisition.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The blocked filter-effect resource acquisition was not released.");

        var resource = new Resource(this);
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    private new sealed class Resource(SuppressedLifecycleBlockingAcquisitionFilterEffect owner) : FilterEffect.Resource
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
internal sealed class SuppressedLifecycleThrowingFilterEffect(Exception failure) : FilterEffect
{
    public int DisposeCount { get; private set; }

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override FilterEffect.Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(this, failure);
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    private new sealed class Resource(
        SuppressedLifecycleThrowingFilterEffect owner,
        Exception failure) : FilterEffect.Resource
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                owner.DisposeCount++;
                throw failure;
            }

            base.Dispose(disposing);
        }
    }
}

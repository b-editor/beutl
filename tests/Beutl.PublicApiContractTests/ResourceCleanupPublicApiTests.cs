using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Meshes;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class ResourceCleanupPublicApiTests
{
    [Test]
    public void ProtectedCleanupContract_IsUsableByNonFriendDerivedResources()
    {
        var firstFailure = new InvalidOperationException("first");
        var laterFailure = new InvalidOperationException("later");
        var first = new PublicDisposableProbe(firstFailure);
        var later = new PublicDisposableProbe(laterFailure);
        var preexistingFailure = new ApplicationException("preexisting");
        var preexistingProbe = new PublicDisposableProbe(laterFailure);
        IDisposable?[] callerOwnedArray = [first, null, later];

        Exception? captured = PublicCleanupResource.DisposeAll(null, callerOwnedArray);
        Exception? retained = PublicCleanupResource.DisposeAll(preexistingFailure, preexistingProbe);

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.SameAs(firstFailure));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(later.DisposeCount, Is.EqualTo(1), "cleanup must continue after the first failure");
            Assert.That(callerOwnedArray, Is.EqualTo(new IDisposable?[] { first, null, later }),
                "the params array belongs to the caller and must not be mutated");
            Assert.That(retained, Is.SameAs(preexistingFailure),
                "an incoming first failure must retain its exact identity");
            Assert.That(preexistingProbe.DisposeCount, Is.EqualTo(1),
                "an incoming failure must not prevent the resource sweep");
            Assert.That(PublicCleanupResource.DisposeAll(captured, null), Is.SameAs(firstFailure),
                "a null params array is accepted and must preserve an existing first failure");
        });

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => PublicCleanupResource.Rethrow(captured));
        Assert.That(actual, Is.SameAs(firstFailure));
        Assert.DoesNotThrow(() => PublicCleanupResource.Rethrow(null));
    }

    [Test]
    public void ExistingIsDisposedGuard_RemainsCompatibleForNonFriendDerivedResources()
    {
        var resource = new PublicGuardedCleanupResource();

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposedDuringCleanup, Is.False);
            Assert.That(resource.CleanupCount, Is.EqualTo(1));
            Assert.That(resource.IsDisposed, Is.True);
        });
    }

    [Test]
    public void BestEffortCleanup_RejectsResourceGraphsBeforeDisposingAnyValue()
    {
        var first = new PublicDisposableProbe(null);
        var resource = new PublicGuardedCleanupResource();
        var last = new PublicDisposableProbe(null);

        Assert.Throws<ArgumentException>(() => PublicCleanupResource.DisposeAll(null, first, resource, last));

        Assert.Multiple(() =>
        {
            Assert.That(first.DisposeCount, Is.Zero);
            Assert.That(resource.IsDisposed, Is.False);
            Assert.That(last.DisposeCount, Is.Zero);
        });

        resource.Dispose();
    }

    [Test]
    public void HandwrittenUpdateCore_IsExtensibleWithoutNestedLifecycleLeases()
    {
        var owner = new PublicShakeEffect();
        var resource = new PublicShakeResource();
        bool updateOnly = false;

        Assert.DoesNotThrow(() => resource.Update(owner, CompositionContext.Default, ref updateOnly));

        EngineObject original = resource.GetOriginal();
        Assert.Throws<InvalidCastException>(() =>
        {
            bool wrongUpdateOnly = false;
            resource.Update(new ShakeEffect(), CompositionContext.Default, ref wrongUpdateOnly);
        });

        Assert.Multiple(() =>
        {
            Assert.That(resource.UpdateCoreCount, Is.EqualTo(1));
            Assert.That(original, Is.SameAs(owner));
            Assert.That(resource.GetOriginal(), Is.SameAs(owner),
                "a rejected wrong-type update must not replace the published original");
        });

        resource.Dispose();
    }

    [Test]
    public void OwnedCollectionResources_ExposeOnlyReadOnlySnapshotsToNonFriendConsumers()
    {
        using var group = new DrawableGroup.Resource();
        using var decorator = new DrawableDecorator.Resource();
        using var sounds = new SoundGroup.Resource();
        using var scene = new Scene3D.Resource();

        AssertReadOnly<Drawable.Resource>(group.Children);
        AssertReadOnly<Drawable.Resource>(decorator.Children);
        AssertReadOnly<Sound.Resource>(sounds.Children);
        AssertReadOnly<Light3D.Resource>(scene.Lights);
        AssertReadOnly<Object3D.Resource>(scene.Objects);

        static void AssertReadOnly<T>(IReadOnlyList<T> values)
        {
            Assert.That(values, Is.Empty);
        }
    }

    [Test]
    public void GeneratedLifecycleCore_IsImplementableByNonFriendManualResources()
    {
        var cleanupFailure = new InvalidOperationException("manual cleanup");
        var child = new PublicGuardedCleanupResource();
        var resource = new PublicLifecycleResource(child, cleanupFailure);
        resource.RunExclusive(new DrawableGroup(), () => Assert.That(resource.Child, Is.SameAs(child)));

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(child.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Child);
            Assert.That(resource.Events, Is.EqualTo(new[] { "prepare", "cleanup" }));
            Assert.That(resource.IsDisposed, Is.True);
        });
    }

    public sealed class PublicCleanupResource : EngineObject.Resource
    {
        public static Exception? DisposeAll(Exception? firstFailure, params IDisposable?[]? resources)
        {
            DisposeOwnedResources(ref firstFailure, resources);
            return firstFailure;
        }

        public static void Rethrow(Exception? firstFailure)
        {
            ThrowIfCleanupFailed(firstFailure);
        }
    }

    public sealed class PublicGuardedCleanupResource : EngineObject.Resource
    {
        public int CleanupCount { get; private set; }

        public bool? IsDisposedDuringCleanup { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposedDuringCleanup = IsDisposed;
            if (IsDisposed)
                return;

            CleanupCount++;
        }
    }

    public sealed class PublicLifecycleResource(
        EngineObject.Resource child,
        Exception cleanupFailure) : EngineObject.Resource
    {
        private EngineObject.Resource? _child = child;

        public EngineObject.Resource? Child => ReadGeneratedResourceState(ref _child);

        public List<string> Events { get; } = [];

        public void RunExclusive(EngineObject owner, Action action)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(owner);
            action();
        }

        protected override void PrepareGeneratedResourceCleanupCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                Events.Add("prepare");
                context.Reserve(Child);
            }

            base.PrepareGeneratedResourceCleanupCore(disposing, context);
        }

        protected override void RollbackGeneratedResourceCleanupCore()
        {
            try
            {
                base.RollbackGeneratedResourceCleanupCore();
            }
            finally
            {
                Events.Add("rollback");
            }
        }

        protected override void CleanupGeneratedResourceCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            try
            {
                if (disposing)
                {
                    Events.Add("cleanup");
                    EngineObject.Resource? retained = _child;
                    _child = null;
                    context.DisposeOwned(retained);
                    context.Capture(cleanupFailure);
                }
            }
            finally
            {
                base.CleanupGeneratedResourceCore(disposing, context);
            }
        }
    }

    public sealed class PublicShakeEffect : ShakeEffect
    {
    }

    public sealed class PublicShakeResource : ShakeEffect.Resource
    {
        public int UpdateCoreCount { get; private set; }

        protected override bool IsCompatibleUpdateOwner(ShakeEffect obj) => obj is PublicShakeEffect;

        protected override void UpdateCore(ShakeEffect obj, CompositionContext context, ref bool updateOnly)
        {
            base.UpdateCore(obj, context, ref updateOnly);
            UpdateCoreCount++;
        }
    }

    private sealed class PublicDisposableProbe(Exception? failure) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (failure != null)
                throw failure;
        }
    }
}

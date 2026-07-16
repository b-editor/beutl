using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Meshes;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public sealed class OwnedResourceCollectionTests
{
    [Test]
    public void SoundGroup_ChildrenAreReadOnlyAndDisposeDrainsAfterMutationAttempts()
    {
        var first = new OwnedCollectionProbeSound();
        var second = new OwnedCollectionProbeSound();
        var third = new OwnedCollectionProbeSound();
        var group = new SoundGroup();
        group.Children.Add(first);
        group.Children.Add(second);
        var resource = (SoundGroup.Resource)group.ToResource(CompositionContext.Default);
        IReadOnlyList<Sound.Resource> retained = resource.Children;

        first.IsEnabled = false;
        bool updateOnly = false;
        resource.Update(group, CompositionContext.Default, ref updateOnly);
        Assert.That(resource.Children, Is.SameAs(retained),
            "a version-only update must retain the published structural snapshot");

        group.Children.Add(third);
        updateOnly = false;
        resource.Update(group, CompositionContext.Default, ref updateOnly);
        IReadOnlyList<Sound.Resource> addedSnapshot = resource.Children;
        Assert.Multiple(() =>
        {
            Assert.That(addedSnapshot, Is.Not.SameAs(retained));
            Assert.That(addedSnapshot, Has.Count.EqualTo(3));
            Assert.That(retained, Has.Count.EqualTo(2), "an older snapshot must remain structurally stable");
        });

        group.Children.Remove(third);
        updateOnly = false;
        resource.Update(group, CompositionContext.Default, ref updateOnly);
        IReadOnlyList<Sound.Resource> removedSnapshot = resource.Children;
        IList<Sound.Resource> mutationSurface = (IList<Sound.Resource>)retained;
        Sound.Resource attemptedAddition = retained[0];
        Exception? addFailure = null;
        Exception? clearFailure = null;
        first.DisposeCallback = () =>
        {
            try
            {
                mutationSurface.Add(attemptedAddition);
            }
            catch (Exception ex)
            {
                addFailure = ex;
                throw;
            }
        };
        second.DisposeCallback = () =>
        {
            try
            {
                mutationSurface.Clear();
            }
            catch (Exception ex)
            {
                clearFailure = ex;
                throw;
            }
        };

        NotSupportedException? actual = Assert.Throws<NotSupportedException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(typeof(SoundGroup.Resource).GetProperty(nameof(SoundGroup.Resource.Children))!.SetMethod,
                Is.Null);
            Assert.That(typeof(SoundGroup.Resource).GetProperty(nameof(SoundGroup.Resource.Children))!.PropertyType,
                Is.EqualTo(typeof(IReadOnlyList<Sound.Resource>)));
            Assert.That(removedSnapshot, Is.Not.SameAs(addedSnapshot));
            Assert.That(removedSnapshot, Is.Not.SameAs(retained),
                "a remove must publish a fresh immutable snapshot even when the final sequence matches an old one");
            Assert.That(removedSnapshot, Has.Count.EqualTo(2));
            Assert.That(retained, Is.Not.InstanceOf<List<Sound.Resource>>(),
                "the public surface must not expose the mutable owned list");
            Assert.That(addFailure, Is.SameAs(actual), "the first cleanup failure identity must be retained");
            Assert.That(clearFailure, Is.TypeOf<NotSupportedException>());
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1),
                "a failed mutation from the first callback must not skip later children");
            Assert.That(third.DisposeCount, Is.EqualTo(1), "a removed owned child must be disposed exactly once");
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Children);
            Assert.That(retained, Has.Count.EqualTo(2), "cleanup must not mutate a retained immutable snapshot");
            Assert.That(addedSnapshot, Has.Count.EqualTo(3));
            Assert.That(removedSnapshot, Has.Count.EqualTo(2));
            Assert.That(resource.IsDisposed, Is.True);
        });

        Assert.DoesNotThrow(resource.Dispose);
        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.EqualTo(1));
        Assert.That(third.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void SoundGroup_UpdateCleanupFailure_PublishesCommittedChildrenBeforeRethrow()
    {
        var failure = new InvalidOperationException("old sound cleanup");
        var previous = new OwnedCollectionProbeSound { DisposeCallback = () => throw failure };
        var replacement = new OwnedCollectionProbeSound();
        var group = new SoundGroup();
        group.Children.Add(previous);
        var resource = (SoundGroup.Resource)group.ToResource(CompositionContext.Default);
        IReadOnlyList<Sound.Resource> retained = resource.Children;
        Sound.Resource previousResource = retained[0];
        group.Children.Clear();
        group.Children.Add(replacement);

        bool updateOnly = false;
        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => resource.Update(group, CompositionContext.Default, ref updateOnly));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(previousResource.IsDisposed, Is.True);
            Assert.That(resource.Children, Is.Not.SameAs(retained));
            Assert.That(resource.Children, Has.Count.EqualTo(1));
            Assert.That(resource.Children[0].GetOriginal(), Is.SameAs(replacement));
            Assert.That(retained[0], Is.SameAs(previousResource));
            Assert.That(previous.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.Zero);
        });

        resource.Dispose();
        Assert.That(replacement.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void SoundGroup_RetiredCleanupMutationUsesPlannedOwnerSnapshot()
    {
        var previous = new OwnedCollectionProbeSound();
        var replacement = new OwnedCollectionProbeSound();
        var appendedDuringCleanup = new OwnedCollectionProbeSound();
        var group = new SoundGroup();
        group.Children.Add(previous);
        var resource = (SoundGroup.Resource)group.ToResource(CompositionContext.Default);
        previous.DisposeCallback = () => group.Children.Add(appendedDuringCleanup);
        group.Children.Clear();
        group.Children.Add(replacement);

        bool updateOnly = false;
        Assert.DoesNotThrow(() => resource.Update(group, CompositionContext.Default, ref updateOnly));

        Assert.Multiple(() =>
        {
            Assert.That(resource.Children, Has.Count.EqualTo(1));
            Assert.That(resource.Children[0].GetOriginal(), Is.SameAs(replacement));
            Assert.That(group.Children, Has.Count.EqualTo(2));
            Assert.That(previous.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.Zero);
            Assert.That(appendedDuringCleanup.DisposeCount, Is.Zero);
        });

        updateOnly = false;
        resource.Update(group, CompositionContext.Default, ref updateOnly);
        Assert.That(resource.Children, Has.Count.EqualTo(2));

        resource.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
            Assert.That(appendedDuringCleanup.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void SoundGroup_OwnedChildTransferredThroughFlow_AcquiresDistinctOwnedSuffix()
    {
        var child = new OwnedCollectionProbeSound();
        var group = new SoundGroup();
        group.Children.Add(child);
        var resource = (SoundGroup.Resource)group.ToResource(CompositionContext.Default);
        Sound.Resource transferred = resource.Children[0];
        var flow = new List<EngineObject.Resource> { transferred };
        var context = new CompositionContext(TimeSpan.Zero) { Flow = flow };

        bool updateOnly = false;
        resource.Update(group, context, ref updateOnly);
        IReadOnlyList<Sound.Resource> published = resource.Children;

        Assert.Multiple(() =>
        {
            Assert.That(flow, Is.Empty);
            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(published[0], Is.SameAs(transferred));
            Assert.That(published[1], Is.Not.SameAs(transferred));
            Assert.That(published[0].GetOriginal(), Is.SameAs(child));
            Assert.That(published[1].GetOriginal(), Is.SameAs(child));
        });

        Sound.Resource ownedSuffix = published[1];
        resource.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(transferred.IsDisposed, Is.False,
                "a resource transferred into the non-owned flow prefix must not be retired as an owned suffix");
            Assert.That(ownedSuffix.IsDisposed, Is.True);
            Assert.That(child.DisposeCount, Is.EqualTo(1));
        });

        transferred.Dispose();
        Assert.That(child.DisposeCount, Is.EqualTo(2));
    }

    [Test]
    public void CoupledReconcile_ResourceTransferredAcrossPlans_IsNotRetiredByPreviousOwner()
    {
        var child = new OwnedCollectionProbeSound();
        var firstGroup = new SoundGroup();
        var secondGroup = new SoundGroup();
        secondGroup.Children.Add(child);
        var transferred = (Sound.Resource)child.ToResource(CompositionContext.Default);
        var firstConsumed = new List<Sound.Resource> { transferred };
        var secondConsumed = new List<Sound.Resource>();
        var firstField = new List<Sound.Resource>();
        var secondField = new List<Sound.Resource> { transferred };
        var firstVersions = new List<int>();
        var secondVersions = new List<int>();
        bool changed = false;

        ResourceReconciler.ReconcileListsFromFlow(
            CompositionContext.Default,
            firstGroup.Children,
            firstConsumed,
            firstField,
            firstVersions,
            secondGroup.Children,
            secondConsumed,
            secondField,
            secondVersions,
            flowRollbackSnapshot: null,
            ref changed);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(firstField, Is.EqualTo(firstConsumed));
            Assert.That(secondField, Has.Count.EqualTo(1));
            Assert.That(secondField[0], Is.Not.SameAs(transferred),
                "the still-owned property item must receive a fresh suffix after its previous resource transfers");
            Assert.That(secondField[0].GetOriginal(), Is.SameAs(child));
            Assert.That(firstVersions, Is.EqualTo(new[] { transferred.Version }));
            Assert.That(secondVersions, Is.Empty);
            Assert.That(transferred.IsDisposed, Is.False,
                "a resource transferred into either coupled consumed prefix must not be retired by the other plan");
            Assert.That(child.DisposeCount, Is.Zero);
        });

        transferred.Dispose();
        Assert.That(child.DisposeCount, Is.EqualTo(1));
        secondField[0].Dispose();
        Assert.That(child.DisposeCount, Is.EqualTo(2));
    }

    [Test]
    public void FlowTransaction_ThirdAcquisitionFailureRollsBackEveryPlanAndRestoresFlow()
    {
        var firstAcquired = new OwnedCollectionProbeSound();
        var secondAcquired = new OwnedCollectionProbeSound();
        var acquisitionFailure = new InvalidOperationException("third acquisition");
        var thirdAcquired = new OwnedCollectionProbeSound { AcquisitionFailure = acquisitionFailure };
        var firstOwner = new SoundGroup();
        var secondOwner = new SoundGroup();
        var thirdOwner = new SoundGroup();
        firstOwner.Children.Add(firstAcquired);
        secondOwner.Children.Add(secondAcquired);
        thirdOwner.Children.Add(thirdAcquired);
        var firstField = new List<Sound.Resource>();
        var secondField = new List<Sound.Resource>();
        var thirdField = new List<Sound.Resource>();
        var firstVersions = new List<int>();
        var secondVersions = new List<int>();
        var thirdVersions = new List<int>();
        var flowOwner1 = new OwnedCollectionProbeSound();
        var flowOwner2 = new OwnedCollectionProbeSound();
        Sound.Resource flowResource1 = flowOwner1.ToResource(CompositionContext.Default);
        Sound.Resource flowResource2 = flowOwner2.ToResource(CompositionContext.Default);
        EngineObject.Resource[] originalFlow = [flowResource1, flowResource2];
        var context = new CompositionContext(TimeSpan.Zero)
        {
            Flow = new List<EngineObject.Resource>()
        };
        bool changed = false;

        ResourceReconciler.FlowReconciliationTransaction transaction
            = ResourceReconciler.BeginFlowTransaction(context, originalFlow);
        transaction.Add(firstOwner.Children, Array.Empty<Sound.Resource>(), firstField, firstVersions);
        transaction.Add(secondOwner.Children, Array.Empty<Sound.Resource>(), secondField, secondVersions);
        transaction.Add(thirdOwner.Children, Array.Empty<Sound.Resource>(), thirdField, thirdVersions);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => transaction.Commit(ref changed));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(acquisitionFailure));
            Assert.That(changed, Is.False);
            Assert.That(firstField, Is.Empty);
            Assert.That(secondField, Is.Empty);
            Assert.That(thirdField, Is.Empty);
            Assert.That(firstAcquired.DisposeCount, Is.EqualTo(1));
            Assert.That(secondAcquired.DisposeCount, Is.EqualTo(1));
            Assert.That(thirdAcquired.DisposeCount, Is.EqualTo(1));
            Assert.That(context.Flow, Is.EqualTo(originalFlow));
            Assert.Throws<InvalidOperationException>(() => transaction.Commit(ref changed));
        });

        flowResource1.Dispose();
        flowResource2.Dispose();
    }

    [Test]
    public void ReconcileResource_IncompatibleAcquisition_DisposesUnpublishedResource()
    {
        var owner = new MismatchedResourceOwner();
        ExpectedOwnedResource? field = null;
        bool changed = false;

        InvalidCastException? failure = Assert.Throws<InvalidCastException>(() =>
            ResourceReconciler.ReconcileResource(
                CompositionContext.Default,
                owner,
                ref field,
                ref changed));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain(typeof(ExpectedOwnedResource).FullName));
            Assert.That(field, Is.Null);
            Assert.That(changed, Is.False);
            Assert.That(owner.LastResource, Is.Not.Null);
            Assert.That(owner.LastResource!.IsDisposed, Is.True);
            Assert.That(owner.LastResource.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Scene3D_CollectionsAreReadOnlyAndDisposeDrainsAfterMutationAttempts()
    {
        var firstLight = new OwnedCollectionProbeLight();
        var secondLight = new OwnedCollectionProbeLight();
        var thirdLight = new OwnedCollectionProbeLight();
        var firstObject = new OwnedCollectionProbeObject();
        var secondObject = new OwnedCollectionProbeObject();
        var thirdObject = new OwnedCollectionProbeObject();
        var scene = new Scene3D();
        scene.Lights.Add(firstLight);
        scene.Lights.Add(secondLight);
        scene.Objects.Add(firstObject);
        scene.Objects.Add(secondObject);
        var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        IReadOnlyList<Light3D.Resource> retainedLights = resource.Lights;
        IReadOnlyList<Object3D.Resource> retainedObjects = resource.Objects;

        firstLight.IsEnabled = false;
        firstObject.IsEnabled = false;
        bool updateOnly = false;
        resource.Update(scene, CompositionContext.Default, ref updateOnly);
        Assert.Multiple(() =>
        {
            Assert.That(resource.Lights, Is.SameAs(retainedLights));
            Assert.That(resource.Objects, Is.SameAs(retainedObjects));
        });

        scene.Lights.Add(thirdLight);
        scene.Objects.Add(thirdObject);
        updateOnly = false;
        resource.Update(scene, CompositionContext.Default, ref updateOnly);
        IReadOnlyList<Light3D.Resource> addedLights = resource.Lights;
        IReadOnlyList<Object3D.Resource> addedObjects = resource.Objects;
        Assert.Multiple(() =>
        {
            Assert.That(addedLights, Is.Not.SameAs(retainedLights));
            Assert.That(addedObjects, Is.Not.SameAs(retainedObjects));
            Assert.That(addedLights, Has.Count.EqualTo(3));
            Assert.That(addedObjects, Has.Count.EqualTo(3));
            Assert.That(retainedLights, Has.Count.EqualTo(2));
            Assert.That(retainedObjects, Has.Count.EqualTo(2));
        });

        scene.Lights.Remove(thirdLight);
        scene.Objects.Remove(thirdObject);
        updateOnly = false;
        resource.Update(scene, CompositionContext.Default, ref updateOnly);
        IReadOnlyList<Light3D.Resource> removedLights = resource.Lights;
        IReadOnlyList<Object3D.Resource> removedObjects = resource.Objects;
        IList<Light3D.Resource> lightMutationSurface = (IList<Light3D.Resource>)retainedLights;
        IList<Object3D.Resource> objectMutationSurface = (IList<Object3D.Resource>)retainedObjects;
        Light3D.Resource attemptedLightAddition = retainedLights[0];
        Object3D.Resource attemptedObjectAddition = retainedObjects[0];
        Exception? firstFailure = null;
        Exception? secondLightFailure = null;
        Exception? firstObjectFailure = null;
        Exception? secondObjectFailure = null;
        firstLight.DisposeCallback = () => CaptureMutationFailure(
            () => lightMutationSurface.Add(attemptedLightAddition), ref firstFailure);
        secondLight.DisposeCallback = () => CaptureMutationFailure(
            lightMutationSurface.Clear, ref secondLightFailure);
        firstObject.DisposeCallback = () => CaptureMutationFailure(
            () => objectMutationSurface.Add(attemptedObjectAddition), ref firstObjectFailure);
        secondObject.DisposeCallback = () => CaptureMutationFailure(
            objectMutationSurface.Clear, ref secondObjectFailure);

        NotSupportedException? actual = Assert.Throws<NotSupportedException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(typeof(Scene3D.Resource).GetProperty(nameof(Scene3D.Resource.Lights))!.SetMethod, Is.Null);
            Assert.That(typeof(Scene3D.Resource).GetProperty(nameof(Scene3D.Resource.Objects))!.SetMethod, Is.Null);
            Assert.That(typeof(Scene3D.Resource).GetProperty(nameof(Scene3D.Resource.Lights))!.PropertyType,
                Is.EqualTo(typeof(IReadOnlyList<Light3D.Resource>)));
            Assert.That(typeof(Scene3D.Resource).GetProperty(nameof(Scene3D.Resource.Objects))!.PropertyType,
                Is.EqualTo(typeof(IReadOnlyList<Object3D.Resource>)));
            Assert.That(removedLights, Is.Not.SameAs(addedLights));
            Assert.That(removedObjects, Is.Not.SameAs(addedObjects));
            Assert.That(removedLights, Is.Not.SameAs(retainedLights));
            Assert.That(removedObjects, Is.Not.SameAs(retainedObjects));
            Assert.That(retainedLights, Is.Not.InstanceOf<List<Light3D.Resource>>());
            Assert.That(retainedObjects, Is.Not.InstanceOf<List<Object3D.Resource>>());
            Assert.That(firstFailure, Is.SameAs(actual), "the first cleanup failure identity must be retained");
            Assert.That(secondLightFailure, Is.TypeOf<NotSupportedException>());
            Assert.That(firstObjectFailure, Is.TypeOf<NotSupportedException>());
            Assert.That(secondObjectFailure, Is.TypeOf<NotSupportedException>());
            Assert.That(firstLight.DisposeCount, Is.EqualTo(1));
            Assert.That(secondLight.DisposeCount, Is.EqualTo(1));
            Assert.That(firstObject.DisposeCount, Is.EqualTo(1));
            Assert.That(secondObject.DisposeCount, Is.EqualTo(1),
                "every owned suffix must be swept despite earlier callback failures");
            Assert.That(thirdLight.DisposeCount, Is.EqualTo(1));
            Assert.That(thirdObject.DisposeCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Lights);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Objects);
            Assert.That(retainedLights, Has.Count.EqualTo(2));
            Assert.That(retainedObjects, Has.Count.EqualTo(2));
            Assert.That(addedLights, Has.Count.EqualTo(3));
            Assert.That(addedObjects, Has.Count.EqualTo(3));
            Assert.That(removedLights, Has.Count.EqualTo(2));
            Assert.That(removedObjects, Has.Count.EqualTo(2));
            Assert.That(resource.IsDisposed, Is.True);
        });

        Assert.DoesNotThrow(resource.Dispose);
    }

    [Test]
    public async Task Scene3D_PublicStateRejectsConcurrentUpdateAndDisposedAccess()
    {
        var blocking = new OwnedCollectionProbeObject();
        var scene = new Scene3D();
        scene.Objects.Add(blocking);
        var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        var context = new CompositionContext(TimeSpan.FromSeconds(2))
        {
            DisableResourceShare = true,
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Eighth,
        };
        blocking.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                resource.Update(scene, context, ref updateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(blocking.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() => _ = resource.Time);
                Assert.Throws<InvalidOperationException>(() => resource.Time = TimeSpan.Zero);
                Assert.Throws<InvalidOperationException>(() => _ = resource.DisableResourceShare);
                Assert.Throws<InvalidOperationException>(() => resource.DisableResourceShare = false);
                Assert.Throws<InvalidOperationException>(() => _ = resource.PreferProxy);
                Assert.Throws<InvalidOperationException>(() => resource.PreferProxy = false);
                Assert.Throws<InvalidOperationException>(() => _ = resource.PreferredProxyPreset);
                Assert.Throws<InvalidOperationException>(() =>
                    resource.PreferredProxyPreset = ProxyPreset.Half);
                Assert.Throws<InvalidOperationException>(resource.Dispose);
            });
        }
        finally
        {
            blocking.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(resource.Time, Is.EqualTo(context.Time));
            Assert.That(resource.DisableResourceShare, Is.True);
            Assert.That(resource.PreferProxy, Is.True);
            Assert.That(resource.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Eighth));
        });

        resource.Time = TimeSpan.FromSeconds(3);
        resource.DisableResourceShare = false;
        resource.PreferProxy = false;
        resource.PreferredProxyPreset = ProxyPreset.Half;
        Assert.Multiple(() =>
        {
            Assert.That(resource.Time, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(resource.DisableResourceShare, Is.False);
            Assert.That(resource.PreferProxy, Is.False);
            Assert.That(resource.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Half));
        });

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Time);
            Assert.Throws<ObjectDisposedException>(() => resource.Time = TimeSpan.Zero);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.DisableResourceShare);
            Assert.Throws<ObjectDisposedException>(() => resource.DisableResourceShare = false);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.PreferProxy);
            Assert.Throws<ObjectDisposedException>(() => resource.PreferProxy = false);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.PreferredProxyPreset);
            Assert.Throws<ObjectDisposedException>(() =>
                resource.PreferredProxyPreset = ProxyPreset.Quarter);
            Assert.That(blocking.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Scene3D_BusyObject_RollsBackLightAndObjectCollectionsTogether()
    {
        var previousLight = new OwnedCollectionProbeLight();
        var previousObject = new OwnedCollectionProbeObject();
        var replacementLight = new OwnedCollectionProbeLight();
        var replacementObject = new OwnedCollectionProbeObject();
        var scene = new Scene3D();
        scene.Lights.Add(previousLight);
        scene.Objects.Add(previousObject);
        var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        IReadOnlyList<Light3D.Resource> retainedLights = resource.Lights;
        IReadOnlyList<Object3D.Resource> retainedObjects = resource.Objects;
        Object3D.Resource busyResource = retainedObjects[0];
        int retainedVersion = resource.Version;
        scene.Lights.Clear();
        scene.Lights.Add(replacementLight);
        scene.Objects.Clear();
        scene.Objects.Add(replacementObject);
        previousObject.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool childUpdateOnly = false;
                busyResource.Update(previousObject, CompositionContext.Default, ref childUpdateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(previousObject.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            bool updateOnly = false;
            Assert.Throws<InvalidOperationException>(
                () => resource.Update(scene, CompositionContext.Default, ref updateOnly));
            Assert.Multiple(() =>
            {
                Assert.That(resource.Lights, Is.SameAs(retainedLights));
                Assert.That(resource.Objects, Is.SameAs(retainedObjects));
                Assert.That(resource.Version, Is.EqualTo(retainedVersion));
                Assert.That(retainedLights[0].IsDisposed, Is.False);
                Assert.That(retainedObjects[0].IsDisposed, Is.False);
                Assert.That(previousLight.DisposeCount, Is.Zero);
                Assert.That(previousObject.DisposeCount, Is.Zero);
                Assert.That(replacementLight.DisposeCount, Is.EqualTo(1));
                Assert.That(replacementObject.DisposeCount, Is.EqualTo(1));
            });
        }
        finally
        {
            previousObject.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        bool retryUpdateOnly = false;
        Assert.DoesNotThrow(() => resource.Update(scene, CompositionContext.Default, ref retryUpdateOnly));
        Assert.Multiple(() =>
        {
            Assert.That(resource.Lights, Has.Count.EqualTo(1));
            Assert.That(resource.Objects, Has.Count.EqualTo(1));
            Assert.That(resource.Lights[0].GetOriginal(), Is.SameAs(replacementLight));
            Assert.That(resource.Objects[0].GetOriginal(), Is.SameAs(replacementObject));
            Assert.That(previousLight.DisposeCount, Is.EqualTo(1));
            Assert.That(previousObject.DisposeCount, Is.EqualTo(1));
            Assert.That(replacementLight.DisposeCount, Is.EqualTo(1));
            Assert.That(replacementObject.DisposeCount, Is.EqualTo(1));
        });

        resource.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(replacementLight.DisposeCount, Is.EqualTo(2));
            Assert.That(replacementObject.DisposeCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task SoundGroup_DisposeReservationFailureRollsBackWholeOwnedGraph()
    {
        var busy = new OwnedCollectionProbeSound();
        var later = new OwnedCollectionProbeSound();
        var group = new SoundGroup();
        group.Children.Add(busy);
        group.Children.Add(later);
        var resource = (SoundGroup.Resource)group.ToResource(CompositionContext.Default);
        IReadOnlyList<Sound.Resource> retained = resource.Children;
        Sound.Resource busyResource = retained[0];
        Sound.Resource laterResource = retained[1];
        busy.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                busyResource.Update(busy, CompositionContext.Default, ref updateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(busy.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(busyResource.IsDisposed, Is.False);
                Assert.That(laterResource.IsDisposed, Is.False);
                Assert.That(resource.Children, Is.SameAs(retained));
                Assert.That(retained, Has.Count.EqualTo(2));
                Assert.That(busy.DisposeCount, Is.Zero);
                Assert.That(later.DisposeCount, Is.Zero,
                    "a failed pre-reservation must not partially clean later owners");
            });
        }
        finally
        {
            busy.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.DoesNotThrow(resource.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.That(busy.DisposeCount, Is.EqualTo(1));
            Assert.That(later.DisposeCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Children);
            Assert.That(retained, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task SoundGroup_MixedReplacementAndRemovalBusyChild_RollsBackWholeUpdate()
    {
        var first = new OwnedCollectionProbeSound();
        var second = new OwnedCollectionProbeSound();
        var replacement = new OwnedCollectionProbeSound();
        var group = new SoundGroup();
        group.Children.Add(first);
        group.Children.Add(second);
        var resource = (SoundGroup.Resource)group.ToResource(CompositionContext.Default);
        IReadOnlyList<Sound.Resource> retained = resource.Children;
        Sound.Resource secondResource = retained[1];
        int retainedVersion = resource.Version;
        second.BlockNextUpdate = true;
        group.Children.Clear();
        group.Children.Add(replacement);

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool childUpdateOnly = false;
                secondResource.Update(second, CompositionContext.Default, ref childUpdateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(second.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            bool updateOnly = false;
            Assert.Throws<InvalidOperationException>(
                () => resource.Update(group, CompositionContext.Default, ref updateOnly));
            Assert.Multiple(() =>
            {
                Assert.That(resource.Children, Is.SameAs(retained));
                Assert.That(resource.Children, Has.Count.EqualTo(2));
                Assert.That(resource.Version, Is.EqualTo(retainedVersion));
                Assert.That(retained[0].IsDisposed, Is.False);
                Assert.That(retained[1].IsDisposed, Is.False);
                Assert.That(first.DisposeCount, Is.Zero);
                Assert.That(second.DisposeCount, Is.Zero);
                Assert.That(replacement.DisposeCount, Is.EqualTo(1));
            });
        }
        finally
        {
            second.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        bool retryUpdateOnly = false;
        Assert.DoesNotThrow(() => resource.Update(group, CompositionContext.Default, ref retryUpdateOnly));
        Assert.Multiple(() =>
        {
            Assert.That(resource.Children, Has.Count.EqualTo(1));
            Assert.That(resource.Children[0].GetOriginal(), Is.SameAs(replacement));
            Assert.That(resource.Version, Is.GreaterThan(retainedVersion));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });

        resource.Dispose();
        Assert.That(replacement.DisposeCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SoundGroup_BusyOwnedChild_RestoresInterleavedFlowExactly()
    {
        var first = new OwnedCollectionProbeSound();
        var busy = new OwnedCollectionProbeSound();
        var replacement = new OwnedCollectionProbeSound();
        var group = new SoundGroup();
        group.Children.Add(first);
        group.Children.Add(busy);
        var resource = (SoundGroup.Resource)group.ToResource(CompositionContext.Default);
        Sound.Resource busyResource = resource.Children[1];

        var flowFirstOwner = new OwnedCollectionProbeSound();
        var flowSecondOwner = new OwnedCollectionProbeSound();
        var markerOwner = new OwnedCollectionProbeObject();
        Sound.Resource flowFirst = flowFirstOwner.ToResource(CompositionContext.Default);
        Object3D.Resource marker = markerOwner.ToResource(CompositionContext.Default);
        Sound.Resource flowSecond = flowSecondOwner.ToResource(CompositionContext.Default);
        EngineObject.Resource[] originalFlow = [flowFirst, marker, flowSecond];
        var flow = new List<EngineObject.Resource>(originalFlow);
        var context = new CompositionContext(TimeSpan.Zero) { Flow = flow };
        group.Children.Clear();
        group.Children.Add(replacement);
        busy.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool childUpdateOnly = false;
                busyResource.Update(busy, CompositionContext.Default, ref childUpdateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(busy.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            bool updateOnly = false;
            Assert.Throws<InvalidOperationException>(() => resource.Update(group, context, ref updateOnly));
            Assert.Multiple(() =>
            {
                Assert.That(flow, Is.EqualTo(originalFlow));
                Assert.That(resource.Children, Has.Count.EqualTo(2));
                Assert.That(resource.Children[0].GetOriginal(), Is.SameAs(first));
                Assert.That(resource.Children[1], Is.SameAs(busyResource));
                Assert.That(replacement.DisposeCount, Is.EqualTo(1));
            });
        }
        finally
        {
            busy.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        resource.Dispose();
        flowFirst.Dispose();
        marker.Dispose();
        flowSecond.Dispose();
    }

    private static void CaptureMutationFailure(Action mutation, ref Exception? failure)
    {
        try
        {
            mutation();
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class OwnedCollectionProbeSound : Sound
    {
        public Exception? AcquisitionFailure { get; set; }

        public bool BlockNextUpdate { get; set; }

        public ManualResetEventSlim UpdateEntered { get; } = new();

        public ManualResetEventSlim ContinueUpdate { get; } = new();

        public Action? DisposeCallback { get; set; }

        public int DisposeCount { get; private set; }

        public override Sound.Resource ToResource(CompositionContext context)
        {
            var resource = new Resource(this);
            try
            {
                bool updateOnly = true;
                resource.Update(this, context, ref updateOnly);
                if (AcquisitionFailure != null)
                    throw AcquisitionFailure;
                return resource;
            }
            catch
            {
                resource.Dispose();
                throw;
            }
        }

        private new sealed class Resource(OwnedCollectionProbeSound owner) : Sound.Resource
        {
            public override SoundSource.Resource? GetSoundSource() => null;

            public override void Update(
                EngineObject obj,
                CompositionContext context,
                ref bool updateOnly)
            {
                using var operation = BeginGeneratedResourceOperation(obj);
                if (owner.BlockNextUpdate)
                {
                    owner.BlockNextUpdate = false;
                    owner.UpdateEntered.Set();
                    if (!owner.ContinueUpdate.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("The test did not release the blocked resource update.");
                }

                base.Update(obj, context, ref updateOnly);
            }

            protected override void Dispose(bool disposing)
            {
                if (!disposing)
                {
                    base.Dispose(false);
                    return;
                }

                owner.DisposeCount++;
                try
                {
                    owner.DisposeCallback?.Invoke();
                }
                finally
                {
                    base.Dispose(true);
                }
            }
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class OwnedCollectionProbeLight : Light3D
    {
        public Action? DisposeCallback { get; set; }

        public int DisposeCount { get; private set; }

        public override Light3D.Resource ToResource(CompositionContext context)
        {
            var resource = new Resource(this);
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        private new sealed class Resource(OwnedCollectionProbeLight owner) : Light3D.Resource
        {
            protected override void Dispose(bool disposing)
            {
                if (!disposing)
                {
                    base.Dispose(false);
                    return;
                }

                owner.DisposeCount++;
                try
                {
                    owner.DisposeCallback?.Invoke();
                }
                finally
                {
                    base.Dispose(true);
                }
            }
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class OwnedCollectionProbeObject : Object3D
    {
        public bool BlockNextUpdate { get; set; }

        public ManualResetEventSlim UpdateEntered { get; } = new();

        public ManualResetEventSlim ContinueUpdate { get; } = new();

        public Action? DisposeCallback { get; set; }

        public int DisposeCount { get; private set; }

        public override Object3D.Resource ToResource(CompositionContext context)
        {
            var resource = new Resource(this);
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        private new sealed class Resource(OwnedCollectionProbeObject owner) : Object3D.Resource
        {
            public override Mesh.Resource? GetMesh() => null;

            public override void Update(
                EngineObject obj,
                CompositionContext context,
                ref bool updateOnly)
            {
                using var operation = BeginGeneratedResourceOperation(obj);
                if (owner.BlockNextUpdate)
                {
                    owner.BlockNextUpdate = false;
                    owner.UpdateEntered.Set();
                    if (!owner.ContinueUpdate.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("The test did not release the blocked resource update.");
                }

                base.Update(obj, context, ref updateOnly);
            }

            protected override void Dispose(bool disposing)
            {
                if (!disposing)
                {
                    base.Dispose(false);
                    return;
                }

                owner.DisposeCount++;
                try
                {
                    owner.DisposeCallback?.Invoke();
                }
                finally
                {
                    base.Dispose(true);
                }
            }
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class MismatchedResourceOwner : EngineObject
    {
        public TrackingOwnedResource? LastResource { get; private set; }

        public override EngineObject.Resource ToResource(CompositionContext context)
        {
            return LastResource = new TrackingOwnedResource();
        }
    }

    private sealed class TrackingOwnedResource : EngineObject.Resource
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
        }
    }

    private sealed class ExpectedOwnedResource : EngineObject.Resource
    {
    }
}

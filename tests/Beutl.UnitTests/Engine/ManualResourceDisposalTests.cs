using System.Reflection;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public sealed class ManualResourceDisposalTests
{
    [Test]
    public void DrawableGroup_FirstChildFailure_StillSweepsLaterChildStateAndBase()
    {
        var failure = new InvalidOperationException("first child");
        var first = new DisposalProbeDrawable(failure);
        var later = new DisposalProbeDrawable();
        var baseEffect = new DisposalProbeFilterEffect();
        var group = new DrawableGroup();
        group.FilterEffect.CurrentValue = baseEffect;
        group.Children.Add(first);
        group.Children.Add(later);
        var resource = (DrawableGroup.Resource)group.ToResource(CompositionContext.Default);
        IReadOnlyList<Drawable.Resource> retainedChildren = resource.Children;

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(later.DisposeCount, Is.EqualTo(1), "later children must be swept after a failure");
            Assert.That(baseEffect.DisposeCount, Is.EqualTo(1), "base Drawable resources must still be swept");
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Children);
            Assert.That(retainedChildren, Has.Count.EqualTo(2));
            Assert.That(retainedChildren[0].IsDisposed, Is.True);
            Assert.That(retainedChildren[1].IsDisposed, Is.True);
            Assert.That(resource.IsDisposed, Is.True);
        });

        Assert.DoesNotThrow(resource.Dispose);
        Assert.That(later.DisposeCount, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DrawableContainer_ChildrenRejectMutationAndDisposeStillSweepsEveryChild(bool useDecorator)
    {
        var addMutator = new DisposalProbeDrawable();
        var clearMutator = new DisposalProbeDrawable();
        var later = new DisposalProbeDrawable();
        (Drawable.Resource resource, IReadOnlyList<Drawable.Resource> retainedChildren) =
            CreateDrawableContainerResource(useDecorator, addMutator, clearMutator, later);
        var unowned = new DisposalProbeDrawable();
        Drawable.Resource unownedResource = unowned.ToResource(CompositionContext.Default);
        var mutationSurface = (IList<Drawable.Resource>)retainedChildren;
        PropertyInfo childrenProperty = resource.GetType().GetProperty(
            nameof(DrawableGroup.Resource.Children))!;
        Exception? addFailure = null;
        bool clearAttempted = false;
        addMutator.OnResourceDispose = () =>
        {
            try
            {
                mutationSurface.Add(unownedResource);
            }
            catch (Exception ex)
            {
                addFailure = ex;
                throw;
            }
        };
        clearMutator.OnResourceDispose = () =>
        {
            clearAttempted = true;
            mutationSurface.Clear();
        };

        NotSupportedException? actual = Assert.Throws<NotSupportedException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(addFailure), "the first cleanup failure identity must be retained");
            Assert.That(childrenProperty.PropertyType, Is.EqualTo(typeof(IReadOnlyList<Drawable.Resource>)));
            Assert.That(childrenProperty.SetMethod, Is.Null, "the public owned-list surface must not have a setter");
            Assert.That(mutationSurface.IsReadOnly, Is.True);
            Assert.That(retainedChildren, Is.Not.InstanceOf<List<Drawable.Resource>>(),
                "the public surface must not leak the mutable owned list");
            Assert.That(clearAttempted, Is.True, "cleanup must continue after the rejected Add");
            Assert.That(addMutator.DisposeCount, Is.EqualTo(1));
            Assert.That(clearMutator.DisposeCount, Is.EqualTo(1));
            Assert.That(later.DisposeCount, Is.EqualTo(1),
                "a rejected retained-view mutation must not interrupt the owned-resource sweep");
            Assert.Throws<ObjectDisposedException>(() => _ = GetDrawableContainerChildren(resource));
            Assert.That(retainedChildren, Has.Count.EqualTo(3),
                "cleanup must not mutate a snapshot retained by a concurrent reader");
            Assert.That(retainedChildren.All(child => child.IsDisposed), Is.True);
            Assert.That(unowned.DisposeCount, Is.Zero, "a rejected Add must not transfer ownership");
        });

        Assert.DoesNotThrow(resource.Dispose);
        unownedResource.Dispose();
        Assert.That(unowned.DisposeCount, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DrawableContainer_ChildrenPublishesOnlyStructuralSnapshots(bool useDecorator)
    {
        var first = new DisposalProbeDrawable();
        var second = new DisposalProbeDrawable();
        (Drawable owner, IListProperty<Drawable> sourceChildren) = CreateDrawableContainer(useDecorator);
        sourceChildren.Add(first);
        Drawable.Resource resource = owner.ToResource(CompositionContext.Default);
        IReadOnlyList<Drawable.Resource> initial = GetDrawableContainerChildren(resource);

        first.IsEnabled = false;
        UpdateResource(resource, owner);
        IReadOnlyList<Drawable.Resource> versionOnly = GetDrawableContainerChildren(resource);

        sourceChildren.Add(second);
        UpdateResource(resource, owner);
        IReadOnlyList<Drawable.Resource> afterAdd = GetDrawableContainerChildren(resource);

        sourceChildren.Remove(second);
        UpdateResource(resource, owner);
        IReadOnlyList<Drawable.Resource> afterRemove = GetDrawableContainerChildren(resource);

        Assert.Multiple(() =>
        {
            Assert.That(versionOnly, Is.SameAs(initial),
                "resource version updates must not replace an unchanged structural snapshot");
            Assert.That(afterAdd, Is.Not.SameAs(initial));
            Assert.That(afterAdd, Has.Count.EqualTo(2));
            Assert.That(initial, Has.Count.EqualTo(1), "an old snapshot must remain structurally stable");
            Assert.That(afterRemove, Is.Not.SameAs(afterAdd));
            Assert.That(afterRemove, Has.Count.EqualTo(1));
            Assert.That(afterRemove[0], Is.SameAs(initial[0]));
            Assert.That(afterAdd, Has.Count.EqualTo(2), "removal must not mutate the prior snapshot");
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        });

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = GetDrawableContainerChildren(resource));
            Assert.That(initial, Has.Count.EqualTo(1));
            Assert.That(afterAdd, Has.Count.EqualTo(2));
            Assert.That(afterRemove, Has.Count.EqualTo(1));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DrawableGroup_DisposeWhileOwnedChildUpdatesRollsBackAndCanRetry()
    {
        var blocking = new BlockingOwnedDrawable();
        var later = new DisposalProbeDrawable();
        var group = new DrawableGroup();
        group.Children.Add(blocking);
        group.Children.Add(later);
        var resource = (DrawableGroup.Resource)group.ToResource(CompositionContext.Default);
        IReadOnlyList<Drawable.Resource> retainedChildren = resource.Children;
        var blockingResource = (BlockingOwnedDrawable.Resource)retainedChildren[0];
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        blocking.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                blockingResource.Update(blocking, context, ref updateOnly);
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
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(resource.Children, Is.SameAs(retainedChildren),
                    "a failed pre-reservation must preserve the published child snapshot");
                Assert.That(retainedChildren, Has.Count.EqualTo(2));
                Assert.That(retainedChildren.All(child => !child.IsDisposed), Is.True);
                Assert.That(blocking.ResourceDisposeCount, Is.Zero);
                Assert.That(later.DisposeCount, Is.Zero);
            });
        }
        finally
        {
            blocking.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Children);
            Assert.That(retainedChildren, Has.Count.EqualTo(2));
            Assert.That(retainedChildren.All(child => child.IsDisposed), Is.True);
            Assert.That(blocking.ResourceDisposeCount, Is.EqualTo(1));
            Assert.That(later.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DrawableTimeController_DisposeWhileTargetUpdatesRollsBackAndCanRetry()
    {
        var blocking = new BlockingOwnedDrawable();
        var controller = new DrawableTimeController();
        controller.Target.CurrentValue = blocking;
        var resource = (DrawableTimeController.Resource)controller.ToResource(CompositionContext.Default);
        var blockingResource = (BlockingOwnedDrawable.Resource)resource.Target!;
        global::Beutl.Media.SpeedIntegrator? retainedIntegrator = resource.SpeedIntegrator;
        blocking.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                blockingResource.Update(blocking, CompositionContext.Default, ref updateOnly);
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
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(resource.Target, Is.SameAs(blockingResource),
                    "a failed ownership preflight must not detach the target");
                Assert.That(resource.SpeedIntegrator, Is.SameAs(retainedIntegrator),
                    "a failed ownership preflight must not dispose the sibling non-resource owner");
                Assert.That(blockingResource.IsDisposed, Is.False);
                Assert.That(blocking.ResourceDisposeCount, Is.Zero);
            });
        }
        finally
        {
            blocking.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Target);
            Assert.That(resource.SpeedIntegrator, Is.Null);
            Assert.That(blockingResource.IsDisposed, Is.True);
            Assert.That(blocking.ResourceDisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ParticleEmitter_FirstChildFailure_StillRunsBaseAndDetachesChild()
    {
        var failure = new InvalidOperationException("particle child");
        var child = new DisposalProbeDrawable(failure);
        var baseEffect = new DisposalProbeFilterEffect();
        var emitter = new ParticleEmitter();
        emitter.ParticleDrawable.CurrentValue = child;
        emitter.FilterEffect.CurrentValue = baseEffect;
        var resource = (ParticleEmitter.Resource)emitter.ToResource(CompositionContext.Default);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(child.DisposeCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => _ = resource.ParticleDrawable);
            Assert.That(baseEffect.DisposeCount, Is.EqualTo(1), "ParticleEmitter must run Drawable.Resource.Dispose");
            Assert.That(resource.IsDisposed, Is.True);
        });

        Assert.DoesNotThrow(resource.Dispose);
        Assert.That(child.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void ParticleEmitter_ToResourceFailure_DisposesPartiallyInitializedBaseResources()
    {
        var failure = new InvalidOperationException("particle acquisition");
        var child = new AcquisitionThrowingDrawable(failure);
        var baseEffect = new DisposalProbeFilterEffect();
        var emitter = new ParticleEmitter();
        emitter.ParticleDrawable.CurrentValue = child;
        emitter.FilterEffect.CurrentValue = baseEffect;

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => emitter.ToResource(CompositionContext.Default));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure), "cleanup must not replace the acquisition failure");
            Assert.That(child.ToResourceCount, Is.EqualTo(1));
            Assert.That(baseEffect.DisposeCount, Is.EqualTo(1),
                "resources acquired by the base Update path must be reclaimed when a later acquisition fails");
        });
    }

    [Test]
    public void GeometryShapeNode_FirstOwnerFailure_StillSweepsEveryOwnerAndDetachesCache()
    {
        var failure = new InvalidOperationException("fill");
        var fill = new ThrowingBrushResource(failure);
        var pen = new CountingPenResource();
        var geometry = new CountingGeometryResource();
        var output = new GeometryRenderNode(geometry, fill, pen);
        var resource = new GeometryShapeNode.Resource();
        FieldInfo cacheField = typeof(GeometryShapeNode.Resource).GetField(
            "_cachedOutput",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        cacheField.SetValue(resource, output);
        SetPrivateField(resource, "_fillResource", fill);
        SetPrivateField(resource, "_penResource", pen);
        SetPrivateField(resource, "_geometryResource", geometry);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(fill.DisposeCount, Is.EqualTo(1));
            Assert.That(pen.DisposeCount, Is.EqualTo(1), "later NodeGraph owners must still be swept");
            Assert.That(geometry.DisposeCount, Is.EqualTo(1), "later NodeGraph owners must still be swept");
            Assert.That(output.IsDisposed, Is.True, "the cached render node itself remains independently owned");
            Assert.That(cacheField.GetValue(resource), Is.Null, "the cache field must be detached before disposal");
            Assert.That(resource.IsDisposed, Is.True);
        });
    }

    [Test]
    public async Task GeometryShapeNode_DisposeWhileOwnedGeometryUpdatesRollsBackAndCanRetry()
    {
        var geometry = new BlockingOwnedGeometry();
        var geometryResource = (BlockingOwnedGeometry.Resource)geometry.ToResource(CompositionContext.Default);
        var output = new GeometryRenderNode(geometryResource, null, null);
        var resource = new GeometryShapeNode.Resource();
        FieldInfo cacheField = typeof(GeometryShapeNode.Resource).GetField(
            "_cachedOutput",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        cacheField.SetValue(resource, output);
        SetPrivateField(resource, "_geometryResource", geometryResource);
        geometry.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                geometryResource.Update(geometry, CompositionContext.Default, ref updateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(geometry.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(geometryResource.IsDisposed, Is.False);
                Assert.That(geometry.ResourceDisposeCount, Is.Zero);
                Assert.That(output.IsDisposed, Is.False,
                    "a rejected reservation must not detach or dispose the cached output");
                Assert.That(cacheField.GetValue(resource), Is.SameAs(output));
            });
        }
        finally
        {
            geometry.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.That(geometryResource.IsDisposed, Is.True);
            Assert.That(geometry.ResourceDisposeCount, Is.EqualTo(1));
            Assert.That(output.IsDisposed, Is.True);
            Assert.That(cacheField.GetValue(resource), Is.Null);
        });
    }

    [Test]
    public void GeometryShapeNode_PendingRollbackOwnerSurvivesRejectedCleanupAndCanRetry()
    {
        var geometry = new BlockingOwnedGeometry();
        var geometryResource = (BlockingOwnedGeometry.Resource)geometry.ToResource(CompositionContext.Default);
        var resource = new GeometryShapeNode.Resource();
        var pending = (List<EngineObject.Resource>)GetPrivateField(resource, "_pendingRollbackResources")!;
        pending.Add(geometryResource);
        SetResourceBusy(geometryResource, true);

        try
        {
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(geometryResource.IsDisposed, Is.False);
                Assert.That(pending, Has.Count.EqualTo(1));
            });
        }
        finally
        {
            SetResourceBusy(geometryResource, false);
        }

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.That(geometryResource.IsDisposed, Is.True);
            Assert.That(geometry.ResourceDisposeCount, Is.EqualTo(1));
            Assert.That(pending, Is.Empty);
        });
    }

    [Test]
    public void GeometryShapeNode_ReplacementWhileOldOwnerBusyLeavesPublishedStateUntouched()
    {
        var node = new GeometryShapeNode();
        node.Geometry.Property!.SetValue(new EllipseGeometry());
        var model = new GraphModel();
        model.Nodes.Add(node);
        using var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        var resource = (GeometryShapeNode.Resource)snapshot.GetResource(0)!;
        var oldGeometry = new BlockingOwnedGeometry();
        var oldResource = (BlockingOwnedGeometry.Resource)oldGeometry.ToResource(CompositionContext.Default);
        var oldOutput = new GeometryRenderNode(oldResource, null, null);
        SetPrivateField(resource, "_geometryResource", oldResource);
        SetPrivateField(resource, "_cachedOutput", oldOutput);
        SetResourceBusy(oldResource, true);

        try
        {
            Assert.Throws<InvalidOperationException>(
                () => snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default));
            Assert.Multiple(() =>
            {
                Assert.That(GetPrivateField(resource, "_geometryResource"), Is.SameAs(oldResource));
                Assert.That(GetPrivateField(resource, "_cachedOutput"), Is.SameAs(oldOutput));
                Assert.That(oldResource.IsDisposed, Is.False);
                Assert.That(oldOutput.IsDisposed, Is.False);
            });
        }
        finally
        {
            SetResourceBusy(oldResource, false);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MediaSourceNode_ReplacementWhileOldOwnerBusyLeavesPublishedStateUntouched(bool video)
    {
        GraphNode node;
        GraphNode.Resource resource;
        EngineObject.Resource oldResource;
        RenderNode oldOutput;
        var model = new GraphModel();
        using var snapshot = new GraphSnapshot();

        if (video)
        {
            var videoNode = new VideoSourceNode();
            var replacementSource = new VideoSource();
            replacementSource.ReadFrom(CreateMissingMediaUri("replacement.mp4"));
            videoNode.Source.Property!.SetValue(replacementSource);
            node = videoNode;
            model.Nodes.Add(node);
            snapshot.Build(model, CompositionContext.Default);
            resource = snapshot.GetResource(0)!;
            var oldSource = new VideoSource();
            oldSource.ReadFrom(CreateMissingMediaUri("old.mp4"));
            var typedResource = oldSource.ToResource(CompositionContext.Default);
            oldResource = typedResource;
            oldOutput = new VideoSourceRenderNode(typedResource, 0, Brushes.Resource.White, null);
            SetPrivateField(resource, "_sourceResource", typedResource);
            SetPrivateField(resource, "_lastSource", oldSource);
            SetPrivateField(resource, "_cachedOutput", oldOutput);
        }
        else
        {
            var imageNode = new ImageSourceNode();
            var replacementSource = new ImageSource();
            replacementSource.ReadFrom(CreateMissingMediaUri("replacement.png"));
            imageNode.Source.Property!.SetValue(replacementSource);
            node = imageNode;
            model.Nodes.Add(node);
            snapshot.Build(model, CompositionContext.Default);
            resource = snapshot.GetResource(0)!;
            var oldSource = new ImageSource();
            oldSource.ReadFrom(CreateMissingMediaUri("old.png"));
            var typedResource = oldSource.ToResource(CompositionContext.Default);
            oldResource = typedResource;
            oldOutput = new ImageSourceRenderNode(typedResource, Brushes.Resource.White, null);
            SetPrivateField(resource, "_sourceResource", typedResource);
            SetPrivateField(resource, "_lastSource", oldSource);
            SetPrivateField(resource, "_cachedOutput", oldOutput);
        }

        SetResourceBusy(oldResource, true);
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default));
            Assert.Multiple(() =>
            {
                Assert.That(GetPrivateField(resource, "_sourceResource"), Is.SameAs(oldResource));
                Assert.That(GetPrivateField(resource, "_cachedOutput"), Is.SameAs(oldOutput));
                Assert.That(oldResource.IsDisposed, Is.False);
                Assert.That(oldOutput.IsDisposed, Is.False);
            });
        }
        finally
        {
            SetResourceBusy(oldResource, false);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ConfiguredNode_ReplacementWhileOldOwnerBusyLeavesPublishedStateUntouched(bool text)
    {
        GraphNode node = text ? new TextNode() : new FilterEffectNode<ReplacementProbeFilterEffect>();
        var model = new GraphModel();
        model.Nodes.Add(node);
        using var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        GraphNode.Resource resource = snapshot.GetResource(0)!;
        string ownerField = text ? "_textResource" : "_filterEffectResource";
        if (text)
        {
            var oldText = new TextBlock();
            TextBlock.Resource oldTextResource = oldText.ToResource(CompositionContext.Default);
            var oldTextOutput = new DrawableRenderNode(oldTextResource);
            SetPrivateField(resource, ownerField, oldTextResource);
            SetGeneratedPortValue(resource, "Output", oldTextOutput);
        }
        else
        {
            snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        }

        var oldResource = (EngineObject.Resource)GetPrivateField(resource, ownerField)!;
        object? oldOutput = text
            ? GetGeneratedPortValue(resource, "Output")
            : GetGeneratedPortValue(resource, "OutputPort");

        if (node is TextNode textNode)
            textNode.Object = new TextBlock();
        else
            ((FilterEffectNode<ReplacementProbeFilterEffect>)node).Object = new ReplacementProbeFilterEffect();

        SetResourceBusy(oldResource, true);
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default));
            Assert.Multiple(() =>
            {
                Assert.That(GetPrivateField(resource, ownerField), Is.SameAs(oldResource));
                Assert.That(
                    GetGeneratedPortValue(resource, text ? "Output" : "OutputPort"),
                    Is.SameAs(oldOutput));
                Assert.That(oldResource.IsDisposed, Is.False);
            });
        }
        finally
        {
            SetResourceBusy(oldResource, false);
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task NodeGraphHost_DisposeWhileSnapshotChildUpdatesRollsBackAndCanRetry(
        bool useFilterEffect,
        bool nestInGroup)
    {
        var child = new BlockingOwnedGraphNode();
        var model = new GraphModel();
        if (nestInGroup)
        {
            var group = new GroupNode();
            group.Group.Nodes.Add(child);
            model.Nodes.Add(group);
        }
        else
        {
            model.Nodes.Add(child);
        }

        EngineObject.Resource hostResource;
        if (useFilterEffect)
        {
            var effect = new NodeGraphFilterEffect();
            effect.Model.CurrentValue = model;
            hostResource = effect.ToResource(CompositionContext.Default);
        }
        else
        {
            var drawable = new NodeGraphDrawable();
            drawable.Model.CurrentValue = model;
            hostResource = drawable.ToResource(CompositionContext.Default);
        }

        BlockingOwnedGraphNode.Resource childResource = child.CreatedResource!;
        child.BlockNextUpdate = true;
        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                childResource.Update(child, CompositionContext.Default, ref updateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(child.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(hostResource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(hostResource.IsDisposed, Is.False);
                Assert.That(childResource.IsDisposed, Is.False);
                Assert.That(child.ResourceDisposeCount, Is.Zero);
            });
        }
        finally
        {
            child.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        hostResource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(hostResource.IsDisposed, Is.True);
            Assert.That(childResource.IsDisposed, Is.True,
                "retry must still own and sweep the snapshot child after reservation rollback");
            Assert.That(child.ResourceDisposeCount, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NodeGraphHost_DisposeWhileRootUpdateBuildsSnapshotFailsBeforeCleanup(bool useFilterEffect)
    {
        EngineObject host;
        EngineObject.Resource hostResource;
        if (useFilterEffect)
        {
            var effect = new NodeGraphFilterEffect();
            host = effect;
            hostResource = effect.ToResource(CompositionContext.Default);
        }
        else
        {
            var drawable = new NodeGraphDrawable();
            host = drawable;
            hostResource = drawable.ToResource(CompositionContext.Default);
        }

        var child = new BlockingOwnedGraphNode { BlockNextUpdate = true };
        var model = new GraphModel();
        model.Nodes.Add(child);
        switch (host)
        {
            case NodeGraphFilterEffect effect:
                effect.Model.CurrentValue = model;
                break;
            case NodeGraphDrawable drawable:
                drawable.Model.CurrentValue = model;
                break;
        }

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                hostResource.Update(host, CompositionContext.Default, ref updateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(child.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(hostResource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(hostResource.IsDisposed, Is.False);
                Assert.That(child.ResourceDisposeCount, Is.Zero);
            });
        }
        finally
        {
            child.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        BlockingOwnedGraphNode.Resource childResource = child.CreatedResource!;

        hostResource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(hostResource.IsDisposed, Is.True);
            Assert.That(childResource.IsDisposed, Is.True);
            Assert.That(child.ResourceDisposeCount, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NodeGraphHost_SameThreadReentryDuringSnapshotBuildFailsAndCanRetry(bool useFilterEffect)
    {
        EngineObject host;
        EngineObject.Resource hostResource;
        if (useFilterEffect)
        {
            var effect = new NodeGraphFilterEffect();
            host = effect;
            hostResource = effect.ToResource(CompositionContext.Default);
        }
        else
        {
            var drawable = new NodeGraphDrawable();
            host = drawable;
            hostResource = drawable.ToResource(CompositionContext.Default);
        }

        var child = new BlockingOwnedGraphNode();
        var model = new GraphModel();
        model.Nodes.Add(child);
        switch (host)
        {
            case NodeGraphFilterEffect effect:
                effect.Model.CurrentValue = model;
                break;
            case NodeGraphDrawable drawable:
                drawable.Model.CurrentValue = model;
                break;
        }

        child.DuringResourceUpdate = () =>
        {
            bool nestedUpdateOnly = false;
            hostResource.Update(host, CompositionContext.Default, ref nestedUpdateOnly);
        };

        bool updateOnly = false;
        Assert.Throws<InvalidOperationException>(
            () => hostResource.Update(host, CompositionContext.Default, ref updateOnly));
        Assert.That(hostResource.IsDisposed, Is.False);

        child.DuringResourceUpdate = null;
        updateOnly = false;
        Assert.DoesNotThrow(() => hostResource.Update(host, CompositionContext.Default, ref updateOnly));
        BlockingOwnedGraphNode.Resource childResource = child.CreatedResource!;

        hostResource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(hostResource.IsDisposed, Is.True);
            Assert.That(childResource.IsDisposed, Is.True);
        });
    }

    [Test]
    public async Task GraphSnapshot_DisposeWhileChildUpdatesRollsBackAndCanRetry()
    {
        var child = new BlockingOwnedGraphNode();
        var model = new GraphModel();
        model.Nodes.Add(child);
        var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        BlockingOwnedGraphNode.Resource childResource = child.CreatedResource!;
        child.BlockNextUpdate = true;
        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                childResource.Update(child, CompositionContext.Default, ref updateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(child.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(snapshot.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.GetResource(0), Is.SameAs(childResource),
                    "a failed reservation must leave the snapshot installed for retry");
                Assert.That(childResource.IsDisposed, Is.False);
                Assert.That(child.ResourceDisposeCount, Is.Zero);
            });
        }
        finally
        {
            child.ContinueUpdate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);

        snapshot.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.GetResource(0), Is.Null);
            Assert.That(childResource.IsDisposed, Is.True);
            Assert.That(child.ResourceDisposeCount, Is.EqualTo(1));
        });
        Assert.DoesNotThrow(snapshot.Dispose);
    }

    [Test]
    public async Task GraphNodeResource_StateViewsRejectMutationAndConcurrentCleanupReads()
    {
        var node = new BlockingUninitializeGraphNode();
        var model = new GraphModel();
        model.Nodes.Add(node);
        var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        GraphNode.Resource resource = snapshot.GetResource(0)!;
        IReadOnlyList<IItemValue> retainedValues = resource.ItemValues;
        IReadOnlyDictionary<INodeMember, int> retainedMap = resource.ItemIndexMap;
        var valueMutationSurface = (IList<IItemValue>)retainedValues;
        var mapMutationSurface = (IDictionary<INodeMember, int>)retainedMap;

        Assert.Multiple(() =>
        {
            Assert.That(valueMutationSurface.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => valueMutationSurface.Add(new ItemValue<int>()));
            Assert.That(mapMutationSurface.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => mapMutationSurface.Add(node.Output, 1));
        });

        Task<Exception?> disposeTask = Task.Run(() =>
        {
            try
            {
                snapshot.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(node.UninitializeEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() => _ = resource.SlotIndex);
                Assert.Throws<InvalidOperationException>(() => _ = resource.ItemValues);
                Assert.Throws<InvalidOperationException>(() => _ = resource.ItemIndexMap);
                Assert.Throws<InvalidOperationException>(() => _ = resource.Renderer);
                Assert.That(retainedValues, Has.Count.EqualTo(1));
                Assert.That(retainedMap, Has.Count.EqualTo(1));
            });
        }
        finally
        {
            node.ContinueUninitialize.Set();
        }

        Assert.That(await disposeTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.SlotIndex);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.ItemValues);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.ItemIndexMap);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Renderer);
            Assert.That(retainedValues, Has.Count.EqualTo(1));
            Assert.That(retainedMap, Has.Count.EqualTo(1));
        });
    }

    private static (Drawable.Resource Resource, IReadOnlyList<Drawable.Resource> Children)
        CreateDrawableContainerResource(bool useDecorator, params Drawable[] children)
    {
        (Drawable owner, IListProperty<Drawable> sourceChildren) = CreateDrawableContainer(useDecorator);
        foreach (Drawable child in children)
        {
            sourceChildren.Add(child);
        }

        Drawable.Resource resource = owner.ToResource(CompositionContext.Default);
        return (resource, GetDrawableContainerChildren(resource));
    }

    private static void SetPrivateField(object target, string name, object? value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }

    private static object? GetPrivateField(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    }

    private static object? GetGeneratedPortValue(GraphNode.Resource resource, string propertyName)
    {
        return resource.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(resource);
    }

    private static void SetGeneratedPortValue(GraphNode.Resource resource, string propertyName, object? value)
    {
        resource.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(resource, value);
    }

    private static Uri CreateMissingMediaUri(string fileName)
    {
        return new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"), fileName));
    }

    private static void SetResourceBusy(EngineObject.Resource resource, bool busy)
    {
        typeof(EngineObject.Resource).GetField(
            "_resourceOperationDepth",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(resource, busy ? 1 : 0);
    }

    private static (Drawable Owner, IListProperty<Drawable> Children) CreateDrawableContainer(bool useDecorator)
    {
        if (useDecorator)
        {
            var decorator = new DrawableDecorator();
            return (decorator, decorator.Children);
        }

        var group = new DrawableGroup();
        return (group, group.Children);
    }

    private static IReadOnlyList<Drawable.Resource> GetDrawableContainerChildren(Drawable.Resource resource)
    {
        return resource switch
        {
            DrawableGroup.Resource group => group.Children,
            DrawableDecorator.Resource decorator => decorator.Children,
            _ => throw new ArgumentException("The resource is not a drawable container.", nameof(resource)),
        };
    }

    private static void UpdateResource(Drawable.Resource resource, Drawable owner)
    {
        bool updateOnly = false;
        resource.Update(owner, CompositionContext.Default, ref updateOnly);
    }

    [SuppressResourceClassGeneration]
    private sealed class DisposalProbeDrawable(Exception? failure = null) : Drawable
    {
        public int DisposeCount { get; private set; }

        public Action? OnResourceDispose { get; set; }

        public override Drawable.Resource ToResource(CompositionContext context)
        {
            var resource = new ProbeResource(this, failure);
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => default;

        protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
        {
        }

        private sealed class ProbeResource(DisposalProbeDrawable owner, Exception? failure) : Drawable.Resource
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    owner.DisposeCount++;
                    owner.OnResourceDispose?.Invoke();
                    if (failure != null)
                        throw failure;
                }

                base.Dispose(disposing);
            }
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class AcquisitionThrowingDrawable(Exception failure) : Drawable
    {
        public int ToResourceCount { get; private set; }

        public override Drawable.Resource ToResource(CompositionContext context)
        {
            ToResourceCount++;
            throw failure;
        }

        protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => default;

        protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
        {
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class DisposalProbeFilterEffect : FilterEffect
    {
        public int DisposeCount { get; private set; }

        public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
        {
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource(this);
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource(DisposalProbeFilterEffect owner) : FilterEffect.Resource
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    owner.DisposeCount++;

                base.Dispose(disposing);
            }
        }
    }

    private sealed class ThrowingBrushResource(Exception failure) : Brush.Resource
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                throw failure;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CountingPenResource : Pen.Resource
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;

            base.Dispose(disposing);
        }
    }

    private sealed class CountingGeometryResource : Geometry.Resource
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;

            base.Dispose(disposing);
        }
    }
}

internal sealed partial class ReplacementProbeFilterEffect : FilterEffect
{
    public ReplacementProbeFilterEffect()
    {
    }

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }
}

internal sealed partial class BlockingOwnedDrawable : Drawable
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
        partial void PreUpdate(BlockingOwnedDrawable obj, CompositionContext context)
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

internal sealed partial class BlockingOwnedGeometry : Geometry
{
    public bool BlockNextUpdate { get; set; }

    public ManualResetEventSlim UpdateEntered { get; } = new();

    public ManualResetEventSlim ContinueUpdate { get; } = new();

    public int ResourceDisposeCount { get; private set; }

    public partial class Resource
    {
        partial void PreUpdate(BlockingOwnedGeometry obj, CompositionContext context)
        {
            if (!obj.BlockNextUpdate)
                return;

            obj.UpdateEntered.Set();
            if (!obj.ContinueUpdate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked geometry resource update was not released.");
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                GetOriginal().ResourceDisposeCount++;
        }
    }
}

internal sealed partial class BlockingOwnedGraphNode : GraphNode
{
    public bool BlockNextUpdate { get; set; }

    public Action? DuringResourceUpdate { get; set; }

    public ManualResetEventSlim UpdateEntered { get; } = new();

    public ManualResetEventSlim ContinueUpdate { get; } = new();

    public Resource? CreatedResource { get; private set; }

    public int ResourceDisposeCount { get; private set; }

    public partial class Resource
    {
        partial void PreUpdate(BlockingOwnedGraphNode obj, CompositionContext context)
        {
            obj.DuringResourceUpdate?.Invoke();

            if (!obj.BlockNextUpdate)
                return;

            obj.UpdateEntered.Set();
            if (!obj.ContinueUpdate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked graph-node resource update was not released.");
        }

        partial void PostUpdate(BlockingOwnedGraphNode obj, CompositionContext context)
        {
            obj.CreatedResource = this;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                GetOriginal().ResourceDisposeCount++;
        }
    }
}

internal sealed partial class BlockingUninitializeGraphNode : GraphNode
{
    public BlockingUninitializeGraphNode()
    {
        Output = AddOutput<int>("Output");
    }

    public OutputPort<int> Output { get; }

    public ManualResetEventSlim UninitializeEntered { get; } = new();

    public ManualResetEventSlim ContinueUninitialize { get; } = new();

    public partial class Resource
    {
        protected override void UninitializeCore()
        {
            BlockingUninitializeGraphNode node = GetOriginal();
            node.UninitializeEntered.Set();
            if (!node.ContinueUninitialize.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked graph-node uninitialize was not released.");
        }
    }
}

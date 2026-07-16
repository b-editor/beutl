using System.Reflection;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class EngineObjectResourceDisposalTests
{
    [Test]
    public void Dispose_WhenCleanupThrows_MarksDisposedAndDoesNotRetry()
    {
        var cleanupFailure = new InvalidOperationException("cleanup failure");
        var resource = new ThrowingResource(cleanupFailure);

        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(cleanupFailure));
                Assert.That(resource.IsDisposed, Is.True);
                Assert.That(resource.DisposeCalls, Is.EqualTo(1));
            });

            Assert.DoesNotThrow(resource.Dispose);
            Assert.That(resource.DisposeCalls, Is.EqualTo(1));
        }
        finally
        {
            GC.SuppressFinalize(resource);
        }
    }

    [Test]
    public void DirectlyConstructedResource_GetOriginalReportsMissingAssociation()
    {
        var resource = new EngineObject.Resource();

        try
        {
            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => resource.GetOriginal());
            Assert.That(failure!.Message, Does.Contain("first update"));
        }
        finally
        {
            resource.Dispose();
        }
    }

    [Test]
    public void Finalizer_SuppressesCleanupFailure()
    {
        var cleanupFailure = new InvalidOperationException("finalizer cleanup failure");
        var resource = new ThrowingResource(cleanupFailure);
        MethodInfo finalizer = typeof(EngineObject.Resource).GetMethod(
            "Finalize",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            Assert.DoesNotThrow(() => finalizer.Invoke(resource, null));
            Assert.Multiple(() =>
            {
                Assert.That(resource.DisposeCalls, Is.EqualTo(1));
                Assert.That(resource.LastDisposing, Is.False);
                Assert.That(resource.IsDisposed, Is.True);
            });
        }
        finally
        {
            GC.SuppressFinalize(resource);
        }
    }

    [Test]
    public void Finalizer_PreventsManagedReentryAndMarksDisposedAfterCleanup()
    {
        var resource = new ReentrantFinalizerResource();
        MethodInfo finalizer = typeof(EngineObject.Resource).GetMethod(
            "Finalize",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            Assert.DoesNotThrow(() => finalizer.Invoke(resource, null));
            Assert.Multiple(() =>
            {
                Assert.That(resource.DisposeCalls, Is.EqualTo(1));
                Assert.That(resource.LastDisposing, Is.False);
                Assert.That(resource.IsDisposed, Is.True);
            });
        }
        finally
        {
            GC.SuppressFinalize(resource);
        }
    }

    [Test]
    public void Finalizer_WhenOwnedDescendantIsBusy_RollsBackEarlierReservations()
    {
        var first = new FinalizerOwnedResource();
        var busy = new FinalizerOwnedResource();
        var resource = new FinalizerOwnershipResource(first, busy);
        using var operationEntered = new ManualResetEventSlim();
        using var releaseOperation = new ManualResetEventSlim();
        Task operation = Task.Run(() => busy.BlockOperation(operationEntered, releaseOperation));
        MethodInfo finalizer = typeof(EngineObject.Resource).GetMethod(
            "Finalize",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            Assert.That(operationEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Assert.DoesNotThrow(() => finalizer.Invoke(resource, null));
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.True);
                Assert.That(first.IsDisposed, Is.False);
                Assert.That(busy.IsDisposed, Is.False);
            });

            Assert.DoesNotThrow(first.Dispose,
                "an earlier owned reservation must return to the active state when a later reservation fails");
            Assert.That(first.IsDisposed, Is.True);
        }
        finally
        {
            releaseOperation.Set();
            Assert.That(operation.Wait(TimeSpan.FromSeconds(10)), Is.True);
            busy.Dispose();
            GC.SuppressFinalize(resource);
            GC.SuppressFinalize(first);
            GC.SuppressFinalize(busy);
        }
    }

    [Test]
    public void GeneratedToResource_WhenUpdateAndCleanupThrow_PreservesUpdateFailureAndCleansUp()
    {
        var updateFailure = new InvalidOperationException("update failure");
        var cleanupFailure = new InvalidOperationException("cleanup failure");
        var obj = new GeneratedThrowingUpdateObject(updateFailure, cleanupFailure);
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => obj.ToResource(context));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(updateFailure));
            Assert.That(obj.ResourceDisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void GeneratedResource_AfterDisposeRejectsUpdateBeforeHookAndPropertyMutation()
    {
        var obj = new GeneratedDisposedGuardObject();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedDisposedGuardObject.Resource)obj.ToResource(context);
        Assert.That(obj.PreUpdateCalls, Is.EqualTo(1));
        resource.Dispose();

        bool updateOnly = false;
        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => resource.Value = 42);
            Assert.Throws<ObjectDisposedException>(() => resource.Update(obj, context, ref updateOnly));
            Assert.That(obj.PreUpdateCalls, Is.EqualTo(1),
                "extension hooks must not run after the generated resource is disposed");
            Assert.That(obj.PostDisposeCalls, Is.EqualTo(1),
                "generated property setters must remain usable by cleanup hooks");
        });
    }

    [Test]
    public void GeneratedResource_WrongUpdateType_DoesNotReplaceOriginal()
    {
        var owner = new GeneratedDisposedGuardObject();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedDisposedGuardObject.Resource)owner.ToResource(context);
        var wrongOwner = new EngineObject();
        bool updateOnly = false;

        try
        {
            Assert.Throws<InvalidCastException>(
                () => resource.Update(wrongOwner, context, ref updateOnly));
            Assert.That(resource.GetOriginal(), Is.SameAs(owner),
                "type validation must complete before the resource publishes a new original owner");

            Assert.DoesNotThrow(() => resource.Update(owner, context, ref updateOnly));
            Assert.That(resource.GetOriginal(), Is.SameAs(owner));
        }
        finally
        {
            resource.Dispose();
        }
    }

    [Test]
    public void GeneratedListCleanup_ReadOnlySnapshotRejectsMutationAndStillSweepsEveryChild()
    {
        var first = new GeneratedListCleanupChild();
        var second = new GeneratedListCleanupChild();
        var owner = new GeneratedListCleanupOwner();
        owner.Children.Add(first);
        owner.Children.Add(second);
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedListCleanupOwner.Resource)owner.ToResource(context);
        IReadOnlyList<GeneratedListCleanupChild.Resource> retained = resource.Children;
        var retainedCollection = (ICollection<GeneratedListCleanupChild.Resource>)retained;
        first.OnResourceDispose = retainedCollection.Clear;

        NotSupportedException? actual = Assert.Throws<NotSupportedException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(retainedCollection.IsReadOnly, Is.True);
            Assert.That(retained, Has.Count.EqualTo(2),
                "the published snapshot must not alias the mutable ownership list");
            Assert.That(first.ResourceDisposeCalls, Is.EqualTo(1));
            Assert.That(second.ResourceDisposeCalls, Is.EqualTo(1),
                "a failed external mutation must not interrupt the ownership sweep");
        });
    }

    [Test]
    public void GeneratedListReplacement_WhenOldCleanupFails_PublishesCommittedSnapshotBeforeRethrow()
    {
        var failure = new InvalidOperationException("old list child cleanup");
        var previous = new GeneratedListCleanupChild { OnResourceDispose = () => throw failure };
        var replacement = new GeneratedListCleanupChild();
        var owner = new GeneratedListCleanupOwner();
        owner.Children.Add(previous);
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedListCleanupOwner.Resource)owner.ToResource(context);
        IReadOnlyList<GeneratedListCleanupChild.Resource> retainedSnapshot = resource.Children;
        GeneratedListCleanupChild.Resource previousResource = retainedSnapshot[0];
        owner.Children.Clear();
        owner.Children.Add(replacement);

        bool updateOnly = false;
        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => resource.Update(owner, context, ref updateOnly));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(previousResource.IsDisposed, Is.True);
            Assert.That(resource.Children, Is.Not.SameAs(retainedSnapshot));
            Assert.That(resource.Children, Has.Count.EqualTo(1));
            Assert.That(resource.Children[0].GetOriginal(), Is.SameAs(replacement));
            Assert.That(retainedSnapshot[0], Is.SameAs(previousResource));
            Assert.That(previous.ResourceDisposeCalls, Is.EqualTo(1));
            Assert.That(replacement.ResourceDisposeCalls, Is.Zero);
        });

        resource.Dispose();
        Assert.That(replacement.ResourceDisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void GeneratedUpdate_WhenPropertyEvaluationDisposesResource_FailsBeforeAcquisitionAndRemainsRetryable()
    {
        var owner = new GeneratedReentrantUpdateOwner();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedReentrantUpdateOwner.Resource)owner.ToResource(context);
        var child = new GeneratedListCleanupChild();
        owner.Child.CurrentValue = child;
        var reentrantContext = new CallbackCompositionContext(resource.Dispose);

        bool updateOnly = false;
        Assert.Throws<InvalidOperationException>(() => resource.Update(owner, reentrantContext, ref updateOnly));

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.False);
            Assert.That(child.ResourceDisposeCalls, Is.Zero,
                "the failed disposal must occur before the property value is converted into an owned resource");
        });

        resource.Dispose();
        Assert.That(resource.IsDisposed, Is.True);
    }

    [Test]
    public async Task GeneratedUpdate_WhenDisposeRunsOnAnotherThread_FailsFastWithoutCorruptingUpdate()
    {
        var owner = new GeneratedBlockingUpdateObject();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedBlockingUpdateObject.Resource)owner.ToResource(context);
        owner.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                resource.Update(owner, context, ref updateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(owner.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Task<Exception?> disposeTask = Task.Run(() =>
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
        });

        try
        {
            Exception? disposeFailure = await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(disposeFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(resource.IsDisposed, Is.False,
                "a rejected concurrent dispose must leave the resource live and retryable");
        }
        finally
        {
            owner.ContinueUpdate.Set();
        }

        Exception? updateFailure = await updateTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(updateFailure, Is.Null);
            Assert.That(resource.IsDisposed, Is.False);
        });

        resource.Dispose();
        Assert.That(resource.IsDisposed, Is.True);
    }

    [Test]
    public void HandwrittenSelectorRead_DoesNotHoldLifecycleMonitorAcrossCallback()
    {
        var resource = new BlockingSelectorResource();

        Exception? cleanupFailure = resource.ReadWhileCleanupAttempts();

        Assert.Multiple(() =>
        {
            Assert.That(cleanupFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(resource.IsDisposed, Is.False,
                "cleanup rejected during a selector read must remain retryable");
        });

        Assert.DoesNotThrow(resource.Dispose);
        Assert.That(resource.IsDisposed, Is.True);
    }

    [Test]
    public void HandwrittenSelectorRead_RejectsGeneratedUpdateReentry()
    {
        var resource = new BlockingSelectorResource();

        Exception? failure = resource.ReadWhileUpdateAttempts();

        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        Assert.DoesNotThrow(resource.Dispose);
    }

    [Test]
    public void HandwrittenSelectorRead_CanNestInsideOwningOperationAndRestoresState()
    {
        var resource = new BlockingSelectorResource();

        Assert.DoesNotThrow(() => resource.ReadInsideExclusiveOperation());
        Assert.DoesNotThrow(() =>
        {
            bool updateOnly = false;
            resource.Update(new EngineObject(), CompositionContext.Default, ref updateOnly);
        });

        resource.Dispose();
    }

    [Test]
    public void HandwrittenSelectorRead_RemainsAvailableToCleanupOwner()
    {
        var resource = new CleanupSelectorResource();

        Assert.DoesNotThrow(resource.Dispose);
        Assert.That(resource.ProjectedDuringCleanup, Is.EqualTo(42));
    }

    [Test]
    public async Task GeneratedUpdate_WhenValueSetterRunsOnAnotherThread_RejectsMutation()
    {
        var owner = new GeneratedBlockingUpdateObject();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedBlockingUpdateObject.Resource)owner.ToResource(context);
        owner.BlockNextUpdate = true;

        Task updateTask = Task.Run(() =>
        {
            bool updateOnly = false;
            resource.Update(owner, context, ref updateOnly);
        });

        Assert.That(owner.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(() => resource.Value = 42);
            Assert.Throws<InvalidOperationException>(() => _ = resource.Value);
        }
        finally
        {
            owner.ContinueUpdate.Set();
        }

        await updateTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(resource.Value, Is.Zero);
        resource.Value = 42;
        Assert.That(resource.Value, Is.EqualTo(42));
    }

    [Test]
    public async Task GeneratedUpdate_WhenTaskRunDisposesSameResource_FailsFastWithoutDeadlock()
    {
        var owner = new GeneratedBlockingUpdateObject();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedBlockingUpdateObject.Resource)owner.ToResource(context);
        owner.DuringUpdate = () =>
        {
            using var disposeStarted = new ManualResetEventSlim();
            Task disposeTask = Task.Run(() =>
            {
                disposeStarted.Set();
                resource.Dispose();
            });
            if (!disposeStarted.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The worker-thread dispose did not start.");

            disposeTask.GetAwaiter().GetResult();
        };

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                resource.Update(owner, context, ref updateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Exception? failure = await updateTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(resource.IsDisposed, Is.False,
                "the failed cross-thread dispose claim must be rolled back so cleanup can be retried");
        });

        resource.Dispose();
        Assert.That(resource.IsDisposed, Is.True);
    }

    [Test]
    public async Task Dispose_WhenCalledConcurrently_FailsFastAndInvokesCleanupOnce()
    {
        var resource = new CoordinatedDisposeResource();
        Task first = Task.Run(resource.Dispose);
        Assert.That(resource.DisposeEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Task<Exception?> second = Task.Run(CaptureDisposeFailure);

        try
        {
            Exception? concurrentFailure = await second.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(concurrentFailure, Is.TypeOf<InvalidOperationException>());
        }
        finally
        {
            resource.ContinueDispose.Set();
        }

        await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(resource.DisposeCalls, Is.EqualTo(1));
            Assert.That(resource.IsDisposed, Is.True);
        });

        Exception? CaptureDisposeFailure()
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

    [Test]
    public async Task Dispose_WhenConcurrentCleanupFails_OwnerRetainsExactFailureAndContenderFailsFast()
    {
        var cleanupFailure = new InvalidOperationException("coordinated cleanup failure");
        var resource = new CoordinatedDisposeResource(cleanupFailure);
        Task<Exception?> first = Task.Run(CaptureDisposeFailure);
        Assert.That(resource.DisposeEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Task<Exception?> second = Task.Run(CaptureDisposeFailure);

        try
        {
            Exception? secondFailure = await second.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(secondFailure, Is.TypeOf<InvalidOperationException>());
        }
        finally
        {
            resource.ContinueDispose.Set();
        }

        Exception? firstFailure = await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(firstFailure, Is.SameAs(cleanupFailure));
            Assert.That(resource.DisposeCalls, Is.EqualTo(1));
            Assert.That(resource.IsDisposed, Is.True);
        });

        Exception? CaptureDisposeFailure()
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

    [Test]
    public void Dispose_LegacyIsDisposedGuardRunsCleanupBeforeStateBecomesDisposed()
    {
        var resource = new LegacyGuardedDisposeResource();

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposedInsideCleanup, Is.False);
            Assert.That(resource.DisposeCalls, Is.EqualTo(1));
            Assert.That(resource.IsDisposed, Is.True);
        });
    }

    [Test]
    public async Task Dispose_WhenCleanupThreadHopsBackToSameResource_FailsFastWithoutDeadlock()
    {
        var resource = new ThreadHoppingDisposeResource();

        await Task.Run(resource.Dispose).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(resource.NestedFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(resource.IsDisposed, Is.True);
        });
    }

    [Test]
    public void GeneratedToResource_WhenPreUpdateThrows_CleanupCanUseOriginal()
    {
        var updateFailure = new InvalidOperationException("pre-update failure");
        var owner = new GeneratedThrowingPreUpdateObject(updateFailure);
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => owner.ToResource(context));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(updateFailure));
            Assert.That(owner.ResourceDisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void GeneratedCleanup_WhenManualOverrideThrowsBeforeBase_StillSweepsGeneratedOwnership()
    {
        var cleanupFailure = new InvalidOperationException("manual cleanup failure");
        var child = new GeneratedListCleanupChild();
        var owner = new GeneratedManualThrowingCleanupOwner(cleanupFailure);
        owner.Child.CurrentValue = child;
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedManualThrowingCleanupOwner.Resource)owner.ToResource(context);
        GeneratedListCleanupChild.Resource childResource = resource.Child!;

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(resource.IsDisposed, Is.True);
            Assert.That(childResource.IsDisposed, Is.True);
            Assert.That(child.ResourceDisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void GeneratedCleanup_DirectlyConstructedDerivedResourceRunsEveryLayerAfterFailure()
    {
        var failure = new InvalidOperationException("derived cleanup");
        var resource = new GeneratedLifecycleDerivedObject.Resource
        {
            DerivedCleanupFailure = failure,
        };

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(resource.LifecycleEvents, Is.EqualTo(new[]
            {
                "derived-prepare",
                "base-prepare",
                "derived-cleanup",
                "base-cleanup",
            }));
            Assert.That(resource.IsDisposed, Is.True);
        });
    }

    [Test]
    public void GeneratedCleanup_PrepareCallbackGetterOnAnotherThread_FailsFast()
    {
        var resource = new GeneratedLifecycleDerivedObject.Resource();
        resource.BasePrepareCallback = () =>
        {
            Task<Exception?> getter = Task.Run(() =>
            {
                try
                {
                    _ = resource.Child;
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            Assert.That(getter.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "cleanup preparation must not hold a generated-layer monitor while invoking extension hooks");
            Assert.That(getter.Result, Is.TypeOf<InvalidOperationException>());
        };

        Assert.DoesNotThrow(resource.Dispose);
        Assert.That(resource.IsDisposed, Is.True);
    }

    [Test]
    public void GeneratedCleanup_PrepareFailureRollsBackLayersInReverseAndRemainsRetryable()
    {
        var prepareFailure = new InvalidOperationException("base prepare");
        var rollbackFailure = new InvalidOperationException("base rollback");
        var resource = new GeneratedLifecycleDerivedObject.Resource
        {
            BasePrepareFailure = prepareFailure,
            BaseRollbackFailure = rollbackFailure,
        };

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(prepareFailure), "rollback failures must not replace reservation failure");
            Assert.That(resource.LifecycleEvents, Is.EqualTo(new[]
            {
                "derived-prepare",
                "base-prepare",
                "base-rollback",
                "derived-rollback",
            }));
            Assert.That(resource.IsDisposed, Is.False);
        });

        resource.LifecycleEvents.Clear();
        Assert.DoesNotThrow(resource.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(resource.LifecycleEvents, Is.EqualTo(new[]
            {
                "derived-prepare",
                "base-prepare",
                "derived-cleanup",
                "base-cleanup",
            }));
            Assert.That(resource.IsDisposed, Is.True);
        });
    }

    [Test]
    public void GeneratedObjectSetter_CannotReplaceAnOwnedResource()
    {
        var owner = new GeneratedReentrantUpdateOwner();
        var first = new GeneratedListCleanupChild();
        var second = new GeneratedListCleanupChild();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedReentrantUpdateOwner.Resource)owner.ToResource(context);
        var firstResource = (GeneratedListCleanupChild.Resource)first.ToResource(context);
        var secondResource = (GeneratedListCleanupChild.Resource)second.ToResource(context);

        resource.Child = firstResource;
        Assert.Throws<InvalidOperationException>(() => resource.Child = secondResource);

        resource.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(first.ResourceDisposeCalls, Is.EqualTo(1));
            Assert.That(firstResource.IsDisposed, Is.True);
            Assert.That(second.ResourceDisposeCalls, Is.Zero);
            Assert.That(secondResource.IsDisposed, Is.False);
        });

        secondResource.Dispose();
    }

    [Test]
    public void GeneratedInheritedObjectSetter_CannotReenterDerivedUpdate()
    {
        var owner = new GeneratedLifecycleDerivedObject();
        var child = new GeneratedListCleanupChild();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedLifecycleDerivedObject.Resource)owner.ToResource(context);
        var childResource = (GeneratedListCleanupChild.Resource)child.ToResource(context);
        owner.InheritedChildToAssign = childResource;
        owner.AssignInheritedChildDuringUpdate = true;
        bool updateOnly = false;

        try
        {
            Assert.Throws<InvalidOperationException>(
                () => resource.Update(owner, context, ref updateOnly));
            Assert.That(resource.Child, Is.Null,
                "an inherited ownership setter must not nest inside the still-running derived update");
            Assert.That(childResource.IsDisposed, Is.False);
        }
        finally
        {
            owner.AssignInheritedChildDuringUpdate = false;
            resource.Dispose();
            childResource.Dispose();
        }
    }

    [Test]
    public async Task GeneratedParentDispose_WhenOwnedChildIsUpdating_RollsBackWholeGraphAndCanRetry()
    {
        var child = new GeneratedBlockingUpdateObject();
        var owner = new GeneratedBlockingChildOwner();
        owner.Child.CurrentValue = child;
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedBlockingChildOwner.Resource)owner.ToResource(context);
        GeneratedBlockingUpdateObject.Resource childResource = resource.Child!;
        child.BlockNextUpdate = true;

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                childResource.Update(child, context, ref updateOnly);
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
            Assert.Throws<InvalidOperationException>(resource.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(childResource.IsDisposed, Is.False);
                Assert.That(resource.Child, Is.SameAs(childResource),
                    "a failed graph reservation must preserve the complete ownership graph");
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
            Assert.That(childResource.IsDisposed, Is.True);
            Assert.That(child.ResourceDisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GeneratedObjectReplacement_WhenPreviousChildIsUpdating_PreservesOwnerAndDisposesUnpublishedReplacement()
    {
        var first = new GeneratedBlockingUpdateObject();
        var second = new GeneratedBlockingUpdateObject();
        var owner = new GeneratedBlockingChildOwner();
        owner.Child.CurrentValue = first;
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedBlockingChildOwner.Resource)owner.ToResource(context);
        GeneratedBlockingUpdateObject.Resource firstResource = resource.Child!;
        owner.Child.CurrentValue = second;
        first.BlockNextUpdate = true;

        Task updateTask = Task.Run(() =>
        {
            bool childUpdateOnly = false;
            firstResource.Update(first, context, ref childUpdateOnly);
        });

        Assert.That(first.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            bool updateOnly = false;
            Assert.Throws<InvalidOperationException>(() => resource.Update(owner, context, ref updateOnly));
            Assert.Multiple(() =>
            {
                Assert.That(resource.Child, Is.SameAs(firstResource));
                Assert.That(firstResource.IsDisposed, Is.False);
                Assert.That(second.ResourceDisposeCalls, Is.EqualTo(1),
                    "the replacement acquired before reservation failure must not leak");
            });
        }
        finally
        {
            first.ContinueUpdate.Set();
        }

        await updateTask.WaitAsync(TimeSpan.FromSeconds(10));
        bool retryUpdateOnly = false;
        Assert.DoesNotThrow(() => resource.Update(owner, context, ref retryUpdateOnly));
        Assert.Multiple(() =>
        {
            Assert.That(resource.Child!.GetOriginal(), Is.SameAs(second));
            Assert.That(firstResource.IsDisposed, Is.True);
            Assert.That(first.ResourceDisposeCalls, Is.EqualTo(1));
            Assert.That(second.ResourceDisposeCalls, Is.EqualTo(1));
        });

        resource.Dispose();
        Assert.That(second.ResourceDisposeCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task GeneratedListReplacementAndRemoval_WhenOneChildIsUpdating_RollsBackWholeBatch()
    {
        var first = new GeneratedBlockingUpdateObject();
        var second = new GeneratedBlockingUpdateObject();
        var replacement = new GeneratedBlockingUpdateObject();
        var owner = new GeneratedBlockingListOwner();
        owner.Children.Add(first);
        owner.Children.Add(second);
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedBlockingListOwner.Resource)owner.ToResource(context);
        IReadOnlyList<GeneratedBlockingUpdateObject.Resource> retainedSnapshot = resource.Children;
        GeneratedBlockingUpdateObject.Resource secondResource = retainedSnapshot[1];
        int retainedVersion = resource.Version;
        second.BlockNextUpdate = true;
        owner.Children.Clear();
        owner.Children.Add(replacement);

        Task updateTask = Task.Run(() =>
        {
            bool childUpdateOnly = false;
            secondResource.Update(second, context, ref childUpdateOnly);
        });

        Assert.That(second.UpdateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            bool updateOnly = false;
            Assert.Throws<InvalidOperationException>(() => resource.Update(owner, context, ref updateOnly));
            Assert.Multiple(() =>
            {
                Assert.That(resource.Children, Is.SameAs(retainedSnapshot));
                Assert.That(resource.Children, Has.Count.EqualTo(2));
                Assert.That(resource.Version, Is.EqualTo(retainedVersion));
                Assert.That(retainedSnapshot[0].IsDisposed, Is.False);
                Assert.That(retainedSnapshot[1].IsDisposed, Is.False);
                Assert.That(first.ResourceDisposeCalls, Is.Zero);
                Assert.That(second.ResourceDisposeCalls, Is.Zero);
                Assert.That(replacement.ResourceDisposeCalls, Is.EqualTo(1),
                    "every unpublished replacement must be reclaimed when batch reservation rolls back");
            });
        }
        finally
        {
            second.ContinueUpdate.Set();
        }

        await updateTask.WaitAsync(TimeSpan.FromSeconds(10));
        bool retryUpdateOnly = false;
        Assert.DoesNotThrow(() => resource.Update(owner, context, ref retryUpdateOnly));
        Assert.Multiple(() =>
        {
            Assert.That(resource.Children, Has.Count.EqualTo(1));
            Assert.That(resource.Children, Is.Not.SameAs(retainedSnapshot));
            Assert.That(resource.Children[0].GetOriginal(), Is.SameAs(replacement));
            Assert.That(resource.Version, Is.GreaterThan(retainedVersion));
            Assert.That(first.ResourceDisposeCalls, Is.EqualTo(1));
            Assert.That(second.ResourceDisposeCalls, Is.EqualTo(1));
            Assert.That(replacement.ResourceDisposeCalls, Is.EqualTo(1));
        });

        resource.Dispose();
        Assert.That(replacement.ResourceDisposeCalls, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GeneratedUpdate_WhenSameResourceReenters_FailsFastWithoutDeadlock(bool useTaskRun)
    {
        var owner = new GeneratedBlockingUpdateObject();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedBlockingUpdateObject.Resource)owner.ToResource(context);
        owner.DuringUpdate = () =>
        {
            void Reenter()
            {
                bool nestedUpdateOnly = false;
                resource.Update(owner, context, ref nestedUpdateOnly);
            }

            if (useTaskRun)
            {
                Task.Run(Reenter).GetAwaiter().GetResult();
            }
            else
            {
                Reenter();
            }
        };

        Task<Exception?> updateTask = Task.Run(() =>
        {
            try
            {
                bool updateOnly = false;
                resource.Update(owner, context, ref updateOnly);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Exception? failure = await updateTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        Assert.That(resource.IsDisposed, Is.False);
    }

    [Test]
    public void GeneratedUpdate_CommonPathDoesNotAllocateLifecycleObjects()
    {
        var owner = new GeneratedDisposedGuardObject();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        var resource = (GeneratedDisposedGuardObject.Resource)owner.ToResource(context);

        for (int i = 0; i < 10; i++)
        {
            bool warmupUpdateOnly = false;
            resource.Update(owner, context, ref warmupUpdateOnly);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            bool updateOnly = false;
            resource.Update(owner, context, ref updateOnly);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero,
            "the generated lifecycle reservation runs on every frame and must remain allocation-free");
    }

    private sealed class ThrowingResource(Exception cleanupFailure) : EngineObject.Resource
    {
        public int DisposeCalls { get; private set; }

        public bool? LastDisposing { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCalls++;
            LastDisposing = disposing;
            throw cleanupFailure;
        }
    }

    private sealed class ReentrantFinalizerResource : EngineObject.Resource
    {
        public int DisposeCalls { get; private set; }

        public bool? LastDisposing { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCalls++;
            LastDisposing = disposing;
            if (!disposing)
            {
                Dispose();
            }
        }
    }

    private sealed class FinalizerOwnershipResource(
        FinalizerOwnedResource first,
        FinalizerOwnedResource second) : EngineObject.Resource
    {
        protected override void PrepareGeneratedResourceCleanupCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            context.Reserve(first);
            context.Reserve(second);
        }

        protected override void CleanupGeneratedResourceCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            context.DisposeOwned(first);
            context.DisposeOwned(second);
        }
    }

    private sealed class FinalizerOwnedResource : EngineObject.Resource
    {
        public void BlockOperation(ManualResetEventSlim entered, ManualResetEventSlim release)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The finalizer-owned resource operation was not released.");
        }
    }

    private sealed class CoordinatedDisposeResource(Exception? cleanupFailure = null) : EngineObject.Resource
    {
        public ManualResetEventSlim DisposeEntered { get; } = new();

        public ManualResetEventSlim ContinueDispose { get; } = new();

        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCalls++;
            DisposeEntered.Set();
            if (!ContinueDispose.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The coordinated dispose was not released.");

            if (cleanupFailure != null)
                throw cleanupFailure;
        }
    }

    private sealed class BlockingSelectorResource : EngineObject.Resource
    {
        private int _state;

        public Exception? ReadWhileCleanupAttempts()
        {
            return ReadGeneratedResourceState(ref _state, _ =>
            {
                Task<Exception?> cleanup = Task.Run(() =>
                {
                    try
                    {
                        Dispose();
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                if (!cleanup.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The cleanup contender blocked on the selector callback monitor.");
                return cleanup.Result;
            });
        }

        public Exception? ReadWhileUpdateAttempts()
        {
            return ReadGeneratedResourceState(ref _state, _ =>
            {
                try
                {
                    bool updateOnly = false;
                    Update(new EngineObject(), CompositionContext.Default, ref updateOnly);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });
        }

        public int ReadInsideExclusiveOperation()
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            return ReadGeneratedResourceState(ref _state, static value => value);
        }
    }

    private sealed class CleanupSelectorResource : EngineObject.Resource
    {
        private int _state = 42;

        public int ProjectedDuringCleanup { get; private set; }

        protected override void Dispose(bool disposing)
        {
            ProjectedDuringCleanup = ReadGeneratedResourceState(ref _state, static value => value);
        }
    }

    private sealed class LegacyGuardedDisposeResource : EngineObject.Resource
    {
        public int DisposeCalls { get; private set; }

        public bool? IsDisposedInsideCleanup { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposedInsideCleanup = IsDisposed;
            if (IsDisposed)
                return;

            DisposeCalls++;
        }
    }

    private sealed class ThreadHoppingDisposeResource : EngineObject.Resource
    {
        public Exception? NestedFailure { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            var thread = new Thread(() =>
            {
                try
                {
                    Dispose();
                }
                catch (Exception ex)
                {
                    NestedFailure = ex;
                }
            });
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The cross-thread cleanup call deadlocked.");
        }
    }
}

internal sealed class CallbackCompositionContext(Action afterFirstGet)
    : CompositionContext(TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame)
{
    private Action? _afterFirstGet = afterFirstGet;

    public override T Get<T>(IProperty<T> property)
    {
        T value = base.Get(property);
        Interlocked.Exchange(ref _afterFirstGet, null)?.Invoke();
        return value;
    }
}

internal sealed partial class GeneratedThrowingUpdateObject(
    Exception updateFailure,
    Exception cleanupFailure) : EngineObject
{
    private Exception UpdateFailure { get; } = updateFailure;

    private Exception CleanupFailure { get; } = cleanupFailure;

    public int ResourceDisposeCalls { get; private set; }

    public partial class Resource
    {
        partial void PostUpdate(GeneratedThrowingUpdateObject obj, CompositionContext context)
            => throw obj.UpdateFailure;

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                GetOriginal().ResourceDisposeCalls++;
                throw GetOriginal().CleanupFailure;
            }
        }
    }
}

internal sealed partial class GeneratedThrowingPreUpdateObject(Exception updateFailure) : EngineObject
{
    private Exception UpdateFailure { get; } = updateFailure;

    public int ResourceDisposeCalls { get; private set; }

    public partial class Resource
    {
        partial void PreUpdate(GeneratedThrowingPreUpdateObject obj, CompositionContext context)
            => throw obj.UpdateFailure;

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                GetOriginal().ResourceDisposeCalls++;
        }
    }
}

internal sealed partial class GeneratedManualThrowingCleanupOwner : EngineObject
{
    private readonly Exception _cleanupFailure;

    public GeneratedManualThrowingCleanupOwner(Exception cleanupFailure)
    {
        _cleanupFailure = cleanupFailure;
        ScanProperties<GeneratedManualThrowingCleanupOwner>();
    }

    public IProperty<GeneratedListCleanupChild?> Child { get; }
        = Property.Create<GeneratedListCleanupChild?>();

    private Exception CleanupFailure => _cleanupFailure;

    public partial class Resource
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                throw GetOriginal().CleanupFailure;
        }
    }
}

internal sealed partial class GeneratedDisposedGuardObject : EngineObject
{
    public GeneratedDisposedGuardObject()
    {
        ScanProperties<GeneratedDisposedGuardObject>();
    }

    public IProperty<int> Value { get; } = Property.Create(0);

    public int PreUpdateCalls { get; private set; }

    public int PostDisposeCalls { get; private set; }

    public partial class Resource
    {
        partial void PreUpdate(GeneratedDisposedGuardObject obj, CompositionContext context)
            => obj.PreUpdateCalls++;

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                Value = 0;
                GetOriginal().PostDisposeCalls++;
            }
        }
    }
}

internal sealed partial class GeneratedListCleanupOwner : EngineObject
{
    public GeneratedListCleanupOwner()
    {
        ScanProperties<GeneratedListCleanupOwner>();
    }

    public IListProperty<GeneratedListCleanupChild> Children { get; }
        = Property.CreateList<GeneratedListCleanupChild>();
}

internal sealed partial class GeneratedReentrantUpdateOwner : EngineObject
{
    public GeneratedReentrantUpdateOwner()
    {
        ScanProperties<GeneratedReentrantUpdateOwner>();
    }

    public IProperty<GeneratedListCleanupChild?> Child { get; }
        = Property.Create<GeneratedListCleanupChild?>();
}

internal sealed partial class GeneratedBlockingChildOwner : EngineObject
{
    public GeneratedBlockingChildOwner()
    {
        ScanProperties<GeneratedBlockingChildOwner>();
    }

    public IProperty<GeneratedBlockingUpdateObject?> Child { get; }
        = Property.Create<GeneratedBlockingUpdateObject?>();
}

internal sealed partial class GeneratedBlockingListOwner : EngineObject
{
    public GeneratedBlockingListOwner()
    {
        ScanProperties<GeneratedBlockingListOwner>();
    }

    public IListProperty<GeneratedBlockingUpdateObject> Children { get; }
        = Property.CreateList<GeneratedBlockingUpdateObject>();
}

internal sealed partial class GeneratedBlockingUpdateObject : EngineObject
{
    public GeneratedBlockingUpdateObject()
    {
        ScanProperties<GeneratedBlockingUpdateObject>();
    }

    public IProperty<int> Value { get; } = Property.Create(0);

    public bool BlockNextUpdate { get; set; }

    public Action? DuringUpdate { get; set; }

    public ManualResetEventSlim UpdateEntered { get; } = new();

    public ManualResetEventSlim ContinueUpdate { get; } = new();

    public int ResourceDisposeCalls { get; private set; }

    public partial class Resource
    {
        partial void PreUpdate(GeneratedBlockingUpdateObject obj, CompositionContext context)
        {
            obj.DuringUpdate?.Invoke();

            if (!obj.BlockNextUpdate)
                return;

            obj.UpdateEntered.Set();
            if (!obj.ContinueUpdate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked generated resource update was not released.");
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                GetOriginal().ResourceDisposeCalls++;
        }
    }
}

internal sealed partial class GeneratedListCleanupChild : EngineObject
{
    public int ResourceDisposeCalls { get; private set; }

    public Action? OnResourceDispose { get; set; }

    public partial class Resource
    {
        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            GeneratedListCleanupChild owner = GetOriginal();
            owner.ResourceDisposeCalls++;
            owner.OnResourceDispose?.Invoke();
        }
    }
}

internal partial class GeneratedLifecycleBaseObject : EngineObject
{
    public GeneratedLifecycleBaseObject()
    {
        ScanProperties<GeneratedLifecycleBaseObject>();
    }

    public IProperty<GeneratedListCleanupChild?> Child { get; }
        = Property.Create<GeneratedListCleanupChild?>();

    public partial class Resource
    {
        internal List<string> LifecycleEvents { get; } = [];

        internal Exception? BasePrepareFailure { get; set; }

        internal Exception? BaseRollbackFailure { get; set; }

        internal Action? BasePrepareCallback { get; set; }

        partial void PrepareResourceDispose(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            if (!disposing)
                return;

            LifecycleEvents.Add("base-prepare");
            BasePrepareCallback?.Invoke();
            Exception? failure = BasePrepareFailure;
            BasePrepareFailure = null;
            if (failure != null)
                throw failure;
        }

        partial void RollbackResourceDisposePreparation()
        {
            LifecycleEvents.Add("base-rollback");
            Exception? failure = BaseRollbackFailure;
            BaseRollbackFailure = null;
            if (failure != null)
                throw failure;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                LifecycleEvents.Add("base-cleanup");
        }
    }
}

internal sealed partial class GeneratedLifecycleDerivedObject : GeneratedLifecycleBaseObject
{
    public bool AssignInheritedChildDuringUpdate { get; set; }

    public GeneratedListCleanupChild.Resource? InheritedChildToAssign { get; set; }

    public new partial class Resource
    {
        internal Exception? DerivedCleanupFailure { get; set; }

        partial void PostUpdate(GeneratedLifecycleDerivedObject obj, CompositionContext context)
        {
            if (obj.AssignInheritedChildDuringUpdate)
                Child = obj.InheritedChildToAssign;
        }

        partial void PrepareResourceDispose(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            if (disposing)
                LifecycleEvents.Add("derived-prepare");
        }

        partial void RollbackResourceDisposePreparation()
        {
            LifecycleEvents.Add("derived-rollback");
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            LifecycleEvents.Add("derived-cleanup");
            if (DerivedCleanupFailure != null)
                throw DerivedCleanupFailure;
        }
    }
}

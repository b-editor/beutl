using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class EditorTabItemLifetimeTests
{
    [Test]
    public async Task EditorContextOwnershipLeaseIsExclusiveAndGenerationSafe()
    {
        var closeService = new UnownedCloseService();
        var context = new BlockingEditorContext(blockDispose: false, closeService: closeService);

        Assert.That(
            new EditorContextHostToken().TryAcquireContext(
                context,
                out EditorContextOwnershipLease? wrongHostLease),
            Is.False);
        Assert.That(wrongHostLease, Is.Null);
        Assert.That(
            closeService.HostToken.TryAcquireContext(
                context,
                out EditorContextOwnershipLease? first),
            Is.True);
        Assert.That(
            closeService.HostToken.TryAcquireContext(
                context,
                out EditorContextOwnershipLease? competing),
            Is.False);
        Assert.That(competing, Is.Null);

        first!.Dispose();
        Assert.That(
            closeService.HostToken.TryAcquireContext(
                context,
                out EditorContextOwnershipLease? successor),
            Is.True);
        first.Dispose();
        Assert.That(
            closeService.HostToken.TryAcquireContext(
                context,
                out competing),
            Is.False);

        successor!.Dispose();
        await context.DisposeAsync();
    }

    [Test]
    public async Task AddAndSelectIsLinearizedWithConcurrentDispose()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        var inserted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdownRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int disposeRequested = 0;
        context.OnDispose = () =>
        {
            if (Interlocked.Exchange(ref disposeRequested, 1) == 0)
                service.RequestContextShutdown(context);
            shutdownRequested.TrySetResult();
            return ValueTask.CompletedTask;
        };

        Task<bool> publish = Task.Run(() => service.TryAddAndSelectTabItem(tab, () =>
        {
            inserted.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        }));
        await inserted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Context shutdown waits for the publication gate; once publication is released it must
        // remove the tab and clear selection before completing.
        Task dispose = Task.Run(async () =>
        {
            await context.DisposeAsync();
        });
        await shutdownRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();

        Assert.That(await publish.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
            Assert.That(tab.Context.Value, Is.Null);
        });
    }

    [Test]
    public async Task RemovingAnAlreadyAbsentTabClearsStaleSelection()
    {
        var service = new EditorService(new ExtensionProvider());
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false, closeService: service));
        service.AddTabItem(tab);
        service.SelectedTabItem.Value = tab;
        await service.ClearTabItemsAsync();

        Assert.That(service.SelectedTabItem.Value, Is.Null);
        Assert.That(await service.RemoveTabItemAsync(tab), Is.False);
        Assert.That(service.SelectedTabItem.Value, Is.Null);
        await tab.DisposeAsync();
    }

    [Test]
    public async Task SameTabCannotBeAttachedTwice()
    {
        var service = new EditorService(new ExtensionProvider());
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false, closeService: service));

        Assert.Multiple(() =>
        {
            Assert.That(service.TryAddTabItem(tab), Is.True);
            Assert.That(service.TryAddTabItem(tab), Is.False);
            Assert.That(service.TabItems.Count, Is.EqualTo(1));
        });

        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task CloseRequestReportsOwnershipAndSharesTerminalCompletion()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);

        EditorContextCloseRequest accepted = service.RequestClose(context);
        EditorContextCloseRequest repeated = service.RequestClose(context);
        var unknown = new BlockingEditorContext(blockDispose: false, closeService: service);
        EditorContextCloseRequest notOwned = service.RequestClose(unknown);

        Assert.Multiple(() =>
        {
            Assert.That(accepted.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(repeated.Status, Is.EqualTo(EditorContextCloseRequestStatus.AlreadyClosing));
            Assert.That(repeated.Completion, Is.SameAs(accepted.Completion));
            Assert.That(accepted.Completion.IsCompleted, Is.False);
            Assert.That(notOwned.Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
            Assert.That(notOwned.Completion.IsCompletedSuccessfully, Is.True);
        });

        context.ReleaseDispose();
        await accepted.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(service.TabItems, Is.Empty);
    }

    [Test]
    public void CloseRequestCompletionPropagatesTerminalFailure()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new ThrowingEditorContext(service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);

        EditorContextCloseRequest request = service.RequestClose(context);

        Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await request.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposingHostOwnedTabUsesAuthoritativeHostClose()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);
        service.SelectedTabItem.Value = tab;

        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(service.RequestClose(context).Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
        });
    }

    [Test]
    public async Task ContextIdentityCannotBeOwnedByTwoTabs()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var first = new EditorTabItem(context);
        var second = new EditorTabItem(context);

        Assert.That(service.TryAddTabItem(first), Is.True);
        Assert.That(service.TryAddTabItem(second), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems.Count, Is.EqualTo(1));
            Assert.That(context.DisposeCount, Is.Zero);
        });

        await service.CloseTabItem(first).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InitialClaimIsNotVisibleBeforeItemAcceptsOwnership()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        var claimAttached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishClaim = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.BeforeInitialContextClaimPublish = () =>
        {
            claimAttached.TrySetResult();
            publishClaim.Task.GetAwaiter().GetResult();
        };

        Task<bool> add = Task.Run(() => service.TryAddTabItem(tab));
        await claimAttached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<EditorContextCloseRequest> close = Task.Run(() => service.RequestClose(context));
        Assert.That(close.Wait(TimeSpan.FromMilliseconds(100)), Is.False);
        publishClaim.TrySetResult();

        _ = await add.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextCloseRequest request = await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
        await request.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(service.RequestClose(context).Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
    }

    [Test]
    public async Task SameTabCanBeClaimedByOnlyOneEditorService()
    {
        var firstService = new EditorService(new ExtensionProvider());
        var secondService = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: firstService);
        var tab = new EditorTabItem(context);

        Task<bool> first = Task.Run(() => firstService.TryAddTabItem(tab));
        Task<bool> second = Task.Run(() => secondService.TryAddTabItem(tab));
        bool[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(results.Count(static value => value), Is.EqualTo(1));
        EditorService owner = results[0] ? firstService : secondService;
        EditorService loser = results[0] ? secondService : firstService;
        Assert.Multiple(() =>
        {
            Assert.That(owner.TabItems, Does.Contain(tab));
            Assert.That(loser.TabItems, Is.Empty);
            Assert.That(loser.RequestClose(context).Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
        });
        await owner.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ForeignHostContextIsRejectedByInitialAttachment()
    {
        var owner = new EditorService(new ExtensionProvider());
        var foreign = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: foreign);
        var tab = new EditorTabItem(context);

        Assert.That(owner.TryAddTabItem(tab), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(owner.TabItems, Is.Empty);
            Assert.That(tab.IsHostOwned, Is.False);
            Assert.That(context.DisposeCount, Is.Zero);
        });

        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ContextAlreadyOwnedByForeignHostIsRejectedWithoutDisturbingOwner()
    {
        var owner = new EditorService(new ExtensionProvider());
        var foreign = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: foreign);
        var tab = new EditorTabItem(context);
        foreign.AddTabItem(tab);

        Assert.That(owner.TryAddTabItem(tab), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(foreign.TabItems, Does.Contain(tab));
            Assert.That(owner.TabItems, Is.Empty);
            Assert.That(context.DisposeCount, Is.Zero);
            Assert.That(owner.RequestClose(context).Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
        });

        await foreign.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task AddTabItemRejectsForeignOwnedTabWithoutDisposingIt()
    {
        var owner = new EditorService(new ExtensionProvider());
        var foreign = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: foreign);
        var tab = new EditorTabItem(context);
        foreign.AddTabItem(tab);
        int addNotifications = 0;
        ((INotifyCollectionChanged)owner.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
                addNotifications++;
        };

        owner.AddTabItem(tab);

        Assert.Multiple(() =>
        {
            Assert.That(foreign.TabItems, Does.Contain(tab));
            Assert.That(owner.TabItems, Is.Empty);
            Assert.That(addNotifications, Is.Zero);
            Assert.That(context.DisposeCount, Is.Zero);
            Assert.That(owner.RequestClose(context).Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
        });

        await foreign.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ForeignHostCannotCloseOwnerTabViaPublicOverload()
    {
        var owner = new EditorService(new ExtensionProvider());
        var foreign = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: owner);
        var tab = new EditorTabItem(context);
        owner.AddTabItem(tab);

        await foreign.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(owner.TabItems, Does.Contain(tab));
            Assert.That(tab.Context.Value, Is.SameAs(context));
            Assert.That(context.DisposeCount, Is.Zero);
            Assert.That(foreign.RequestClose(context).Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
        });

        EditorContextCloseRequest ownerClose = owner.RequestClose(context);
        Assert.That(ownerClose.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
        await ownerClose.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
        Assert.That(owner.TabItems, Is.Empty);
    }

    [Test]
    public async Task ForeignHostContextIsRejectedByReplacementWithoutConsumption()
    {
        var owner = new EditorService(new ExtensionProvider());
        var foreign = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: owner);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: foreign);
        var tab = new EditorTabItem(current);
        owner.AddTabItem(tab);

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.False);
            Assert.That(tab.Context.Value, Is.SameAs(current));
            Assert.That(current.DisposeCount, Is.Zero);
            Assert.That(replacement.DisposeCount, Is.Zero);
        });

        await owner.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ActivationDisposesContextWhenExtensionReturnsForeignHostToken()
    {
        var foreign = new EditorService(new ExtensionProvider());
        var provider = new ExtensionProvider();
        var extension = new ForeignContextEditorExtension(foreign, blockDispose: true);
        provider.AddExtensions(1, [extension]);
        var service = new EditorService(provider);
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-foreign-token.activation"))
        };

        service.ActivateTabItem(scene);

        BlockingEditorContext context = await extension.CreatedContext.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        await context.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(
            foreign.HostToken.TryAcquireContext(
                context,
                out EditorContextOwnershipLease? competing),
            Is.False);
        Assert.That(competing, Is.Null);
        context.ReleaseDispose();
        await context.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(
            foreign.HostToken.TryAcquireContext(
                context,
                out EditorContextOwnershipLease? successor),
            Is.True);
        successor!.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(foreign.TabItems, Is.Empty);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DeferredActivationCleanupPreservesImmediateFailureForReconciliation()
    {
        var foreign = new EditorService(new ExtensionProvider());
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var context = new ImmediateFaultEditorContext(
            new Scene(16, 16, string.Empty),
            foreign);
        provider.AddExtensions(1, [new SuppliedContextEditorExtension(context)]);
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-immediate-fault.activation"))
        };

        service.ActivateTabItem(scene);
        Assert.That(context.DisposeStarted.Task.IsCompleted, Is.True);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ClearTabItemsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        await service.ClearTabItemsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DeferredActivationCleanupDrainsDelayedFailureOnce()
    {
        var foreign = new EditorService(new ExtensionProvider());
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var context = new DelayedFaultEditorContext(
            new Scene(16, 16, string.Empty),
            foreign);
        provider.AddExtensions(1, [new SuppliedContextEditorExtension(context)]);
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-delayed-fault.activation"))
        };

        service.ActivateTabItem(scene);
        await context.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task drain = service.ClearTabItemsAsync().AsTask();
        Assert.That(drain.IsCompleted, Is.False);

        context.Release.TrySetResult();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await drain.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
        await service.ClearTabItemsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DeferredActivationCleanupSuccessIsConsumedByReconciliation()
    {
        var foreign = new EditorService(new ExtensionProvider());
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var context = new BlockingEditorContext(
            blockDispose: false,
            new Scene(16, 16, string.Empty),
            foreign);
        provider.AddExtensions(1, [new SuppliedContextEditorExtension(context)]);
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-success.activation"))
        };

        service.ActivateTabItem(scene);
        await service.ClearTabItemsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await service.ClearTabItemsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DeferredActivationCleanupPostAwaitCallbackRejectsReconciliation()
    {
        var foreign = new EditorService(new ExtensionProvider());
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var context = new ReentrantDisposeEditorContext(
            new Scene(16, 16, string.Empty),
            foreign);
        context.AfterAwait = () =>
        {
            Assert.Throws<InvalidOperationException>(() =>
                service.ClearTabItemsAsync().AsTask().GetAwaiter().GetResult());
            return ValueTask.CompletedTask;
        };
        provider.AddExtensions(1, [new SuppliedContextEditorExtension(context)]);
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-post-await.activation"))
        };

        service.ActivateTabItem(scene);
        await context.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.ClearTabItemsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task FinalReconciliationDrainJoinsSiblingAddedAfterInitialClaim()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var foreign = new EditorService(new ExtensionProvider());
        (Scene parentScene, Scene siblingScene) = CreateSiblingDrainScenes("joined");
        var sibling = new BlockingEditorContext(obj: siblingScene, closeService: service);
        var siblingRequested = new TaskCompletionSource<EditorContextCloseRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ClaimAwareEditorContext(service, parentScene, foreign)
        {
            AfterClaim = () =>
            {
                siblingRequested.TrySetResult(service.RequestClose(sibling));
                return ValueTask.CompletedTask;
            }
        };
        provider.AddExtensions(
            1,
            [
                new MatchedContextEditorExtension(".parent-drain", context),
                new MatchedContextEditorExtension(".sibling-drain", sibling)
            ]);

        Task reconciliation = service.ReconcileTabItemsAsync([parentScene, siblingScene]).AsTask();
        await context.Claimed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextCloseRequest request = await siblingRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool siblingWasClaimed = false;
        bool completedWhileSiblingBlocked = false;
        Exception? reconciliationFailure;
        try
        {
            await sibling.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            siblingWasClaimed = IsLifecycleTeardownTaken(service, request.Completion);
            completedWhileSiblingBlocked = reconciliation.IsCompleted;
        }
        finally
        {
            sibling.ReleaseDispose();
            reconciliationFailure = await CaptureFailureAsync(reconciliation);
        }

        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(siblingWasClaimed, Is.True);
            Assert.That(completedWhileSiblingBlocked, Is.False);
            Assert.That(reconciliationFailure, Is.Null);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(sibling.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
    }

    [Test]
    public async Task FinalReconciliationDrainReportsLateSiblingFailureOnce()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var foreign = new EditorService(new ExtensionProvider());
        (Scene parentScene, Scene siblingScene) = CreateSiblingDrainScenes("failure");
        var sibling = new ThrowingEditorContext(service, siblingScene);
        var siblingRequested = new TaskCompletionSource<EditorContextCloseRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ClaimAwareEditorContext(service, parentScene, foreign)
        {
            AfterClaim = () =>
            {
                siblingRequested.TrySetResult(service.RequestClose(sibling));
                return ValueTask.CompletedTask;
            }
        };
        provider.AddExtensions(
            1,
            [
                new MatchedContextEditorExtension(".parent-drain", context),
                new MatchedContextEditorExtension(".sibling-drain", sibling)
            ]);

        Exception? firstFailure = await CaptureFailureAsync(
            service.ReconcileTabItemsAsync([parentScene, siblingScene]).AsTask());
        EditorContextCloseRequest request = await siblingRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Exception? secondFailure = await CaptureFailureAsync(service.ClearTabItemsAsync().AsTask());

        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(firstFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(firstFailure?.Message, Is.EqualTo("dispose failed"));
            Assert.That(secondFailure, Is.Null);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(sibling.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FinalReconciliationDrainAdoptsAlreadyClosingSibling()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var foreign = new EditorService(new ExtensionProvider());
        (Scene parentScene, Scene siblingScene) = CreateSiblingDrainScenes("already-closing");
        var sibling = new BlockingEditorContext(obj: siblingScene, closeService: service);
        var allowCausalRequest = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var causalRequestCreated = new TaskCompletionSource<EditorContextCloseRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ClaimAwareEditorContext(service, parentScene, foreign)
        {
            AfterClaim = async () =>
            {
                await allowCausalRequest.Task.ConfigureAwait(false);
                causalRequestCreated.TrySetResult(service.RequestClose(sibling));
            }
        };
        provider.AddExtensions(
            1,
            [
                new MatchedContextEditorExtension(".parent-drain", context),
                new MatchedContextEditorExtension(".sibling-drain", sibling)
            ]);

        Task reconciliation = service.ReconcileTabItemsAsync([parentScene, siblingScene]).AsTask();
        await context.Claimed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextCloseRequest firstRequest = service.RequestClose(sibling);
        EditorContextCloseRequest causalRequest = default;
        bool takenBeforeAdoption = false;
        bool takenAfterAdoption = false;
        bool completedWhileSiblingBlocked = false;
        Exception? reconciliationFailure;
        try
        {
            await sibling.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            takenBeforeAdoption = IsLifecycleTeardownTaken(service, firstRequest.Completion);

            allowCausalRequest.TrySetResult();
            causalRequest = await causalRequestCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            takenAfterAdoption = IsLifecycleTeardownTaken(service, firstRequest.Completion);
            completedWhileSiblingBlocked = reconciliation.IsCompleted;
        }
        finally
        {
            allowCausalRequest.TrySetResult();
            sibling.ReleaseDispose();
            reconciliationFailure = await CaptureFailureAsync(reconciliation);
        }

        Assert.Multiple(() =>
        {
            Assert.That(firstRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(causalRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.AlreadyClosing));
            Assert.That(causalRequest.Completion, Is.SameAs(firstRequest.Completion));
            Assert.That(takenBeforeAdoption, Is.False);
            Assert.That(takenAfterAdoption, Is.True);
            Assert.That(completedWhileSiblingBlocked, Is.False);
            Assert.That(reconciliationFailure, Is.Null);
            Assert.That(sibling.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FinalReconciliationDrainDoesNotJoinUnrelatedLateClose()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var foreign = new EditorService(new ExtensionProvider());
        (Scene parentScene, Scene siblingScene) = CreateSiblingDrainScenes("unrelated");
        var unrelated = new BlockingEditorContext(obj: siblingScene, closeService: service);
        var allowParentCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ClaimAwareEditorContext(service, parentScene, foreign)
        {
            AfterClaim = async () => await allowParentCompletion.Task.ConfigureAwait(false)
        };
        provider.AddExtensions(
            1,
            [
                new MatchedContextEditorExtension(".parent-drain", context),
                new MatchedContextEditorExtension(".sibling-drain", unrelated)
            ]);

        Task reconciliation = service.ReconcileTabItemsAsync([parentScene, siblingScene]).AsTask();
        await context.Claimed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextCloseRequest unrelatedRequest = service.RequestClose(unrelated);
        bool unrelatedWasClaimed = false;
        Exception? reconciliationFailure = null;
        try
        {
            await unrelated.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            unrelatedWasClaimed = IsLifecycleTeardownTaken(service, unrelatedRequest.Completion);
            allowParentCompletion.TrySetResult();
            await reconciliation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            reconciliationFailure = ex;
        }
        finally
        {
            allowParentCompletion.TrySetResult();
            unrelated.ReleaseDispose();
            await unrelatedRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            if (!reconciliation.IsCompleted)
            {
                try
                {
                    await reconciliation.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch
                {
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(unrelatedRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(unrelatedWasClaimed, Is.False);
            Assert.That(reconciliationFailure, Is.Null);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(unrelated.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FinalReconciliationDrainDefersUnrelatedLateFailure()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var foreign = new EditorService(new ExtensionProvider());
        (Scene parentScene, Scene siblingScene) = CreateSiblingDrainScenes("unrelated-failure");
        var unrelated = new ThrowingEditorContext(service, siblingScene);
        var allowParentCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ClaimAwareEditorContext(service, parentScene, foreign)
        {
            AfterClaim = async () => await allowParentCompletion.Task.ConfigureAwait(false)
        };
        provider.AddExtensions(
            1,
            [
                new MatchedContextEditorExtension(".parent-drain", context),
                new MatchedContextEditorExtension(".sibling-drain", unrelated)
            ]);

        Task reconciliation = service.ReconcileTabItemsAsync([parentScene, siblingScene]).AsTask();
        await context.Claimed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextCloseRequest unrelatedRequest = service.RequestClose(unrelated);
        Exception? directFailure = await CaptureFailureAsync(unrelatedRequest.Completion);

        allowParentCompletion.TrySetResult();
        Exception? reconciliationFailure = await CaptureFailureAsync(reconciliation);
        Exception? nextDrainFailure = await CaptureFailureAsync(service.ClearTabItemsAsync().AsTask());
        Exception? finalDrainFailure = await CaptureFailureAsync(service.ClearTabItemsAsync().AsTask());

        Assert.Multiple(() =>
        {
            Assert.That(unrelatedRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(directFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(reconciliationFailure, Is.Null);
            Assert.That(nextDrainFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(nextDrainFailure?.Message, Is.EqualTo("dispose failed"));
            Assert.That(finalDrainFailure, Is.Null);
            Assert.That(unrelated.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FinalReconciliationDrainAdoptsPreexistingSiblingDescendants()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var foreign = new EditorService(new ExtensionProvider());
        (Scene parentScene, Scene firstScene) = CreateSiblingDrainScenes("transitive");
        var secondScene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "final-drain-transitive.descendant-drain"))
        };
        var second = new BlockingEditorContext(obj: secondScene, closeService: service);
        var secondRequestCreated = new TaskCompletionSource<EditorContextCloseRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new BlockingEditorContext(obj: firstScene, closeService: service)
        {
            OnDispose = () =>
            {
                secondRequestCreated.TrySetResult(service.RequestClose(second));
                return ValueTask.CompletedTask;
            }
        };
        var allowCausalRequest = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var causalRequestCreated = new TaskCompletionSource<EditorContextCloseRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ClaimAwareEditorContext(service, parentScene, foreign)
        {
            AfterClaim = async () =>
            {
                await allowCausalRequest.Task.ConfigureAwait(false);
                causalRequestCreated.TrySetResult(service.RequestClose(first));
            }
        };
        provider.AddExtensions(
            1,
            [
                new MatchedContextEditorExtension(".parent-drain", context),
                new MatchedContextEditorExtension(".sibling-drain", first),
                new MatchedContextEditorExtension(".descendant-drain", second)
            ]);

        Task reconciliation = service.ReconcileTabItemsAsync(
            [parentScene, firstScene, secondScene]).AsTask();
        await context.Claimed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextCloseRequest firstRequest = service.RequestClose(first);
        EditorContextCloseRequest secondRequest = default;
        EditorContextCloseRequest causalRequest = default;
        bool firstTakenBeforeAdoption = false;
        bool secondTakenBeforeAdoption = false;
        bool firstTakenAfterAdoption = false;
        bool secondTakenAfterAdoption = false;
        bool completedWhileDescendantsBlocked = false;
        Exception? reconciliationFailure;
        try
        {
            secondRequest = await secondRequestCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await second.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            firstTakenBeforeAdoption = IsLifecycleTeardownTaken(service, firstRequest.Completion);
            secondTakenBeforeAdoption = IsLifecycleTeardownTaken(service, secondRequest.Completion);

            allowCausalRequest.TrySetResult();
            causalRequest = await causalRequestCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            firstTakenAfterAdoption = IsLifecycleTeardownTaken(service, firstRequest.Completion);
            secondTakenAfterAdoption = IsLifecycleTeardownTaken(service, secondRequest.Completion);
            completedWhileDescendantsBlocked = reconciliation.IsCompleted;
        }
        finally
        {
            allowCausalRequest.TrySetResult();
            first.ReleaseDispose();
            second.ReleaseDispose();
            reconciliationFailure = await CaptureFailureAsync(reconciliation);
        }

        Assert.Multiple(() =>
        {
            Assert.That(firstRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(secondRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(causalRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.AlreadyClosing));
            Assert.That(firstTakenBeforeAdoption, Is.False);
            Assert.That(secondTakenBeforeAdoption, Is.False);
            Assert.That(firstTakenAfterAdoption, Is.True);
            Assert.That(secondTakenAfterAdoption, Is.True);
            Assert.That(completedWhileDescendantsBlocked, Is.False);
            Assert.That(reconciliationFailure, Is.Null);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FinalReconciliationDrainAdoptsPreexistingAlreadyClosingDependency()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var foreign = new EditorService(new ExtensionProvider());
        (Scene parentScene, Scene firstScene) = CreateSiblingDrainScenes("dependency");
        var secondScene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "final-drain-dependency.descendant-drain"))
        };
        var second = new BlockingEditorContext(obj: secondScene, closeService: service);
        var dependencyRequestCreated = new TaskCompletionSource<EditorContextCloseRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new BlockingEditorContext(obj: firstScene, closeService: service)
        {
            OnDispose = () =>
            {
                dependencyRequestCreated.TrySetResult(service.RequestClose(second));
                return ValueTask.CompletedTask;
            }
        };
        var allowCausalRequest = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var causalRequestCreated = new TaskCompletionSource<EditorContextCloseRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ClaimAwareEditorContext(service, parentScene, foreign)
        {
            AfterClaim = async () =>
            {
                await allowCausalRequest.Task.ConfigureAwait(false);
                causalRequestCreated.TrySetResult(service.RequestClose(first));
            }
        };
        provider.AddExtensions(
            1,
            [
                new MatchedContextEditorExtension(".parent-drain", context),
                new MatchedContextEditorExtension(".sibling-drain", first),
                new MatchedContextEditorExtension(".descendant-drain", second)
            ]);

        Task reconciliation = service.ReconcileTabItemsAsync(
            [parentScene, firstScene, secondScene]).AsTask();
        await context.Claimed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextCloseRequest secondRequest = service.RequestClose(second);
        EditorContextCloseRequest firstRequest = default;
        EditorContextCloseRequest dependencyRequest = default;
        EditorContextCloseRequest causalRequest = default;
        bool firstTakenBeforeAdoption = false;
        bool secondTakenBeforeAdoption = false;
        bool firstTakenAfterAdoption = false;
        bool secondTakenAfterAdoption = false;
        bool completedWhileDependenciesBlocked = false;
        Exception? reconciliationFailure;
        try
        {
            await second.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            firstRequest = service.RequestClose(first);
            dependencyRequest = await dependencyRequestCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            firstTakenBeforeAdoption = IsLifecycleTeardownTaken(service, firstRequest.Completion);
            secondTakenBeforeAdoption = IsLifecycleTeardownTaken(service, secondRequest.Completion);

            allowCausalRequest.TrySetResult();
            causalRequest = await causalRequestCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            firstTakenAfterAdoption = IsLifecycleTeardownTaken(service, firstRequest.Completion);
            secondTakenAfterAdoption = IsLifecycleTeardownTaken(service, secondRequest.Completion);
            completedWhileDependenciesBlocked = reconciliation.IsCompleted;
        }
        finally
        {
            allowCausalRequest.TrySetResult();
            first.ReleaseDispose();
            second.ReleaseDispose();
            reconciliationFailure = await CaptureFailureAsync(reconciliation);
        }

        Assert.Multiple(() =>
        {
            Assert.That(secondRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(firstRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(dependencyRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.AlreadyClosing));
            Assert.That(causalRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.AlreadyClosing));
            Assert.That(firstTakenBeforeAdoption, Is.False);
            Assert.That(secondTakenBeforeAdoption, Is.False);
            Assert.That(firstTakenAfterAdoption, Is.True);
            Assert.That(secondTakenAfterAdoption, Is.True);
            Assert.That(completedWhileDependenciesBlocked, Is.False);
            Assert.That(reconciliationFailure, Is.Null);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FinalReconciliationDrainAdoptsLateRequestFromCompletedRootOperation()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var foreign = new EditorService(new ExtensionProvider());
        (Scene parentScene, Scene targetScene) = CreateSiblingDrainScenes("completed-root");
        var keeperScene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "final-drain-completed-root.keeper-drain"))
        };
        var target = new BlockingEditorContext(obj: targetScene, closeService: service);
        var keeper = new BlockingEditorContext(obj: keeperScene, closeService: service);
        var allowParentCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var keeperRequestCreated = new TaskCompletionSource<EditorContextCloseRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lateRequestCreated = new TaskCompletionSource<Task<EditorContextCloseRequest>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ClaimAwareEditorContext? context = null;
        context = new ClaimAwareEditorContext(service, parentScene, foreign)
        {
            AfterClaim = async () =>
            {
                await allowParentCompletion.Task.ConfigureAwait(false);
                keeperRequestCreated.TrySetResult(service.RequestClose(keeper));
                object entry = context!.Entry!;
                lateRequestCreated.TrySetResult(Task.Run(() =>
                {
                    bool unmapped = SpinWait.SpinUntil(
                        () => !IsLifecycleTeardownOperationMapped(service, entry),
                        TimeSpan.FromSeconds(5));
                    if (!unmapped)
                        throw new TimeoutException("The completed root operation remained mapped.");
                    return service.RequestClose(target);
                }));
            }
        };
        provider.AddExtensions(
            1,
            [
                new MatchedContextEditorExtension(".parent-drain", context),
                new MatchedContextEditorExtension(".sibling-drain", target),
                new MatchedContextEditorExtension(".keeper-drain", keeper)
            ]);

        Task reconciliation = service.ReconcileTabItemsAsync(
            [parentScene, targetScene, keeperScene]).AsTask();
        await context.Claimed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextCloseRequest targetRequest = service.RequestClose(target);
        EditorContextCloseRequest keeperRequest = default;
        EditorContextCloseRequest lateRequest = default;
        bool targetTakenAfterLateRequest = false;
        bool completedWhileTargetBlocked = false;
        Exception? reconciliationFailure;
        try
        {
            await target.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            allowParentCompletion.TrySetResult();
            keeperRequest = await keeperRequestCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await keeper.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<EditorContextCloseRequest> lateRequestTask = await lateRequestCreated.Task
                .WaitAsync(TimeSpan.FromSeconds(5));
            lateRequest = await lateRequestTask.WaitAsync(TimeSpan.FromSeconds(5));
            targetTakenAfterLateRequest = IsLifecycleTeardownTaken(service, targetRequest.Completion);
            completedWhileTargetBlocked = reconciliation.IsCompleted;
        }
        finally
        {
            allowParentCompletion.TrySetResult();
            target.ReleaseDispose();
            keeper.ReleaseDispose();
            reconciliationFailure = await CaptureFailureAsync(reconciliation);
        }

        Assert.Multiple(() =>
        {
            Assert.That(targetRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(keeperRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(lateRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.AlreadyClosing));
            Assert.That(targetTakenAfterLateRequest, Is.True);
            Assert.That(completedWhileTargetBlocked, Is.False);
            Assert.That(reconciliationFailure, Is.Null);
            Assert.That(target.DisposeCount, Is.EqualTo(1));
            Assert.That(keeper.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ReconciliationReportsHostCloseFailureOnce()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new ThrowingEditorContext(service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);

        Exception? failure = null;
        try
        {
            await service.ClearTabItemsAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(failure!.Message, Is.EqualTo("dispose failed"));
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
        await service.ClearTabItemsAsync();
    }

    [Test]
    public async Task FailedActivationDoesNotDuplicateHostOwnedTeardownFailure()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var context = new ThrowingEditorContext(service);
        provider.AddExtensions(1, [new SuppliedContextEditorExtension(context)]);
        service.BeforePhysicalAdd = () => throw new InvalidOperationException("publication failed");
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-host-fault.activation"))
        };

        Assert.Throws<InvalidOperationException>(() => service.ActivateTabItem(scene));
        await context.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Exception? failure = null;
        try
        {
            await service.ClearTabItemsAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(failure!.Message, Is.EqualTo("dispose failed"));
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
        service.BeforePhysicalAdd = null;
        await service.ClearTabItemsAsync();
    }

    [Test]
    public async Task ActivationDisposesTransferredContextWhenTabConstructionFails()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var extension = new TransferredContextEditorExtension(service, blockDispose: true);
        provider.AddExtensions(2, [extension]);
        service.BeforeActivationTabConstruction =
            _ => throw new InvalidOperationException("Tab creation failed.");
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-throw.activation-fault"))
        };

        Assert.Throws<InvalidOperationException>(() => service.ActivateTabItem(scene));

        BlockingEditorContext context = await extension.CreatedContext.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        await context.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(
            service.HostToken.TryAcquireContext(
                context,
                out EditorContextOwnershipLease? competing),
            Is.False);
        Assert.That(competing, Is.Null);
        context.ReleaseDispose();
        await context.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(
            service.HostToken.TryAcquireContext(
                context,
                out EditorContextOwnershipLease? successor),
            Is.True);
        successor!.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ActivationDisposesTransferredContextWhenOwnershipInspectionThrows()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var extension = new ThrowingActivationContextEditorExtension();
        provider.AddExtensions(3, [extension]);
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-host-token.activation-fault"))
        };

        Assert.Throws<InvalidOperationException>(() => service.ActivateTabItem(scene));

        BlockingEditorContext context = await extension.CreatedContext.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        await context.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ActivationFactoryCannotSynchronouslyReconcileItsOwnAdmission()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var extension = new ReentrantReconcileReplacementExtension(
            service,
            useWorker: false,
            matchFileExtension: true);
        provider.AddExtensions(4, [extension]);
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-reentrant.operation"))
        };

        Task activation = Task.Run(() => service.ActivateTabItem(scene));
        InvalidOperationException? failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await activation.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("reconciliation"));
            Assert.That(extension.CreationCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
        await service.ClearTabItemsAsync();
    }

    [Test]
    public async Task ActivationDoesNotDisposeAlreadyOwnedSameHostContext()
    {
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var ownedScene = new Scene(16, 16, string.Empty);
        var ownedContext = new BlockingEditorContext(
            blockDispose: false,
            ownedScene,
            new ForwardingCloseService(service));
        var ownedTab = new EditorTabItem(ownedContext);
        service.AddTabItem(ownedTab);
        var extension = new ActivationExistingContextEditorExtension(ownedContext);
        provider.AddExtensions(1, [extension]);
        var activationScene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-same-owned.activation"))
        };

        service.ActivateTabItem(activationScene);

        Assert.Multiple(() =>
        {
            Assert.That(extension.CreationCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Does.Contain(ownedTab));
            Assert.That(service.TabItems.Count, Is.EqualTo(1));
            Assert.That(ownedTab.Context.Value, Is.SameAs(ownedContext));
            Assert.That(ownedContext.DisposeCount, Is.Zero);
        });

        await service.CloseTabItem(ownedTab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ActivationDoesNotDisposeAlreadyOwnedForeignForwardedContext()
    {
        var foreign = new EditorService(new ExtensionProvider());
        var provider = new ExtensionProvider();
        var service = new EditorService(provider);
        var ownedScene = new Scene(16, 16, string.Empty);
        var ownedContext = new BlockingEditorContext(
            blockDispose: false,
            ownedScene,
            new ForwardingCloseService(foreign));
        var ownedTab = new EditorTabItem(ownedContext);
        foreign.AddTabItem(ownedTab);
        var extension = new ActivationExistingContextEditorExtension(ownedContext);
        provider.AddExtensions(1, [extension]);
        var activationScene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "activation-foreign-owned.activation"))
        };

        service.ActivateTabItem(activationScene);

        Assert.Multiple(() =>
        {
            Assert.That(extension.CreationCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(foreign.TabItems, Does.Contain(ownedTab));
            Assert.That(ownedTab.Context.Value, Is.SameAs(ownedContext));
            Assert.That(ownedContext.DisposeCount, Is.Zero);
        });

        await foreign.CloseTabItem(ownedTab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ReplacementWithContextOwnedByForeignHostIsRejectedWithoutDisturbingEitherTab()
    {
        var firstHost = new EditorService(new ExtensionProvider());
        var secondHost = new EditorService(new ExtensionProvider());
        var foreignContext = new BlockingEditorContext(blockDispose: false, closeService: firstHost);
        var foreignTab = new EditorTabItem(foreignContext);
        firstHost.AddTabItem(foreignTab);

        var currentContext = new BlockingEditorContext(blockDispose: false, closeService: secondHost);
        var targetTab = new EditorTabItem(currentContext);
        secondHost.AddTabItem(targetTab);

        EditorContextReplacementResult replaced = await targetTab.ReplaceContextAsync(foreignContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.False);
            Assert.That(foreignTab.Context.Value, Is.SameAs(foreignContext));
            Assert.That(targetTab.Context.Value, Is.SameAs(currentContext));
            Assert.That(foreignContext.DisposeCount, Is.Zero);
            Assert.That(currentContext.DisposeCount, Is.Zero);
            Assert.That(secondHost.RequestClose(foreignContext).Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
        });

        EditorContextCloseRequest foreignClose = foreignContext.CloseService.RequestClose(foreignContext);
        Assert.That(foreignClose.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
        await foreignClose.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await secondHost.CloseTabItem(targetTab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task UnownedTabCannotReplaceContextOwnedByAnotherHost()
    {
        var owner = new EditorService(new ExtensionProvider());
        var foreign = new EditorService(new ExtensionProvider());
        var ownerContext = new BlockingEditorContext(blockDispose: false, closeService: owner);
        var ownerTab = new EditorTabItem(ownerContext);
        owner.AddTabItem(ownerTab);

        var unownedCurrent = new BlockingEditorContext(blockDispose: false, closeService: foreign);
        var unownedTab = new EditorTabItem(unownedCurrent);

        EditorContextReplacementResult result = await unownedTab.ReplaceContextAsync(ownerContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(EditorContextReplacementStatus.NotOwned));
            Assert.That(result.InputConsumed, Is.False);
            Assert.That(ownerTab.Context.Value, Is.SameAs(ownerContext));
            Assert.That(unownedTab.Context.Value, Is.SameAs(unownedCurrent));
            Assert.That(ownerContext.DisposeCount, Is.Zero);
            Assert.That(unownedCurrent.DisposeCount, Is.Zero);
        });

        await owner.CloseTabItem(ownerTab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await unownedTab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task InitialAddAndReplacementInterleavingCannotPublishUnownedReplacement()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        using var attachEntered = new ManualResetEventSlim();
        using var releaseAttach = new ManualResetEventSlim();
        service.BeforeInitialContextClaimPublish = () =>
        {
            attachEntered.Set();
            releaseAttach.Wait(TimeSpan.FromSeconds(5));
        };

        Task<bool> add = Task.Run(() => service.TryAddTabItem(tab));
        Assert.That(attachEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        EditorContextReplacementResult replacementResult = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        releaseAttach.Set();

        Assert.That(await add.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(replacementResult.Status, Is.EqualTo(EditorContextReplacementStatus.NotOwned));
            Assert.That(replacementResult.InputConsumed, Is.False);
            Assert.That(tab.Context.Value, Is.SameAs(current));
            Assert.That(service.TabItems, Does.Contain(tab));
            Assert.That(replacement.DisposeCount, Is.Zero);
        });

        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await replacement.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task HostMediatedReplacementTransfersFactoryOwnership()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        var extension = new HostMediatedReplacementExtension();

        EditorContextReplacementStatus status = await service.ReplaceContextAsync(tab, extension);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(EditorContextReplacementStatus.Succeeded));
            Assert.That(tab.Context.Value, Is.SameAs(extension.CreatedContext));
            Assert.That(extension.CreatedContext!.DisposeCount, Is.Zero);
            Assert.That(service.TabItems, Does.Contain(tab));
        });
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReplacementFactoryCannotSynchronouslyReconcileItsOwnAdmission(bool useWorker)
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        var extension = new ReentrantReconcileReplacementExtension(service, useWorker);

        InvalidOperationException? failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ReplaceContextAsync(tab, extension).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("reconciliation"));
            Assert.That(extension.CreationCount, Is.EqualTo(1));
            Assert.That(tab.Context.Value, Is.SameAs(current));
            Assert.That(current.DisposeCount, Is.Zero);
        });
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ReplacementFactoryChildCanReconcileAfterAdmissionCompletes()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        var extension = new DeferredReconcileReplacementExtension(service);

        EditorContextReplacementStatus status = await service.ReplaceContextAsync(tab, extension);
        Assert.That(status, Is.EqualTo(EditorContextReplacementStatus.CreationFailed));

        extension.Release.TrySetResult();
        await extension.Reconciliation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(current.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CompletedAdmissionOperationDoesNotRetainEditorServiceInDelayedChild()
    {
        (WeakReference service, Task child, TaskCompletionSource release) =
            await CreateDelayedAdmissionChildAsync();

        for (int i = 0; service.IsAlive && i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.That(service.IsAlive, Is.False);
        release.TrySetResult();
        await child.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Service, Task Child, TaskCompletionSource Release)>
        CreateDelayedAdmissionChildAsync()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);
        var extension = new CapturingAdmissionChildExtension();

        Assert.That(
            await service.ReplaceContextAsync(tab, extension),
            Is.EqualTo(EditorContextReplacementStatus.CreationFailed));
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var serviceReference = new WeakReference(service);
        Task child = extension.Child;
        TaskCompletionSource release = extension.Release;
        service = null!;
        context = null!;
        tab = null!;
        extension = null!;
        return (serviceReference, child, release);
    }

    [Test]
    public async Task NestedHostReplacementCannotHideOuterAdmissionFromReconciliation()
    {
        var outerService = new EditorService(new ExtensionProvider());
        var innerService = new EditorService(new ExtensionProvider());
        var outerContext = new BlockingEditorContext(blockDispose: false, closeService: outerService);
        var innerContext = new BlockingEditorContext(blockDispose: false, closeService: innerService);
        var outerTab = new EditorTabItem(outerContext);
        var innerTab = new EditorTabItem(innerContext);
        outerService.AddTabItem(outerTab);
        innerService.AddTabItem(innerTab);
        var reconcileOuter = new ReentrantReconcileReplacementExtension(
            outerService,
            useWorker: false);
        var nested = new NestedReplacementExtension(innerService, innerTab, reconcileOuter);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await outerService.ReplaceContextAsync(outerTab, nested).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Multiple(() =>
        {
            Assert.That(outerTab.Context.Value, Is.SameAs(outerContext));
            Assert.That(innerTab.Context.Value, Is.SameAs(innerContext));
            Assert.That(outerContext.DisposeCount, Is.Zero);
            Assert.That(innerContext.DisposeCount, Is.Zero);
        });
        await outerService.CloseTabItem(outerTab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await innerService.CloseTabItem(innerTab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task HostMediatedReplacementDoesNotInvokeFactoryAfterAdmissionCloses()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        var blocking = new BlockingReplacementExtension();
        var rejected = new HostMediatedReplacementExtension();

        Task<EditorContextReplacementStatus> first = Task.Run(
            () => service.ReplaceContextAsync(tab, blocking).AsTask());
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var admissionClosed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.AfterTabAdmissionClosed = () => admissionClosed.TrySetResult();
        Task reconcile = service.ClearTabItemsAsync().AsTask();
        await admissionClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        EditorContextReplacementStatus status = await service.ReplaceContextAsync(tab, rejected);
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(EditorContextReplacementStatus.Busy));
            Assert.That(rejected.CreationCount, Is.Zero);
            Assert.That(reconcile.IsCompleted, Is.False);
        });

        blocking.Release.TrySetResult();
        Assert.That(
            await first.WaitAsync(TimeSpan.FromSeconds(5)),
            Is.EqualTo(EditorContextReplacementStatus.Succeeded));
        await reconcile.WaitAsync(TimeSpan.FromSeconds(5));
        service.AfterTabAdmissionClosed = null;
    }

    [Test]
    public async Task ReconciliationWaitsForBlockedReplacementFactoryAndCleansUp()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        var blocking = new BlockingReplacementExtension();
        var admissionClosed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.AfterTabAdmissionClosed = () => admissionClosed.TrySetResult();

        Task<EditorContextReplacementStatus> replacement = Task.Run(
            () => service.ReplaceContextAsync(tab, blocking).AsTask());
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task reconcile = service.ClearTabItemsAsync().AsTask();
        await admissionClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(reconcile.IsCompleted, Is.False);
        blocking.Release.TrySetResult();

        Assert.That(
            await replacement.WaitAsync(TimeSpan.FromSeconds(5)),
            Is.EqualTo(EditorContextReplacementStatus.Succeeded));
        await reconcile.WaitAsync(TimeSpan.FromSeconds(5));
        service.AfterTabAdmissionClosed = null;

        Assert.That(service.TabItems, Is.Empty);
        Assert.That(blocking.CreatedContext!.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ThrowingAdmissionClosedObserverDoesNotStrandAdmissionOrReconciliation()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        service.AfterTabAdmissionClosed = () => throw new InvalidOperationException("admission observer failed");

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ClearTabItemsAsync());
        service.AfterTabAdmissionClosed = null;

        var extension = new HostMediatedReplacementExtension();
        EditorContextReplacementStatus status = await service.ReplaceContextAsync(tab, extension);
        Assert.That(status, Is.EqualTo(EditorContextReplacementStatus.Succeeded));

        await service.ClearTabItemsAsync();
        Assert.That(service.TabItems, Is.Empty);
    }

    [Test]
    public async Task HostMediatedReplacementRejectsForeignTabBeforeCreatingContext()
    {
        var owner = new EditorService(new ExtensionProvider());
        var foreign = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: foreign);
        var tab = new EditorTabItem(current);
        foreign.AddTabItem(tab);
        var extension = new HostMediatedReplacementExtension();

        EditorContextReplacementStatus status = await owner.ReplaceContextAsync(tab, extension);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(EditorContextReplacementStatus.NotOwned));
            Assert.That(extension.CreationCount, Is.Zero);
            Assert.That(tab.Context.Value, Is.SameAs(current));
            Assert.That(current.DisposeCount, Is.Zero);
            Assert.That(foreign.TabItems, Does.Contain(tab));
        });
        await foreign.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task HostMediatedReplacementDoesNotDisposeExistingOwnedFactoryResult()
    {
        var service = new EditorService(new ExtensionProvider());
        var foreign = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(
            blockDispose: false,
            closeService: new ForwardingCloseService(service));
        var siblingContext = new BlockingEditorContext(
            blockDispose: false,
            closeService: new ForwardingCloseService(service));
        var foreignContext = new BlockingEditorContext(
            blockDispose: false,
            closeService: new ForwardingCloseService(foreign));
        var tab = new EditorTabItem(current);
        var siblingTab = new EditorTabItem(siblingContext);
        var foreignTab = new EditorTabItem(foreignContext);
        service.AddTabItem(tab);
        service.AddTabItem(siblingTab);
        foreign.AddTabItem(foreignTab);

        EditorContextReplacementStatus sameStatus = await service.ReplaceContextAsync(
            tab,
            new ExistingContextEditorExtension(current));
        EditorContextReplacementStatus siblingStatus = await service.ReplaceContextAsync(
            tab,
            new ExistingContextEditorExtension(siblingContext));
        EditorContextReplacementStatus foreignStatus = await service.ReplaceContextAsync(
            tab,
            new ExistingContextEditorExtension(foreignContext));

        Assert.Multiple(() =>
        {
            Assert.That(sameStatus, Is.EqualTo(EditorContextReplacementStatus.AlreadyActive));
            Assert.That(siblingStatus, Is.EqualTo(EditorContextReplacementStatus.NotOwned));
            Assert.That(foreignStatus, Is.EqualTo(EditorContextReplacementStatus.NotOwned));
            Assert.That(tab.Context.Value, Is.SameAs(current));
            Assert.That(siblingTab.Context.Value, Is.SameAs(siblingContext));
            Assert.That(foreignTab.Context.Value, Is.SameAs(foreignContext));
            Assert.That(current.DisposeCount, Is.Zero);
            Assert.That(siblingContext.DisposeCount, Is.Zero);
            Assert.That(foreignContext.DisposeCount, Is.Zero);
        });

        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await service.CloseTabItem(siblingTab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await foreign.CloseTabItem(foreignTab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task HostMediatedReplacementPreservesContextClaimedDuringFactoryCall()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        var extension = new RegisteringContextEditorExtension(service);

        EditorContextReplacementStatus status = await service.ReplaceContextAsync(tab, extension);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(EditorContextReplacementStatus.NotOwned));
            Assert.That(tab.Context.Value, Is.SameAs(current));
            Assert.That(extension.CreatedTab, Is.Not.Null);
            Assert.That(extension.CreatedContext, Is.SameAs(extension.CreatedTab!.Context.Value));
            Assert.That(extension.CreatedContext!.DisposeCount, Is.Zero);
            Assert.That(service.TabItems, Does.Contain(extension.CreatedTab));
        });

        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await service.CloseTabItem(extension.CreatedTab!).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task HostMediatedReplacementPreservesExternalHostOwnershipLease()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);

        var externalCloseService = new UnownedCloseService();
        var externalContext = new BlockingEditorContext(
            blockDispose: false,
            closeService: new ForwardingCloseService(externalCloseService));
        Assert.That(
            externalCloseService.HostToken.TryAcquireContext(
                externalContext,
                out EditorContextOwnershipLease? externalLease),
            Is.True);

        try
        {
            EditorContextReplacementStatus status = await service.ReplaceContextAsync(
                tab,
                new ExistingContextEditorExtension(externalContext));

            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(EditorContextReplacementStatus.NotOwned));
                Assert.That(tab.Context.Value, Is.SameAs(current));
                Assert.That(externalContext.DisposeCount, Is.Zero);
            });
        }
        finally
        {
            externalLease!.Dispose();
            await externalContext.DisposeAsync();
            await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task HostMediatedReplacementDisposesFactoryContextWhenTokenValidationThrows()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        var extension = new HostMediatedReplacementExtension(throwOnHostToken: true);

        InvalidOperationException? caught = null;
        try
        {
            await service.ReplaceContextAsync(tab, extension);
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }
        Assert.That(caught, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(tab.Context.Value, Is.SameAs(current));
            Assert.That(current.DisposeCount, Is.Zero);
            Assert.That(extension.CreatedContext!.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Does.Contain(tab));
        });
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task HostMediatedReplacementRejectsStaleGenerationAfterFactoryReentry()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        var extension = new BlockingReplacementExtension();

        Task<EditorContextReplacementStatus> pending =
            Task.Run(() => service.ReplaceContextAsync(tab, extension).AsTask());
        await extension.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var intervening = new BlockingEditorContext(blockDispose: false, closeService: service);
        EditorContextReplacementResult interveningResult = await tab.ReplaceContextAsync(intervening)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(interveningResult.Succeeded, Is.True);
        EditorContextReplacementResult restoredResult = await tab.ReplaceContextAsync(current)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(restoredResult.Succeeded, Is.True);

        extension.Release.TrySetResult();
        EditorContextReplacementStatus staleStatus = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(staleStatus, Is.EqualTo(EditorContextReplacementStatus.Busy));
            Assert.That(extension.CreatedContext!.DisposeCount, Is.EqualTo(1));
            Assert.That(tab.Context.Value, Is.SameAs(current));
        });
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task HostMediatedReplacementDisposesFactoryContextWhenCloseWinsDuringCreation()
    {
        var service = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(current);
        service.AddTabItem(tab);
        var extension = new BlockingReplacementExtension();

        Task<EditorContextReplacementStatus> pending =
            Task.Run(() => service.ReplaceContextAsync(tab, extension).AsTask());
        await extension.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        extension.Release.TrySetResult();
        EditorContextReplacementStatus status = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(EditorContextReplacementStatus.Busy));
            Assert.That(extension.CreatedContext!.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Does.Not.Contain(tab));
            Assert.That(tab.Context.Value, Is.Null);
        });
    }

    [Test]
    public async Task ForeignHostCannotDisposeTabWhileItsContextIsTemporarilyNull()
    {
        var owner = new EditorService(new ExtensionProvider());
        var foreign = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(closeService: owner);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: owner);
        var tab = new EditorTabItem(oldContext);
        owner.AddTabItem(tab);
        int foreignAddNotifications = 0;
        ((INotifyCollectionChanged)foreign.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
                foreignAddNotifications++;
        };

        Task<EditorContextReplacementResult> replacing = tab.ReplaceContextAsync(replacement).AsTask();
        await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(tab.Context.Value, Is.Null);

        foreign.AddTabItem(tab);
        oldContext.ReleaseDispose();

        Assert.That((await replacing!.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(owner.TabItems, Does.Contain(tab));
            Assert.That(foreign.TabItems, Is.Empty);
            Assert.That(foreignAddNotifications, Is.Zero);
            Assert.That(tab.Context.Value, Is.SameAs(replacement));
            Assert.That(replacement.DisposeCount, Is.Zero);
        });

        await owner.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task SameHostContextCanBeReplacedSuccessfully()
    {
        var owner = new EditorService(new ExtensionProvider());
        var current = new BlockingEditorContext(blockDispose: false, closeService: owner);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: owner);
        var tab = new EditorTabItem(current);
        owner.AddTabItem(tab);

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.True);
            Assert.That(tab.Context.Value, Is.SameAs(replacement));
            Assert.That(current.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.Zero);
        });

        await owner.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ConcurrentRemovalHasOnePhysicalExecutor()
    {
        var service = new EditorService(new ExtensionProvider());
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false, closeService: service));
        service.AddTabItem(tab);
        int removeNotifications = 0;
        ((INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Remove)
                Interlocked.Increment(ref removeNotifications);
        };
        using var start = new ManualResetEventSlim();

        Task<bool> first = Task.Run(async () =>
        {
            start.Wait();
            return await service.RemoveTabItemAsync(tab);
        });
        Task<bool> second = Task.Run(async () =>
        {
            start.Wait();
            return await service.RemoveTabItemAsync(tab);
        });
        start.Set();

        bool[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Some.True);
            Assert.That(removeNotifications, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task AddObserverCanWaitForCloseWithoutDeadlock()
    {
        var service = new EditorService(new ExtensionProvider());
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false, closeService: service));
        EditorContextCloseRequest closeRequest = default;
        ((INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
                closeRequest = service.RequestClose(tab.Context.Value!);
        };

        bool added = await Task.Run(() => service.TryAddTabItem(tab))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(closeRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.False);
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
        });
    }

    [Test]
    public async Task RemovalObserverCanWaitForRejectedAddToReturn()
    {
        var service = new EditorService(new ExtensionProvider());
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false, closeService: service));
        var addReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool removalObservedAddReturn = false;
        ((INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
            {
                _ = service.RequestClose(tab.Context.Value!);
            }
            else if (args.Action == NotifyCollectionChangedAction.Remove)
            {
                removalObservedAddReturn = addReturned.Task.Wait(TimeSpan.FromSeconds(2));
                if (!removalObservedAddReturn)
                    throw new TimeoutException("Removal waited on an Add that joined removal completion.");
            }
        };

        bool added = await Task.Run(() =>
        {
            try { return service.TryAddTabItem(tab); }
            finally { addReturned.TrySetResult(); }
        }).WaitAsync(TimeSpan.FromSeconds(5));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.False);
            Assert.That(removalObservedAddReturn, Is.True);
            Assert.That(service.TabItems, Is.Empty);
        });
    }

    [Test]
    public async Task RemovalBeforePhysicalAddCannotLeaveAClosedTabPublished()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        var beforeAdd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAdd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int addNotifications = 0;
        ((INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
                Interlocked.Increment(ref addNotifications);
        };

        Task<bool> add = Task.Run(() => service.TryAddTabItem(tab, () =>
        {
            beforeAdd.TrySetResult();
            releaseAdd.Task.GetAwaiter().GetResult();
        }));
        await beforeAdd.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(service.RequestTabRemoval(tab), Is.False);
        releaseAdd.TrySetResult();

        Assert.That(await add.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        await tab.GetRemovalCompletion().WaitAsync(TimeSpan.FromSeconds(5));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(addNotifications, Is.Zero);
        });
    }

    [Test]
    public async Task AttachmentCommitBeforePhysicalAddCannotPublishAfterRemovalReservation()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        var attachmentCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePhysicalAdd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int addNotifications = 0;
        ((INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
                Interlocked.Increment(ref addNotifications);
        };
        service.BeforePhysicalAdd = () =>
        {
            attachmentCommitted.TrySetResult();
            releasePhysicalAdd.Task.GetAwaiter().GetResult();
        };

        Task<bool> add = Task.Run(() => service.TryAddTabItem(tab));
        await attachmentCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(service.RequestTabRemoval(tab), Is.False);
        releasePhysicalAdd.TrySetResult();

        Assert.That(await add.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        await tab.GetRemovalCompletion().WaitAsync(TimeSpan.FromSeconds(5));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(addNotifications, Is.Zero);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RemoveObserverCanRequestCloseWithoutDeadlock()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);
        EditorContextCloseRequest nestedClose = default;
        ((INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Remove)
                nestedClose = service.RequestClose(context);
        };

        bool removed = await Task.Run(async () => await service.RemoveTabItemAsync(tab))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(nestedClose.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
        await nestedClose.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
        });
    }

    [Test]
    public async Task RemoveObserverCanWaitForCrossThreadCloseAdmission()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);
        EditorContextCloseRequest nested = default;
        ((INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Remove)
            {
                Task<EditorContextCloseRequest> request = Task.Run(() => service.RequestClose(context));
                nested = request.GetAwaiter().GetResult();
            }
        };

        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(nested.Status, Is.EqualTo(EditorContextCloseRequestStatus.AlreadyClosing));
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
        });
    }

    [Test]
    public async Task IsSelectedObserverClosingTabCannotLeaveStaleSelection()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        EditorContextCloseRequest closeRequest = default;
        using IDisposable selectionObserver = tab.IsSelected.Subscribe(isSelected =>
        {
            if (isSelected)
                closeRequest = service.RequestClose(context);
        });

        Assert.That(service.TryAddAndSelectTabItem(tab), Is.False);
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
            Assert.That(tab.Context.Value, Is.Null);
        });
    }

    [Test]
    public async Task ThrowingSelectionObserverCannotLeakNewTabContext()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        using IDisposable observer = tab.IsSelected.Subscribe(value =>
        {
            if (value)
                throw new InvalidOperationException("selection observer failed");
        });

        Assert.Throws<InvalidOperationException>(() => service.TryAddAndSelectTabItem(tab));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExistingTabSelectionObserverCanWaitForCloseWithoutDeadlock()
    {
        var service = new EditorService(new ExtensionProvider());
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "existing-selection-close.scene"))
        };
        var context = new BlockingEditorContext(blockDispose: false, scene, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);
        EditorContextCloseRequest close = default;
        using IDisposable observer = tab.IsSelected.Subscribe(value =>
        {
            if (value)
                close = service.RequestClose(context);
        });

        await Task.Run(() => service.ActivateTabItem(scene))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(close.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
        await close.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
        });
    }

    [Test]
    public async Task ExistingTabSelectionObserverCloseCannotReexposeStaleSelection()
    {
        var service = new EditorService(new ExtensionProvider());
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "existing-selection-fire-and-forget.scene"))
        };
        var context = new BlockingEditorContext(blockDispose: false, scene, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);
        EditorContextCloseRequest close = default;
        using IDisposable observer = tab.IsSelected.Subscribe(value =>
        {
            if (value)
                close = service.RequestClose(context);
        });

        service.ActivateTabItem(scene);
        Assert.That(close.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
        await close.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
        });
    }

    [Test]
    public async Task DelayedSelectionObserverChildUsesNormalDisposeSemantics()
    {
        var service = new EditorService(new ExtensionProvider());
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "delayed-selection-close.scene"))
        };
        var context = new BlockingEditorContext(obj: scene, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);
        var releaseChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? delayedClose = null;
        using IDisposable observer = tab.IsSelected.Subscribe(value =>
        {
            if (value)
            {
                delayedClose = Task.Run(async () =>
                {
                    await releaseChild.Task;
                    await service.CloseTabItem(tab);
                });
            }
        });

        service.ActivateTabItem(scene);
        Assert.That(delayedClose, Is.Not.Null);
        releaseChild.TrySetResult();
        await context.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(delayedClose!.IsCompleted, Is.False);

        context.ReleaseDispose();
        await delayedClose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(service.TabItems, Is.Empty);
    }

    [Test]
    public async Task SelectionObserverCannotStartACompetingReplacement()
    {
        var service = new EditorService(new ExtensionProvider());
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "selection-replacement.scene"))
        };
        var original = new BlockingEditorContext(blockDispose: false, scene, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(original);
        service.AddTabItem(tab);
        EditorContextReplacementResult? replaced = null;
        using IDisposable observer = tab.IsSelected.Subscribe(value =>
        {
            if (value)
            {
                replaced = tab.ReplaceContextAsync(replacement)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        });

        await Task.Run(() => service.ActivateTabItem(scene))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced?.Succeeded, Is.False);
            Assert.That(tab.Context.Value, Is.SameAs(original));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
            Assert.That(service.SelectedTabItem.Value, Is.SameAs(tab));
        });
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task SelectionAndRemovalDoNotInvertContextPublicationGate()
    {
        for (int iteration = 0; iteration < 20; iteration++)
        {
            var service = new EditorService(new ExtensionProvider());
            var scene = new Scene(16, 16, string.Empty)
            {
                Uri = new Uri(Path.Combine(Path.GetTempPath(), $"publication-gate-{iteration}.scene"))
            };
            var context = new GatedEditorContext(scene, service);
            var tab = new EditorTabItem(context);
            service.AddTabItem(tab);
            service.SelectedTabItem.Value = tab;
            context.PauseNextPublication();

            ((System.Collections.Specialized.INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, args) =>
            {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                {
                    context.EnterGate();
                    context.ExitGate();
                }
            };

            Task activate = Task.Run(() => service.ActivateTabItem(scene));
            await context.PublicationPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<bool> remove = Task.Run(async () => await service.RemoveTabItemAsync(tab));
            context.ReleasePublication();

            await Task.WhenAll(
                activate.WaitAsync(TimeSpan.FromSeconds(5)),
                remove.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Multiple(() =>
            {
                Assert.That(service.TabItems, Is.Empty, $"iteration {iteration}");
                Assert.That(service.SelectedTabItem.Value, Is.Null, $"iteration {iteration}");
            });
            await tab.DisposeAsync();
        }
    }

    [Test]
    public async Task ReplacementAndRemovalDoNotInvertContextPublicationGate()
    {
        var service = new EditorService(new ExtensionProvider());
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false, closeService: service));
        service.AddTabItem(tab);
        var replacement = new GatedEditorContext(new Scene(16, 16, string.Empty), service);
        using IDisposable contextSubscription = tab.Context.Subscribe(value =>
        {
            if (ReferenceEquals(value, replacement))
                _ = tab.IsPublicationCurrent();
        });
        replacement.PauseNextPublication();

        ((System.Collections.Specialized.INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                replacement.EnterGate();
                replacement.ExitGate();
            }
        };

        Task<EditorContextReplacementResult> replace = Task.Run(async () => await tab.ReplaceContextAsync(replacement));
        await replacement.PublicationPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(service.RequestTabRemoval(tab), Is.False);
        Assert.That(service.ContainsTabItem(tab), Is.True,
            "Physical removal waits for the admitted replacement publication to drain.");
        replacement.ReleasePublication();

        Assert.That((await replace!.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded, Is.False);
        await tab.GetRemovalCompletion().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
        });
        await tab.DisposeAsync();
        Assert.That(replacement.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeAsyncPublishesOneSharedTerminalTask()
    {
        var context = new BlockingEditorContext();
        var tab = new EditorTabItem(context);

        Task first = tab.DisposeAsync().AsTask();
        Task second = tab.DisposeAsync().AsTask();

        Assert.That(second, Is.SameAs(first));
        Assert.That(first.IsCompleted, Is.False);
        Assert.That(context.DisposeStarted.Task.IsCompleted, Is.True);

        context.ReleaseDispose();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(tab.DisposeAsync().AsTask(), Is.SameAs(first));
            Assert.That(context.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ReplaceContextAsync_RejectsAndDisposesNewContextWhenCloseWins()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(closeService: service);
        var newContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);

        Task close = tab.DisposeAsync().AsTask();
        Task<EditorContextReplacementResult> replacement = tab.ReplaceContextAsync(newContext).AsTask();

        oldContext.ReleaseDispose();
        EditorContextReplacementResult replacementResult = await replacement!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replacementResult.Status, Is.EqualTo(EditorContextReplacementStatus.NotOwned));
        Assert.That(replacementResult.InputConsumed, Is.False);
        await newContext.DisposeAsync();

        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(newContext.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SameInstanceReplacementIsRejectedWithoutConsumption()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(context)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.False);
            Assert.That(tab.Context.Value, Is.SameAs(context));
            Assert.That(context.DisposeCount, Is.Zero);
        });
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task StandaloneSameInstanceReplacementIsRejectedWithoutConsumption()
    {
        var context = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(context);

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(context)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.False);
            Assert.That(tab.Context.Value, Is.SameAs(context));
            Assert.That(context.DisposeCount, Is.Zero);
        });
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task CrossTabReplacementIsRejectedWithoutConsumption()
    {
        var service = new EditorService(new ExtensionProvider());
        var firstContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var secondContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var first = new EditorTabItem(firstContext);
        var second = new EditorTabItem(secondContext);
        service.AddTabItem(first);
        service.AddTabItem(second);

        EditorContextReplacementResult replaced = await first.ReplaceContextAsync(secondContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.False);
            Assert.That(first.Context.Value, Is.SameAs(firstContext));
            Assert.That(second.Context.Value, Is.SameAs(secondContext));
            Assert.That(firstContext.DisposeCount, Is.Zero);
            Assert.That(secondContext.DisposeCount, Is.Zero);
        });
        await service.CloseTabItem(first).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await service.CloseTabItem(second).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ClosingTabCannotConsumeAnotherTabsContext()
    {
        var service = new EditorService(new ExtensionProvider());
        var closingContext = new BlockingEditorContext(closeService: service);
        var otherContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var closing = new EditorTabItem(closingContext);
        var other = new EditorTabItem(otherContext);
        service.AddTabItem(closing);
        service.AddTabItem(other);

        Task close = service.CloseTabItem(closing).AsTask();
        await closingContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextReplacementResult replaced = await closing.ReplaceContextAsync(otherContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(replaced.Succeeded, Is.False);
        Assert.That(otherContext.DisposeCount, Is.Zero);
        closingContext.ReleaseDispose();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        await service.CloseTabItem(other).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RejectedReplacementClaimRemainsHeldUntilDisposalCompletes()
    {
        var service = new EditorService(new ExtensionProvider());
        var closingContext = new BlockingEditorContext(closeService: service);
        var rejectedContext = new BlockingEditorContext(closeService: service);
        var closing = new EditorTabItem(closingContext);
        service.AddTabItem(closing);

        Task<EditorContextReplacementResult> replace = closing.ReplaceContextAsync(rejectedContext).AsTask();
        await closingContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task close = service.CloseTabItem(closing).AsTask();
        closingContext.ReleaseDispose();
        await rejectedContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var competing = new EditorTabItem(rejectedContext);
        Assert.That(service.TryAddTabItem(competing), Is.False);

        rejectedContext.ReleaseDispose();
        EditorContextReplacementResult replacementResult = await replace.WaitAsync(TimeSpan.FromSeconds(5));
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(replacementResult.Status, Is.EqualTo(EditorContextReplacementStatus.Busy));
            Assert.That(replacementResult.InputConsumed, Is.True);
            Assert.That(rejectedContext.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Does.Not.Contain(competing));
        });
    }

    [Test]
    public async Task TransitioningTabCannotConsumeAnotherTabsContext()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var otherContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var transitioning = new EditorTabItem(oldContext);
        var other = new EditorTabItem(otherContext);
        service.AddTabItem(transitioning);
        service.AddTabItem(other);

        Task<EditorContextReplacementResult> firstReplacement = transitioning.ReplaceContextAsync(replacement).AsTask();
        await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditorContextReplacementResult rejected = await transitioning.ReplaceContextAsync(otherContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(rejected.Succeeded, Is.False);
        Assert.That(otherContext.DisposeCount, Is.Zero);
        oldContext.ReleaseDispose();
        Assert.That((await firstReplacement!.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded, Is.True);
        await service.CloseTabItem(transitioning).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await service.CloseTabItem(other).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ReservedReplacementIdentityCanRequestOwningTabClose()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        EditorContextCloseRequest closeRequest = default;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null)
                closeRequest = service.RequestClose(replacement);
        });

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.False);
            Assert.That(closeRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StaleContextCloseGenerationCannotCloseReplacement()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        var lookupCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAdmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.BeforeContextCloseAdmission = () =>
        {
            lookupCompleted.TrySetResult();
            releaseAdmission.Task.GetAwaiter().GetResult();
        };

        Task<EditorContextCloseRequest> staleClose = Task.Run(() => service.RequestClose(oldContext));
        await lookupCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(
            (await tab.ReplaceContextAsync(replacement).AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Succeeded,
            Is.True);
        releaseAdmission.TrySetResult();

        EditorContextCloseRequest request = await staleClose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
            Assert.That(tab.Context.Value, Is.SameAs(replacement));
            Assert.That(service.TabItems, Does.Contain(tab));
            Assert.That(replacement.DisposeCount, Is.Zero);
        });

        service.BeforeContextCloseAdmission = null;
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task HostCloseReservationRejectsReplacementBeforeCloseStarts()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        var reservationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCloseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.BeforeHostCloseStart = () =>
        {
            reservationCompleted.TrySetResult();
            releaseCloseStart.Task.GetAwaiter().GetResult();
        };

        Task<EditorContextCloseRequest> close = Task.Run(() => service.RequestClose(oldContext));
        await reservationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replaced.Succeeded, Is.False);
        Assert.That(oldContext.DisposeCount, Is.Zero);
        Assert.That(replacement.DisposeCount, Is.EqualTo(1));

        releaseCloseStart.TrySetResult();
        EditorContextCloseRequest request = await close.WaitAsync(TimeSpan.FromSeconds(5));
        await request.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
    }

    [Test]
    public async Task ThrowingHostCloseStartStillCompletesTerminalCleanup()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);
        service.BeforeHostCloseStart = () => throw new InvalidOperationException("close start failed");

        EditorContextCloseRequest request = service.RequestClose(context);
        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await request.Completion.WaitAsync(TimeSpan.FromSeconds(5)))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("close start failed"));
            Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
        Assert.That(
            service.HostToken.TryAcquireContext(
                context,
                out EditorContextOwnershipLease? successor),
            Is.True);
        successor!.Dispose();
    }

    [Test]
    public async Task HostOwnedOutgoingDisposalDoesNotRequestTabClose()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        oldContext.OnDispose = () =>
        {
            service.RequestContextShutdown(oldContext);
            return ValueTask.CompletedTask;
        };

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.True);
            Assert.That(service.TabItems, Does.Contain(tab));
            Assert.That(tab.Context.Value, Is.SameAs(replacement));
        });
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RejectedReplacementDisposalFailureIsReportedAfterCleanup()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(closeService: service);
        var replacement = new ThrowingEditorContext(service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);

        Task close = tab.DisposeAsync().AsTask();
        Task<EditorContextReplacementResult> replace = tab.ReplaceContextAsync(replacement).AsTask();

        EditorContextReplacementResult replacementResult = await replace.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replacementResult.Status, Is.EqualTo(EditorContextReplacementStatus.NotOwned));
        Assert.That(replacementResult.InputConsumed, Is.False);
        Assert.CatchAsync<InvalidOperationException>(async () => await replacement.DisposeAsync());
        oldContext.ReleaseDispose();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ReplacementContextDisposalCanReenterTabCloseWithoutDeadlock()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var newContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        EditorContextCloseRequest closeRequest = default;
        oldContext.OnDispose = () =>
        {
            closeRequest = service.RequestClose(oldContext);
            return ValueTask.CompletedTask;
        };

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(newContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.False);
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(newContext.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ContextPropertyPublishesNullOnlyAfterTerminalClose()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var newContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        var values = new List<IEditorContext?>();
        using IDisposable subscription = tab.Context.Subscribe(values.Add);

        Assert.That((await tab.ReplaceContextAsync(newContext).AsTask()).Succeeded, Is.True);
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(values, Does.Contain(null));
    }

    [Test]
    public async Task FailedReplacementTerminallyRemovesTabAndClearsSelection()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new ThrowingEditorContext(service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        service.SelectedTabItem.Value = tab;
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var values = new List<IEditorContext?>();
        using IDisposable subscription = tab.Context.Subscribe(values.Add);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await tab.ReplaceContextAsync(replacement).AsTask());
        Assert.That(service.TabItems, Is.Empty);
        Assert.That(service.SelectedTabItem.Value, Is.Null);
        Assert.That(values, Does.Contain(null));
        Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
        Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        await tab.DisposeAsync();
    }

    [Test]
    public async Task ConcurrentCloseJoinsThrowingReplacementWithoutDeadlock()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(closeService: service);
        var fail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldContext.OnDispose = async () =>
        {
            await fail.Task;
            throw new InvalidOperationException("dispose failed");
        };
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        service.SelectedTabItem.Value = tab;

        Task replacement = tab.ReplaceContextAsync(new BlockingEditorContext(blockDispose: false, closeService: service)).AsTask();
        await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task close = tab.DisposeAsync().AsTask();
        fail.TrySetResult();
        oldContext.ReleaseDispose();

        Assert.ThrowsAsync<InvalidOperationException>(async () => await replacement.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(close.IsCompleted, Is.True,
            "The public replacement failure must not complete before terminal close cleanup.");
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(service.TabItems, Is.Empty);
    }

    [Test]
    public async Task ContextSubscriberCanReenterCloseWhenReplacementPublishesNull()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        Task? reentrantClose = null;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null)
                reentrantClose = tab.DisposeAsync().AsTask();
        });

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replaced.Succeeded, Is.False);
        if (reentrantClose is not null)
            await reentrantClose.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ContextSubscriberCanRequestHostCloseWhenReplacementPublishesNull()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        EditorContextCloseRequest closeRequest = default;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null)
                closeRequest = service.RequestClose(oldContext);
        });

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.False);
            Assert.That(closeRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CloseStartedByReplacementPublicationWinsTheResult()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        Task? close = null;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (ReferenceEquals(value, replacement))
                close = tab.DisposeAsync().AsTask();
        });

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replaced.Succeeded, Is.False);
        Assert.That(close, Is.Not.Null);
        await close!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replacement.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ContextSubscriberCannotStartACompetingReplacement()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var nested = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        Task<EditorContextReplacementResult>? nestedReplacement = null;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null && nestedReplacement is null)
                nestedReplacement = tab.ReplaceContextAsync(nested).AsTask();
        });

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replaced.Succeeded, Is.True);
        Assert.That(nestedReplacement, Is.Not.Null);
        Assert.That((await nestedReplacement!.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(nested.DisposeCount, Is.EqualTo(1));
            Assert.That(tab.Context.Value!, Is.SameAs(replacement));
        });
        await tab.DisposeAsync();
    }

    [Test]
    public async Task DelayedOwnedDisposalChildUsesNormalDisposeSemanticsAfterScopeEnds()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        var releaseChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? delayedClose = null;
        oldContext.OnDispose = () =>
        {
            delayedClose = Task.Run(async () =>
            {
                await releaseChild.Task;
                await tab.DisposeAsync();
            });
            return ValueTask.CompletedTask;
        };

        Assert.That(
            (await tab.ReplaceContextAsync(replacement).AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Succeeded,
            Is.True);
        Assert.That(delayedClose, Is.Not.Null);
        releaseChild.TrySetResult();
        await replacement.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(delayedClose!.IsCompleted, Is.False);

        replacement.ReleaseDispose();
        await delayedClose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replacement.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ContextPublicationDoesNotRunRejectedPluginTeardownUnderTheHostGate()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var nested = new BlockingEditorContext(blockDispose: false, closeService: service);
        var third = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        Task<EditorContextReplacementResult>? nestedReplacement = null;
        nested.OnDispose = () =>
        {
            Task competing = Task.Run(() =>
                tab.ReplaceContextAsync(third).AsTask().GetAwaiter().GetResult());
            if (!competing.Wait(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("The tab lifetime gate was held by Context publication.");
            return ValueTask.CompletedTask;
        };
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null && nestedReplacement is null)
                nestedReplacement = tab.ReplaceContextAsync(nested).AsTask();
        });

        Assert.That((await tab.ReplaceContextAsync(replacement).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5))).Succeeded, Is.True);
        Assert.That((await nestedReplacement!.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(nested.DisposeCount, Is.EqualTo(1));
            Assert.That(third.DisposeCount, Is.EqualTo(1));
        });
        await tab.DisposeAsync();
    }

    [Test]
    public async Task RejectedReplacementCanReenterCloseWithoutDeadlock()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        EditorContextCloseRequest nestedClose = default;
        replacement.OnDispose = () =>
        {
            nestedClose = service.RequestClose(oldContext);
            return ValueTask.CompletedTask;
        };

        Task<EditorContextReplacementResult> replace = tab.ReplaceContextAsync(replacement).AsTask();
        await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task close = service.CloseTabItem(tab).AsTask();
        oldContext.ReleaseDispose();

        Assert.That((await replace!.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded, Is.False);
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(nestedClose.Status, Is.EqualTo(EditorContextCloseRequestStatus.AlreadyClosing));
        Assert.Multiple(() =>
        {
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ImmediateRejectedReplacementUsesTheOwnedDisposalFence()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var nested = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        Task<EditorContextReplacementResult>? nestedReplacement = null;
        EditorContextCloseRequest closeRequest = default;
        nested.OnDispose = () =>
        {
            closeRequest = service.RequestClose(oldContext);
            return ValueTask.CompletedTask;
        };
        oldContext.OnDispose = async () =>
        {
            nestedReplacement = tab.ReplaceContextAsync(nested).AsTask();
            await nestedReplacement;
        };

        EditorContextReplacementResult replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Succeeded, Is.False);
            Assert.That(nestedReplacement, Is.Not.Null);
            Assert.That(nestedReplacement!.Result.Succeeded, Is.False);
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
            Assert.That(nested.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ThrowingContextSubscriberCannotStrandReplacement()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null)
                throw new InvalidOperationException("observer failed");
        });

        Assert.CatchAsync<InvalidOperationException>(async () =>
            await tab.ReplaceContextAsync(replacement).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Multiple(() =>
        {
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ThrowingReplacementSubscriberDisposesPublishedReplacement()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var replacement = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (ReferenceEquals(value, replacement))
                throw new InvalidOperationException("replacement observer failed");
        });

        Assert.CatchAsync<InvalidOperationException>(async () =>
            await tab.ReplaceContextAsync(replacement).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
            Assert.That(tab.Context.Value, Is.Null);
        });
    }

    [Test]
    public async Task ThrowingContextSubscriberCannotSkipDirectDispose()
    {
        var context = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(context);
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null)
                throw new InvalidOperationException("observer failed");
        });

        Assert.CatchAsync<Exception>(async () =>
            await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ThrowingRemovalObserversCannotSkipTabCleanup()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false, closeService: service);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);
        service.SelectedTabItem.Value = tab;
        ((INotifyCollectionChanged)service.TabItems).CollectionChanged += (_, _) =>
            throw new InvalidOperationException("collection observer failed");
        using IDisposable selected = service.SelectedTabItem.Subscribe(value =>
        {
            if (value is null)
                throw new InvalidOperationException("selection observer failed");
        });

        Assert.CatchAsync<Exception>(async () =>
            await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ClosingOneTabWaitsForSiblingCloseStartedByItsContext()
    {
        var service = new EditorService(new ExtensionProvider());
        var firstContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var secondContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var first = new EditorTabItem(firstContext);
        var second = new EditorTabItem(secondContext);
        service.AddTabItem(first);
        service.AddTabItem(second);
        EditorContextCloseRequest secondClose = default;
        firstContext.OnDispose = () =>
        {
            secondClose = service.RequestClose(secondContext);
            return ValueTask.CompletedTask;
        };

        await service.CloseTabItem(first).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await secondClose.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(firstContext.DisposeCount, Is.EqualTo(1));
            Assert.That(secondContext.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
    }

    [Test]
    public async Task MutualSiblingCloseUsesAncestorIdentityWithoutCycle()
    {
        var service = new EditorService(new ExtensionProvider());
        var firstContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var secondContext = new BlockingEditorContext(blockDispose: false, closeService: service);
        var first = new EditorTabItem(firstContext);
        var second = new EditorTabItem(secondContext);
        service.AddTabItem(first);
        service.AddTabItem(second);
        EditorContextCloseRequest secondClose = default;
        EditorContextCloseRequest firstReentrantClose = default;
        firstContext.OnDispose = () =>
        {
            secondClose = service.RequestClose(secondContext);
            return ValueTask.CompletedTask;
        };
        secondContext.OnDispose = () =>
        {
            firstReentrantClose = service.RequestClose(firstContext);
            return ValueTask.CompletedTask;
        };

        await service.CloseTabItem(first).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await secondClose.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(firstContext.DisposeCount, Is.EqualTo(1));
            Assert.That(secondContext.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(firstReentrantClose.Status, Is.EqualTo(EditorContextCloseRequestStatus.AlreadyClosing));
        });
    }

    private class BlockingEditorContext : IEditorContext
    {
        private readonly bool _blockDispose;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingEditorContext(
            bool blockDispose = true,
            CoreObject? obj = null,
            IEditorContextCloseService? closeService = null)
        {
            _blockDispose = blockDispose;
            Object = obj ?? new Scene(16, 16, string.Empty);
            CloseService = closeService ?? new UnownedCloseService();
        }

        public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; protected set; }

        public Func<ValueTask>? OnDispose { get; set; }

        public CoreObject Object { get; }

        public EditorExtension Extension { get; } = TestEditorExtension.Instance;

        public IEditorContextCloseService CloseService { get; }

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public virtual async ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposeStarted.TrySetResult();
            if (OnDispose is { } onDispose)
                await onDispose();
            if (_blockDispose)
                await _release.Task.ConfigureAwait(false);

            Disposed.TrySetResult();
        }

        public void ReleaseDispose() => _release.TrySetResult();

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public ValueTask<bool> OpenToolTabAsync(IToolContext item) => new(false);

        public ValueTask CloseToolTabAsync(IToolContext item)
        {
            return ValueTask.CompletedTask;
        }

        public object? GetService(Type serviceType) => null;
    }

    private sealed class UnownedCloseService : IEditorContextCloseService
    {
        public EditorContextHostToken HostToken { get; } = new();

        public EditorContextCloseRequest RequestClose(IEditorContext context)
            => new(EditorContextCloseRequestStatus.NotOwned, Task.CompletedTask);
    }

    private sealed class GatedEditorContext(
        Scene scene,
        IEditorContextCloseService? closeService = null)
        : BlockingEditorContext(false, scene, closeService), IEditorContextPublicationGate
    {
        private readonly object _gate = new();
        private bool _pauseNext;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PublicationPaused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void PauseNextPublication() => _pauseNext = true;

        public void ReleasePublication() => _release.TrySetResult();

        public bool TryPublish(Action publish)
        {
            lock (_gate)
            {
                if (_pauseNext)
                {
                    _pauseNext = false;
                    PublicationPaused.TrySetResult();
                    _release.Task.GetAwaiter().GetResult();
                }
            }

            publish();
            return true;
        }

        public void EnterGate() => Monitor.Enter(_gate);

        public void ExitGate() => Monitor.Exit(_gate);
    }

    private sealed class ThrowingEditorContext(
        IEditorContextCloseService? closeService = null,
        CoreObject? obj = null)
        : BlockingEditorContext(obj: obj, closeService: closeService)
    {
        private bool _throw = true;

        public override async ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposeStarted.TrySetResult();
            if (!_throw)
            {
                Disposed.TrySetResult();
                return;
            }
            _throw = false;
            await Task.Yield();
            throw new InvalidOperationException("dispose failed");
        }
    }

    private sealed class TestEditorExtension : EditorExtension
    {
        public static readonly TestEditorExtension Instance = new();

        public override FilePickerFileType GetFilePickerFileType() => new("Test");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(CoreObject obj, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => false;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            context = null;
            return false;
        }
    }

    private sealed class HostMediatedReplacementExtension(bool throwOnHostToken = false) : EditorExtension
    {
        public BlockingEditorContext? CreatedContext { get; private set; }

        public int CreationCount { get; private set; }

        public override FilePickerFileType GetFilePickerFileType() => new("Replacement");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => false;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            CreationCount++;
            IEditorContextCloseService closeService = throwOnHostToken
                ? new ThrowingHostTokenCloseService()
                : services.CloseService;
            CreatedContext = new BlockingEditorContext(blockDispose: false, obj, closeService);
            context = CreatedContext;
            return true;
        }
    }

    private sealed class ExistingContextEditorExtension(IEditorContext existingContext) : EditorExtension
    {
        public override FilePickerFileType GetFilePickerFileType() => new("Existing");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => false;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            context = existingContext;
            return true;
        }
    }

    private sealed class ActivationExistingContextEditorExtension(IEditorContext existingContext)
        : EditorExtension
    {
        public int CreationCount { get; private set; }

        public override FilePickerFileType GetFilePickerFileType() => new("ActivationExisting");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => true;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            CreationCount++;
            context = existingContext;
            return true;
        }
    }

    private sealed class SuppliedContextEditorExtension(IEditorContext suppliedContext)
        : EditorExtension
    {
        public override FilePickerFileType GetFilePickerFileType() => new("Supplied");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => true;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            context = suppliedContext;
            return true;
        }
    }

    private sealed class ImmediateFaultEditorContext(
        CoreObject obj,
        IEditorContextCloseService closeService)
        : BlockingEditorContext(blockDispose: false, obj, closeService)
    {
        public override ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposeStarted.TrySetResult();
            return ValueTask.FromException(new InvalidOperationException("immediate teardown failure"));
        }
    }

    private sealed class DelayedFaultEditorContext(
        CoreObject obj,
        IEditorContextCloseService closeService)
        : BlockingEditorContext(blockDispose: false, obj, closeService)
    {
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposeStarted.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            throw new InvalidOperationException("delayed teardown failure");
        }
    }

    private sealed class ReentrantDisposeEditorContext(
        CoreObject obj,
        IEditorContextCloseService closeService)
        : BlockingEditorContext(blockDispose: false, obj, closeService)
    {
        public Func<ValueTask>? AfterAwait { get; set; }

        public override async ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposeStarted.TrySetResult();
            await Task.Yield();
            if (AfterAwait is { } callback)
                await callback();
        }
    }

    private sealed class ClaimAwareEditorContext(
        EditorService tracker,
        CoreObject obj,
        IEditorContextCloseService closeService)
        : BlockingEditorContext(blockDispose: false, obj, closeService)
    {
        public TaskCompletionSource Claimed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public object? Entry { get; private set; }

        public Func<ValueTask>? AfterClaim { get; set; }

        public override async ValueTask DisposeAsync()
        {
            object entry = Entry = CaptureOnlyActiveLifecycleTeardown(tracker);
            DisposeCount++;
            DisposeStarted.TrySetResult();
            bool taken = await Task.Run(() => SpinWait.SpinUntil(
                () => IsLifecycleTeardownTaken(tracker, entry),
                TimeSpan.FromSeconds(5)));
            if (!taken)
                throw new TimeoutException("The lifecycle teardown was not claimed.");

            Claimed.TrySetResult();
            if (AfterClaim is { } callback)
                await callback();
            Disposed.TrySetResult();
        }
    }

    private sealed class MatchedContextEditorExtension(
        string fileExtension,
        IEditorContext context) : EditorExtension
    {
        public override FilePickerFileType GetFilePickerFileType() => new("Sibling");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => ext == fileExtension;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? result)
        {
            result = context;
            return true;
        }
    }

    private static (Scene Parent, Scene Sibling) CreateSiblingDrainScenes(string suffix)
    {
        var parent = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(
                Path.GetTempPath(),
                $"final-drain-{suffix}.parent-drain"))
        };
        var sibling = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(
                Path.GetTempPath(),
                $"final-drain-{suffix}.sibling-drain"))
        };
        return (parent, sibling);
    }

    private static object CaptureOnlyActiveLifecycleTeardown(EditorService service)
    {
        var gateField = typeof(EditorService).GetField(
            "_lifecycleTeardownGate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var activeField = typeof(EditorService).GetField(
            "_activeLifecycleTeardowns",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object gate = gateField.GetValue(service)!;
        lock (gate)
        {
            var active = (System.Collections.IEnumerable)activeField.GetValue(service)!;
            return active.Cast<object>().Single();
        }
    }

    private static bool IsLifecycleTeardownTaken(EditorService service, Task completion)
    {
        var gateField = typeof(EditorService).GetField(
            "_lifecycleTeardownGate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var mapField = typeof(EditorService).GetField(
            "_lifecycleTeardownsByTask",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        object gate = gateField.GetValue(service)!;
        lock (gate)
        {
            object? entry = null;
            if (mapField?.GetValue(service) is System.Collections.IDictionary map)
                entry = map[completion];

            if (entry is null)
            {
                var activeField = typeof(EditorService).GetField(
                    "_activeLifecycleTeardowns",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var active = (System.Collections.IEnumerable)activeField.GetValue(service)!;
                entry = active.Cast<object>().FirstOrDefault(candidate =>
                    ReferenceEquals(GetLifecycleTeardownTask(candidate), completion));
            }

            return entry is not null && ReadLifecycleTeardownTaken(entry);
        }
    }

    private static bool IsLifecycleTeardownTaken(EditorService service, object entry)
    {
        var gateField = typeof(EditorService).GetField(
            "_lifecycleTeardownGate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object gate = gateField.GetValue(service)!;
        lock (gate)
        {
            return ReadLifecycleTeardownTaken(entry);
        }
    }

    private static bool ReadLifecycleTeardownTaken(object entry)
        => (bool)entry.GetType().GetProperty(nameof(DeferredLifecycleTeardownView.Taken))!
            .GetValue(entry)!;

    private static bool IsLifecycleTeardownOperationMapped(EditorService service, object entry)
    {
        var gateField = typeof(EditorService).GetField(
            "_lifecycleTeardownGate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var mapField = typeof(EditorService).GetField(
            "_lifecycleTeardownsByOperation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (mapField is null)
            return false;
        object operation = entry.GetType().GetProperty(nameof(DeferredLifecycleTeardownView.Operation))!
            .GetValue(entry)!;
        object gate = gateField.GetValue(service)!;
        lock (gate)
        {
            var map = (System.Collections.IDictionary)mapField.GetValue(service)!;
            return map.Contains(operation);
        }
    }

    private static Task GetLifecycleTeardownTask(object entry)
        => (Task)entry.GetType().GetProperty(nameof(DeferredLifecycleTeardownView.Task))!
            .GetValue(entry)!;

    private sealed class DeferredLifecycleTeardownView
    {
        public Task Task { get; } = Task.CompletedTask;

        public object Operation { get; } = new();

        public bool Taken { get; }
    }

    private static async Task<Exception?> CaptureFailureAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class ThrowingActivationContextEditorExtension : EditorExtension
    {
        public TaskCompletionSource<BlockingEditorContext> CreatedContext { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override FilePickerFileType GetFilePickerFileType() => new("ActivationThrowing");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => ext == ".activation-fault";

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            var created = new BlockingEditorContext(
                blockDispose: false,
                obj,
                new ThrowingHostTokenCloseService());
            CreatedContext.TrySetResult(created);
            context = created;
            return true;
        }
    }

    private sealed class ForwardingCloseService(IEditorContextCloseService inner)
        : IEditorContextCloseService
    {
        public EditorContextHostToken HostToken => inner.HostToken;

        public EditorContextCloseRequest RequestClose(IEditorContext context)
            => inner.RequestClose(context);
    }

    private sealed class RegisteringContextEditorExtension(EditorService service) : EditorExtension
    {
        public BlockingEditorContext? CreatedContext { get; private set; }

        public EditorTabItem? CreatedTab { get; private set; }

        public override FilePickerFileType GetFilePickerFileType() => new("Registering");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => false;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            CreatedContext = new BlockingEditorContext(
                blockDispose: false,
                obj,
                new ForwardingCloseService(service));
            CreatedTab = new EditorTabItem(CreatedContext);
            service.AddTabItem(CreatedTab);
            context = CreatedContext;
            return true;
        }
    }

    private sealed class ThrowingHostTokenCloseService : IEditorContextCloseService
    {
        public EditorContextHostToken HostToken => throw new InvalidOperationException("Host token unavailable");

        public EditorContextCloseRequest RequestClose(IEditorContext context)
            => new(EditorContextCloseRequestStatus.NotOwned, Task.CompletedTask);
    }

    private sealed class BlockingReplacementExtension : EditorExtension
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingEditorContext? CreatedContext { get; private set; }

        public override FilePickerFileType GetFilePickerFileType() => new("Blocking");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => false;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            Entered.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            CreatedContext = new BlockingEditorContext(blockDispose: false, obj, services.CloseService);
            context = CreatedContext;
            return true;
        }
    }

    private sealed class ReentrantReconcileReplacementExtension(
        EditorService service,
        bool useWorker,
        bool matchFileExtension = false)
        : EditorExtension
    {
        public int CreationCount { get; private set; }

        public override FilePickerFileType GetFilePickerFileType() => new("ReentrantReconcile");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => matchFileExtension;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            CreationCount++;
            if (useWorker)
            {
                Task.Run(() => service.ClearTabItemsAsync().AsTask())
                    .GetAwaiter()
                    .GetResult();
            }
            else
            {
                service.ClearTabItemsAsync().AsTask().GetAwaiter().GetResult();
            }
            context = null;
            return false;
        }
    }

    private sealed class DeferredReconcileReplacementExtension(EditorService service)
        : EditorExtension
    {
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Reconciliation { get; private set; } = Task.CompletedTask;

        public override FilePickerFileType GetFilePickerFileType() => new("DeferredReconcile");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => false;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            Reconciliation = Task.Run(async () =>
            {
                await Release.Task;
                await service.ClearTabItemsAsync();
            });
            context = null;
            return false;
        }
    }

    private sealed class CapturingAdmissionChildExtension : EditorExtension
    {
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Child { get; private set; } = Task.CompletedTask;

        public override FilePickerFileType GetFilePickerFileType() => new("CapturingChild");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => false;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            TaskCompletionSource release = Release;
            Child = Task.Run(async () => await release.Task);
            context = null;
            return false;
        }
    }

    private sealed class NestedReplacementExtension(
        EditorService service,
        EditorTabItem tab,
        EditorExtension nestedExtension) : EditorExtension
    {
        public override FilePickerFileType GetFilePickerFileType() => new("NestedReplacement");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => false;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            service.ReplaceContextAsync(tab, nestedExtension).AsTask().GetAwaiter().GetResult();
            context = null;
            return false;
        }
    }

    private sealed class ForeignContextEditorExtension(
        EditorService foreignHost,
        bool blockDispose = false) : EditorExtension
    {
        public TaskCompletionSource<BlockingEditorContext> CreatedContext { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override FilePickerFileType GetFilePickerFileType() => new("Foreign");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => ext == ".activation";

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            var created = new BlockingEditorContext(blockDispose, obj, foreignHost);
            CreatedContext.TrySetResult(created);
            context = created;
            return true;
        }
    }

    private sealed class TransferredContextEditorExtension(
        EditorService host,
        bool blockDispose = false) : EditorExtension
    {
        public TaskCompletionSource<BlockingEditorContext> CreatedContext { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override FilePickerFileType GetFilePickerFileType() => new("Throwing");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => ext == ".activation-fault";

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            var created = new BlockingEditorContext(blockDispose, obj, host);
            CreatedContext.TrySetResult(created);
            context = created;
            return true;
        }
    }
}

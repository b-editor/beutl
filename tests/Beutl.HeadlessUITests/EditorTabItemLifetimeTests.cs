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
    public async Task AddAndSelectIsLinearizedWithConcurrentDispose()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false);
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
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false));
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
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false));

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
        var context = new BlockingEditorContext();
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);

        EditorContextCloseRequest accepted = service.RequestClose(context);
        EditorContextCloseRequest repeated = service.RequestClose(context);
        var unknown = new BlockingEditorContext(blockDispose: false);
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
        var context = new ThrowingEditorContext();
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
        var context = new BlockingEditorContext(blockDispose: false);
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
        var context = new BlockingEditorContext(blockDispose: false);
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
        var context = new BlockingEditorContext(blockDispose: false);
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
        var context = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(context);
        using var beforeAttach = new Barrier(2);
        firstService.BeforeInitialOwnerAttach = WaitForBothServices;
        secondService.BeforeInitialOwnerAttach = WaitForBothServices;

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

        void WaitForBothServices()
        {
            if (!beforeAttach.SignalAndWait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Both services did not reach owner attachment.");
        }
    }

    [Test]
    public async Task ConcurrentRemovalHasOnePhysicalExecutor()
    {
        var service = new EditorService(new ExtensionProvider());
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false));
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
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false));
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
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false));
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
        var context = new BlockingEditorContext(blockDispose: false);
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
        var context = new BlockingEditorContext(blockDispose: false);
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
        var context = new BlockingEditorContext(blockDispose: false);
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
        var context = new BlockingEditorContext(blockDispose: false);
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
        var context = new BlockingEditorContext(blockDispose: false);
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
        var context = new BlockingEditorContext(blockDispose: false);
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
        var context = new BlockingEditorContext(blockDispose: false, scene);
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
        var context = new BlockingEditorContext(blockDispose: false, scene);
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
        var context = new BlockingEditorContext(obj: scene);
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
        var original = new BlockingEditorContext(blockDispose: false, scene);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(original);
        service.AddTabItem(tab);
        bool? replaced = null;
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
            Assert.That(replaced, Is.False);
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
            var context = new GatedEditorContext(scene);
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
        var tab = new EditorTabItem(new BlockingEditorContext(blockDispose: false));
        service.AddTabItem(tab);
        var replacement = new GatedEditorContext(new Scene(16, 16, string.Empty));
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

        Task<bool> replace = Task.Run(async () => await tab.ReplaceContextAsync(replacement));
        await replacement.PublicationPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(service.RequestTabRemoval(tab), Is.False);
        Assert.That(service.ContainsTabItem(tab), Is.True,
            "Physical removal waits for the admitted replacement publication to drain.");
        replacement.ReleasePublication();

        Assert.That(await replace.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
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
        var oldContext = new BlockingEditorContext();
        var newContext = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);

        Task close = tab.DisposeAsync().AsTask();
        Task<bool> replacement = tab.ReplaceContextAsync(newContext).AsTask();

        oldContext.ReleaseDispose();
        Assert.That(await replacement.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        await newContext.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(newContext.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SameInstanceReplacementIsRejectedWithoutConsumption()
    {
        var service = new EditorService(new ExtensionProvider());
        var context = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(context);
        service.AddTabItem(tab);

        bool replaced = await tab.ReplaceContextAsync(context)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.False);
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

        bool replaced = await tab.ReplaceContextAsync(context)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.False);
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
        var firstContext = new BlockingEditorContext(blockDispose: false);
        var secondContext = new BlockingEditorContext(blockDispose: false);
        var first = new EditorTabItem(firstContext);
        var second = new EditorTabItem(secondContext);
        service.AddTabItem(first);
        service.AddTabItem(second);

        bool replaced = await first.ReplaceContextAsync(secondContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.False);
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
        var closingContext = new BlockingEditorContext();
        var otherContext = new BlockingEditorContext(blockDispose: false);
        var closing = new EditorTabItem(closingContext);
        var other = new EditorTabItem(otherContext);
        service.AddTabItem(closing);
        service.AddTabItem(other);

        Task close = service.CloseTabItem(closing).AsTask();
        await closingContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool replaced = await closing.ReplaceContextAsync(otherContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(replaced, Is.False);
        Assert.That(otherContext.DisposeCount, Is.Zero);
        closingContext.ReleaseDispose();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        await service.CloseTabItem(other).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RejectedReplacementReservationRemainsInvisibleUntilDisposalCompletes()
    {
        var service = new EditorService(new ExtensionProvider());
        var closingContext = new BlockingEditorContext();
        var rejectedContext = new BlockingEditorContext();
        var closing = new EditorTabItem(closingContext);
        service.AddTabItem(closing);

        Task close = service.CloseTabItem(closing).AsTask();
        await closingContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<bool> replace = closing.ReplaceContextAsync(rejectedContext).AsTask();
        await rejectedContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(
            service.RequestClose(rejectedContext).Status,
            Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
        var competing = new EditorTabItem(rejectedContext);
        Assert.That(service.TryAddTabItem(competing), Is.False);

        rejectedContext.ReleaseDispose();
        Assert.That(await replace.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        closingContext.ReleaseDispose();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(rejectedContext.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TransitioningTabCannotConsumeAnotherTabsContext()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext();
        var replacement = new BlockingEditorContext(blockDispose: false);
        var otherContext = new BlockingEditorContext(blockDispose: false);
        var transitioning = new EditorTabItem(oldContext);
        var other = new EditorTabItem(otherContext);
        service.AddTabItem(transitioning);
        service.AddTabItem(other);

        Task<bool> firstReplacement = transitioning.ReplaceContextAsync(replacement).AsTask();
        await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool rejected = await transitioning.ReplaceContextAsync(otherContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(rejected, Is.False);
        Assert.That(otherContext.DisposeCount, Is.Zero);
        oldContext.ReleaseDispose();
        Assert.That(await firstReplacement.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        await service.CloseTabItem(transitioning).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await service.CloseTabItem(other).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ReservedReplacementIdentityCanRequestOwningTabClose()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        EditorContextCloseRequest closeRequest = default;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null)
                closeRequest = service.RequestClose(replacement);
        });

        bool replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.False);
            Assert.That(closeRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StaleContextCloseGenerationCannotCloseReplacement()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
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
            await tab.ReplaceContextAsync(replacement).AsTask().WaitAsync(TimeSpan.FromSeconds(5)),
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
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
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

        bool replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replaced, Is.False);
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
    public async Task HostOwnedOutgoingDisposalDoesNotRequestTabClose()
    {
        var service = new EditorService(new ExtensionProvider());
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        service.AddTabItem(tab);
        oldContext.OnDispose = () =>
        {
            service.RequestContextShutdown(oldContext);
            return ValueTask.CompletedTask;
        };

        bool replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.True);
            Assert.That(service.TabItems, Does.Contain(tab));
            Assert.That(tab.Context.Value, Is.SameAs(replacement));
        });
        await service.CloseTabItem(tab).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RejectedReplacementDisposalFailureIsReportedAfterCleanup()
    {
        var oldContext = new BlockingEditorContext();
        var replacement = new ThrowingEditorContext();
        var tab = new EditorTabItem(oldContext);

        Task close = tab.DisposeAsync().AsTask();
        Task<bool> replace = tab.ReplaceContextAsync(replacement).AsTask();

        Assert.CatchAsync<InvalidOperationException>(async () =>
            await replace.WaitAsync(TimeSpan.FromSeconds(5)));
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
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var newContext = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        var service = new EditorService(new ExtensionProvider());
        service.AddTabItem(tab);
        EditorContextCloseRequest closeRequest = default;
        oldContext.OnDispose = () =>
        {
            closeRequest = service.RequestClose(oldContext);
            return ValueTask.CompletedTask;
        };

        bool replaced = await tab.ReplaceContextAsync(newContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.False);
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(newContext.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ContextPropertyPublishesNullOnlyAfterTerminalClose()
    {
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var newContext = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        var values = new List<IEditorContext?>();
        using IDisposable subscription = tab.Context.Subscribe(values.Add);

        Assert.That(await tab.ReplaceContextAsync(newContext).AsTask(), Is.True);
        await tab.DisposeAsync();

        Assert.That(values, Does.Contain(null));
    }

    [Test]
    public async Task FailedReplacementTerminallyRemovesTabAndClearsSelection()
    {
        var oldContext = new ThrowingEditorContext();
        var tab = new EditorTabItem(oldContext);
        var service = new EditorService(new ExtensionProvider());
        service.AddTabItem(tab);
        service.SelectedTabItem.Value = tab;
        var replacement = new BlockingEditorContext(blockDispose: false);
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
        var oldContext = new BlockingEditorContext();
        var fail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldContext.OnDispose = async () =>
        {
            await fail.Task;
            throw new InvalidOperationException("dispose failed");
        };
        var tab = new EditorTabItem(oldContext);
        var service = new EditorService(new ExtensionProvider());
        service.AddTabItem(tab);
        service.SelectedTabItem.Value = tab;

        Task replacement = tab.ReplaceContextAsync(new BlockingEditorContext(blockDispose: false)).AsTask();
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
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        Task? reentrantClose = null;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null)
                reentrantClose = tab.DisposeAsync().AsTask();
        });

        bool replaced = await tab.ReplaceContextAsync(new BlockingEditorContext(blockDispose: false))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replaced, Is.False);
        if (reentrantClose is not null)
            await reentrantClose.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ContextSubscriberCanRequestHostCloseWhenReplacementPublishesNull()
    {
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        var service = new EditorService(new ExtensionProvider());
        service.AddTabItem(tab);
        EditorContextCloseRequest closeRequest = default;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null)
                closeRequest = service.RequestClose(oldContext);
        });

        bool replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.False);
            Assert.That(closeRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CloseStartedByReplacementPublicationWinsTheResult()
    {
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        Task? close = null;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (ReferenceEquals(value, replacement))
                close = tab.DisposeAsync().AsTask();
        });

        bool replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replaced, Is.False);
        Assert.That(close, Is.Not.Null);
        await close!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replacement.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ContextSubscriberCannotStartACompetingReplacement()
    {
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var nested = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        Task<bool>? nestedReplacement = null;
        using IDisposable subscription = tab.Context.Subscribe(value =>
        {
            if (value is null && nestedReplacement is null)
                nestedReplacement = tab.ReplaceContextAsync(nested).AsTask();
        });

        bool replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replaced, Is.True);
        Assert.That(nestedReplacement, Is.Not.Null);
        Assert.That(await nestedReplacement!.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
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
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext();
        var tab = new EditorTabItem(oldContext);
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
            await tab.ReplaceContextAsync(replacement).AsTask().WaitAsync(TimeSpan.FromSeconds(5)),
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
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var nested = new BlockingEditorContext(blockDispose: false);
        var third = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        Task<bool>? nestedReplacement = null;
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

        Assert.That(await tab.ReplaceContextAsync(replacement).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(await nestedReplacement!.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
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
        var oldContext = new BlockingEditorContext();
        var replacement = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        var service = new EditorService(new ExtensionProvider());
        service.AddTabItem(tab);
        EditorContextCloseRequest nestedClose = default;
        replacement.OnDispose = () =>
        {
            nestedClose = service.RequestClose(oldContext);
            return ValueTask.CompletedTask;
        };

        Task<bool> replace = tab.ReplaceContextAsync(replacement).AsTask();
        await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task close = service.CloseTabItem(tab).AsTask();
        oldContext.ReleaseDispose();

        Assert.That(await replace.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
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
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var nested = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        var service = new EditorService(new ExtensionProvider());
        service.AddTabItem(tab);
        Task<bool>? nestedReplacement = null;
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

        bool replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.False);
            Assert.That(nestedReplacement, Is.Not.Null);
            Assert.That(nestedReplacement!.Result, Is.False);
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
            Assert.That(nested.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ThrowingContextSubscriberCannotStrandReplacement()
    {
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
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
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var replacement = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
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
        var context = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(context);
        var service = new EditorService(new ExtensionProvider());
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
        var firstContext = new BlockingEditorContext(blockDispose: false);
        var secondContext = new BlockingEditorContext(blockDispose: false);
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
        var firstContext = new BlockingEditorContext(blockDispose: false);
        var secondContext = new BlockingEditorContext(blockDispose: false);
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

        public BlockingEditorContext(bool blockDispose = true, CoreObject? obj = null)
        {
            _blockDispose = blockDispose;
            Object = obj ?? new Scene(16, 16, string.Empty);
        }

        public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; protected set; }

        public Func<ValueTask>? OnDispose { get; set; }

        public CoreObject Object { get; }

        public EditorExtension Extension { get; } = TestEditorExtension.Instance;

        public IEditorContextCloseService CloseService { get; } = new UnownedCloseService();

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
        public EditorContextCloseRequest RequestClose(IEditorContext context)
            => new(EditorContextCloseRequestStatus.NotOwned, Task.CompletedTask);
    }

    private sealed class GatedEditorContext(Scene scene) : BlockingEditorContext(false, scene), IEditorContextPublicationGate
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

    private sealed class ThrowingEditorContext : BlockingEditorContext
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
}

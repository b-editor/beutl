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
            shutdownRequested.TrySetResult();
            if (Interlocked.Exchange(ref disposeRequested, 1) == 0)
                service.RequestContextShutdown(context);
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

        Assert.That(await publish.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
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
        service.ClearTabItems();

        Assert.That(service.SelectedTabItem.Value, Is.Null);
        Assert.That(service.RemoveTabItem(tab), Is.False);
        Assert.That(service.SelectedTabItem.Value, Is.Null);
        await tab.DisposeAsync();
    }

    [Test]
    public async Task SelectionAndRemovalDoNotInvertContextPublicationGate()
    {
        var service = new EditorService(new ExtensionProvider());
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), "publication-gate.scene"))
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
        Task remove = Task.Run(() => service.RemoveTabItem(tab));
        context.ReleasePublication();

        await Task.WhenAll(
            activate.WaitAsync(TimeSpan.FromSeconds(5)),
            remove.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
        });
        await tab.DisposeAsync();
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
                tab.MutateLifetime(static () => { });
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
        Task remove = Task.Run(() => service.RemoveTabItem(tab));
        replacement.ReleasePublication();

        Assert.That(await replace.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        await remove.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(service.TabItems, Is.Empty);
            Assert.That(service.SelectedTabItem.Value, Is.Null);
        });
        await tab.DisposeAsync();
        Assert.That(replacement.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeAsync_PublishesOneSharedTaskBeforeContextTeardown()
    {
        var context = new BlockingEditorContext();
        var tab = new EditorTabItem(context);

        Task first = tab.DisposeAsync().AsTask();
        Task second = tab.DisposeAsync().AsTask();

        Assert.That(second, Is.SameAs(first));
        Assert.That(context.DisposeStarted.Task.IsCompleted, Is.True);

        context.ReleaseDispose();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
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
        oldContext.OnDispose = () => tab.DisposeAsync();

        bool replaced = await tab.ReplaceContextAsync(newContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

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
        replacement.OnDispose = () => tab.DisposeAsync();

        Task<bool> replace = tab.ReplaceContextAsync(replacement).AsTask();
        await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task close = tab.DisposeAsync().AsTask();
        oldContext.ReleaseDispose();

        Assert.That(await replace.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        await close.WaitAsync(TimeSpan.FromSeconds(5));
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
        Task<bool>? nestedReplacement = null;
        nested.OnDispose = () => tab.DisposeAsync();
        oldContext.OnDispose = async () =>
        {
            nestedReplacement = tab.ReplaceContextAsync(nested).AsTask();
            await nestedReplacement;
        };

        bool replaced = await tab.ReplaceContextAsync(replacement)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

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
        firstContext.OnDispose = () => service.CloseTabItem(second);

        await service.CloseTabItem(first).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

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
        firstContext.OnDispose = () => service.CloseTabItem(second);
        secondContext.OnDispose = () => service.CloseTabItem(first);

        await service.CloseTabItem(first).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await second.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(firstContext.DisposeCount, Is.EqualTo(1));
            Assert.That(secondContext.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
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

                publish();
                return true;
            }
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

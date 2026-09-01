using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class ToolTabHeaderTests
{
    private const int DefaultDisposalExtensionPackageId = 923_841;
    private const int RestoreReentryExtensionPackageId = 923_842;

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
    }

    [AvaloniaTest]
    public async Task Dockable_title_follows_the_context_header()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-follows");

        var context = new FakeToolContext("first");
        var dockable = new BeutlToolDockable(context, editor);

        Assert.That(dockable.Title, Is.EqualTo("first"));

        context.HeaderSource.Value = "second";
        Assert.That(dockable.Title, Is.EqualTo("second"));

        // Keep the source alive so this verifies the dockable's unsubscribe.
        await dockable.DisposeAsync();
        context.HeaderSource.Value = "after-dispose";
        Assert.Multiple(() =>
        {
            Assert.That(context.HeaderSource.Value, Is.EqualTo("after-dispose"));
            Assert.That(dockable.Title, Is.EqualTo("second"));
        });

        context.HeaderSource.Dispose();
    }

    [AvaloniaTest]
    public async Task Blank_header_falls_back_to_the_extension_metadata()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-blank");

        var context = new FakeToolContext(string.Empty);
        await using var dockable = new BeutlToolDockable(context, editor);

        Assert.That(dockable.Title, Is.EqualTo(FakeToolExtension.Instance.Header));

        context.HeaderSource.Value = "named";
        Assert.That(dockable.Title, Is.EqualTo("named"));

        context.HeaderSource.Value = string.Empty;
        Assert.That(dockable.Title, Is.EqualTo(FakeToolExtension.Instance.Header));
    }

    [AvaloniaTest]
    public async Task A_blank_extension_header_falls_back_to_the_display_name()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-blank-extension");

        var context = new FakeToolContext(string.Empty, BlankHeaderToolExtension.Instance);
        await using var dockable = new BeutlToolDockable(context, editor);

        Assert.Multiple(() =>
        {
            Assert.That(dockable.Title, Is.EqualTo(BlankHeaderToolExtension.Instance.DisplayName));
            Assert.That(dockable.Title, Is.Not.EqualTo(BlankHeaderToolExtension.Instance.Name));
        });
    }

    [AvaloniaTest]
    public async Task ToolDisposalCanReenterCloseWithoutWaitingOnItself()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-reentrant-close");
        var context = new FakeToolContext("reentrant");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        context.OnDispose = () => editor.CloseToolTabAsync(context);

        await editor.CloseToolTabAsync(context).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task LayoutTeardownAllowsOneToolToCloseAnotherWithoutParentCycle()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-reentrant-sibling-close");
        var first = new FakeToolContext("first");
        var second = new FakeToolContext("second");
        Assert.That(await editor.OpenToolTabAsync(first), Is.True);
        Assert.That(await editor.OpenToolTabAsync(second), Is.True);
        first.OnDispose = () => editor.CloseToolTabAsync(second);

        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task LayoutTeardownAllowsMutualToolCloseWithoutCycle()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-mutual-close");
        var first = new FakeToolContext("first");
        var second = new FakeToolContext("second");
        Assert.That(await editor.OpenToolTabAsync(first), Is.True);
        Assert.That(await editor.OpenToolTabAsync(second), Is.True);
        first.OnDispose = () => editor.CloseToolTabAsync(second);
        second.OnDispose = () => editor.CloseToolTabAsync(first);

        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task ToolDisposalCanRequestEditorDisposalWithoutParentSelfWait()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-reentrant-editor-dispose");
        var context = new FakeToolContext("editor dispose");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        context.OnDispose = () => editor.DisposeAsync();

        await editor.CloseToolTabAsync(context).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await editor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task PostAttachFailureRollsBackDockableAndDisposesContextOnce()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-post-attach-failure");
        var context = new FakeToolContext("post-attach");
        editor.DockHost.Factory.AfterToolAttach = _ => throw new InvalidOperationException("activation failed");

        Assert.That(await editor.OpenToolTabAsync(context), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(editor.DockHost.Factory.EnumerateTools(), Does.Not.Contain(
                Has.Property(nameof(BeutlToolDockable.ToolContext)).SameAs(context)));
            Assert.That(context.DisposeCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task UserCloseVetoKeepsTheDockableAndContextAlive()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-close-veto");
        var context = new FakeToolContext("close-veto");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => item.ToolContext == context);
        EventHandler<DockableClosingEventArgs> veto = (_, args) => args.Cancel = true;
        editor.DockHost.Factory.DockableClosing += veto;

        editor.DockHost.Factory.CloseDockable(dockable);

        editor.DockHost.Factory.DockableClosing -= veto;
        Assert.Multiple(() =>
        {
            Assert.That(editor.DockHost.Factory.EnumerateTools(), Does.Contain(dockable));
            Assert.That(dockable.Owner, Is.Not.Null);
            Assert.That(context.DisposeCount, Is.Zero);
        });
        await editor.CloseToolTabAsync(context);
    }

    [AvaloniaTest]
    public async Task ThrowingUserCloseCallbackDoesNotCorruptTheDockable()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-close-throw");
        var context = new FakeToolContext("close-throw");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => item.ToolContext == context);
        EventHandler<DockableClosingEventArgs> throwing = (_, _) =>
            throw new InvalidOperationException("close observer failed");
        editor.DockHost.Factory.DockableClosing += throwing;

        editor.DockHost.Factory.CloseDockable(dockable);

        editor.DockHost.Factory.DockableClosing -= throwing;
        Assert.Multiple(() =>
        {
            Assert.That(editor.DockHost.Factory.EnumerateTools(), Does.Contain(dockable));
            Assert.That(dockable.Owner, Is.Not.Null);
            Assert.That(context.DisposeCount, Is.Zero);
        });
        await editor.CloseToolTabAsync(context);
    }

    [AvaloniaTest]
    public async Task UserCloseObservesAContextDisposalFailure()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-user-close-dispose-failure");
        var context = new FakeToolContext("user-close-dispose-failure")
        {
            OnDispose = () => ValueTask.FromException(
                new InvalidOperationException("dispose failed")),
        };
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => item.ToolContext == context);
        Func<BeutlToolDockable, Task> tracker = editor.DockHost.Factory.DisposalTracker!;
        Task? tracked = null;
        editor.DockHost.Factory.DisposalTracker = item => tracked = tracker(item);

        editor.DockHost.Factory.CloseDockable(dockable);

        Assert.That(tracked, Is.Not.Null);
        Assert.CatchAsync<Exception>(async () =>
            await tracked!.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task LayoutResetBypassesThrowingCloseCallbacksAndCanRunAgain()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-reset-close-throw");
        var context = new FakeToolContext("reset-close-throw");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        EventHandler<DockableClosingEventArgs> throwing = (_, _) =>
            throw new InvalidOperationException("close observer failed");
        editor.DockHost.Factory.DockableClosing += throwing;

        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));

        editor.DockHost.Factory.DockableClosing -= throwing;
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task LayoutResetRemovesHiddenToolsBeforeDisposal()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-hidden-reset");
        var context = new FakeToolContext("hidden-reset");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => item.ToolContext == context);
        IRootDock oldRoot = editor.DockHost.Layout.Value;
        editor.DockHost.Factory.HideDockable(dockable);
        Assert.That(oldRoot.HiddenDockables, Does.Contain(dockable));

        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(oldRoot.HiddenDockables, Does.Not.Contain(dockable));
            Assert.That(dockable.Owner, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task ProgrammaticCloseRemovesAnEmptyFloatingWindow()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-floating-close");
        var context = new FakeToolContext("floating-close");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => item.ToolContext == context);
        IRootDock mainRoot = editor.DockHost.Layout.Value;
        editor.DockHost.Factory.FloatDockable(dockable);
        Assert.That(mainRoot.Windows, Is.Not.Null.And.Not.Empty);

        await editor.CloseToolTabAsync(context).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(mainRoot.Windows, Is.Empty);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(dockable.Owner, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task PartialDisposalPreparationCannotStrandLayoutTransition()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-partial-prepare");
        var first = new FakeToolContext("partial-first");
        var second = new FakeToolContext("partial-second");
        Assert.That(await editor.OpenToolTabAsync(first), Is.True);
        Assert.That(await editor.OpenToolTabAsync(second), Is.True);
        int preparations = 0;
        editor.DockHost.BeforePrepareDockableDisposal = _ =>
        {
            if (++preparations == 2)
                throw new InvalidOperationException("prepare failed");
        };

        Exception? resetError = null;
        try
        {
            await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            resetError = ex;
        }
        Assert.That(resetError, Is.TypeOf<InvalidOperationException>());
        editor.DockHost.BeforePrepareDockableDisposal = null;
        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task OpenStartedBeforeResetNeverAttachesToDetachedRoot()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-open-reset-race");
        var extension = new BlockingToolExtension();
        IToolDock oldTarget = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
        Task<bool> open = Task.Run(() => editor.DockHost.OpenToolTabFromExtensionAsync(extension, oldTarget));

        await extension.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));
        extension.Release();

        bool opened = await open.WaitAsync(TimeSpan.FromSeconds(5));
        var context = extension.CreatedContext!;
        Assert.That(context.DisposeCount, Is.EqualTo(opened ? 0 : 1));
        if (opened)
        {
            BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools()
                .Single(t => t.ToolContext == context);
            Assert.That(BeutlDockFactory.Traverse(editor.DockHost.Layout.Value), Does.Contain(dockable));
            Assert.That(BeutlDockFactory.Traverse(editor.DockHost.Layout.Value), Does.Not.Contain(oldTarget));
        }
    }

    [AvaloniaTest]
    public async Task ConcurrentCloseSharesOneInFlightDisposal()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-concurrent-close");
        var context = new FakeToolContext("concurrent-close");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnDispose = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        Task first = editor.CloseToolTabAsync(context).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task second = editor.CloseToolTabAsync(context).AsTask();
        Task<bool> reopen = editor.OpenToolTabAsync(context).AsTask();
        Assert.That(reopen.IsCompleted, Is.False,
            "A reopen of an in-flight disposal must join that disposal before it is rejected.");
        release.TrySetResult();
        await Task.WhenAll(first, second, reopen).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(reopen.Result, Is.False);
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(editor.DockHost.Factory.EnumerateTools(), Does.Not.Contain(
                Has.Property(nameof(BeutlToolDockable.ToolContext)).SameAs(context)));
        });
    }

    [AvaloniaTest]
    public async Task ReentrantOpenOfSameContextDoesNotSelfWait()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-reentrant-open");
        var context = new FakeToolContext("reentrant-open");
        context.OnDispose = async () => { await editor.OpenToolTabAsync(context).AsTask(); };
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        await editor.CloseToolTabAsync(context).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task MutualReentrantOpenDuringDisposalDoesNotCycle()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-mutual-reentrant-open");
        var first = new FakeToolContext("mutual-open-first");
        var second = new FakeToolContext("mutual-open-second");
        Assert.That(await editor.OpenToolTabAsync(first), Is.True);
        Assert.That(await editor.OpenToolTabAsync(second), Is.True);
        first.OnDispose = async () =>
        {
            Assert.That(await editor.OpenToolTabAsync(second), Is.False);
        };
        second.OnDispose = async () =>
        {
            Assert.That(await editor.OpenToolTabAsync(first), Is.False);
        };

        await editor.CloseToolTabAsync(first).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await editor.CloseToolTabAsync(second).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task ReentrantOpenOfFreshContextDuringResetStillConsumesIt()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-reset-fresh-reentrant");
        var outgoing = new FakeToolContext("reset-outgoing");
        var fresh = new FakeToolContext("reset-fresh");
        Assert.That(await editor.OpenToolTabAsync(outgoing), Is.True);
        outgoing.OnDispose = async () =>
        {
            Assert.That(await editor.OpenToolTabAsync(fresh), Is.False);
        };

        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(fresh.DisposeCount, Is.EqualTo(1));
        Assert.That(editor.DockHost.Factory.EnumerateTools(), Does.Not.Contain(
            Has.Property(nameof(BeutlToolDockable.ToolContext)).SameAs(fresh)));
    }

    [AvaloniaTest]
    public async Task DockableCleanupFailureStillDisposesAndReleasesItsContext()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-cleanup-failure");
        var context = new FakeToolContext("cleanup-failure");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => item.ToolContext == context);
        void ThrowOnContextClear(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(IDockable.Context))
                throw new InvalidOperationException("observer failed");
        }
        dockable.PropertyChanged += ThrowOnContextClear;

        Assert.ThrowsAsync<AggregateException>(async () =>
            await dockable.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        dockable.PropertyChanged -= ThrowOnContextClear;
        editor.DockHost.Factory.DetachDockable(dockable);

        Assert.That(context.DisposeCount, Is.EqualTo(1));
        Assert.Throws<ObjectDisposedException>(() => _ = dockable.ToolContext);
    }

    [AvaloniaTest]
    public async Task CompletedDisposalDoesNotRetainContextOrRedisposeLateClose()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-disposal-weak-retention");
        (WeakReference contextWeak, WeakReference dockableWeak) = await Task.Run(() => OpenAndClose(editor));

        ForceGc();
        Assert.Multiple(() =>
        {
            Assert.That(contextWeak.IsAlive, Is.False,
                "the dock host must not strongly retain a disposed context");
            Assert.That(dockableWeak.IsAlive, Is.False,
                "the dock host must not strongly retain a detached dockable");
        });

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static (WeakReference Context, WeakReference Dockable) OpenAndClose(EditViewModel editor)
        {
            var context = new FakeToolContext("weak-retention");
            Assert.That(editor.OpenToolTabAsync(context).AsTask().GetAwaiter().GetResult(), Is.True);
            BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools()
                .Single(item => item.ToolContext == context);
            editor.DockHost.Factory.InitLayout(editor.DockHost.Layout.Value);
            WeakReference contextWeak = new(context);
            WeakReference dockableWeak = new(dockable);
            editor.CloseToolTabAsync(context).AsTask().GetAwaiter().GetResult();
            editor.CloseToolTabAsync(context).AsTask().GetAwaiter().GetResult();
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(editor.DockHost.Factory.EnumerateTools(), Does.Not.Contain(
                Has.Property(nameof(BeutlToolDockable.ToolContext)).SameAs(context)));
            Assert.That(editor.DockHost.Factory.CurrentDockable, Is.Not.SameAs(dockable));
            Assert.That(dockable.Owner, Is.Null);
            Assert.That(dockable.OriginalOwner, Is.Null);
            Assert.That(dockable.Context, Is.Null);
            Assert.That(editor.DockHost.Factory.ToolControls.ContainsKey(dockable), Is.False);
            Assert.That(editor.DockHost.Factory.VisibleDockableControls.ContainsKey(dockable), Is.False);
            Assert.That(editor.DockHost.Factory.PinnedDockableControls.ContainsKey(dockable), Is.False);
            Assert.That(editor.DockHost.Factory.TabDockableControls.ContainsKey(dockable), Is.False);
            context.HeaderSource.Dispose();
            context = null!;
            dockable = null!;
            return (contextWeak, dockableWeak);
        }

        static void ForceGc()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(10);
            }
        }
    }

    [AvaloniaTest]
    public async Task ExternalOperationsCannotInterleaveDefaultTabMaterialization()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-default-materialization");
        var extension = new DefaultToolExtension();
        editor.DockHost.DefaultExtensionsOverride = [extension];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.DockHost.BeforeDefaultTabMaterializationAsync = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        Task reset = editor.DockHost.ResetLayoutAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        IRootDock transitionRoot = editor.DockHost.Layout.Value;

        await editor.DockHost.ResetLayoutAsync();
        Assert.That(editor.DockHost.Layout.Value, Is.SameAs(transitionRoot));

        var external = new FakeToolContext("external-during-defaults");
        Assert.That(await editor.OpenToolTabAsync(external), Is.False);
        Assert.That(external.DisposeCount, Is.EqualTo(1));

        release.TrySetResult();
        await reset.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaTest]
    public async Task DefaultContextCanSynchronouslyRequestEditorDisposalWithoutDeadlock()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-default-dispose-reentry");
        EditorTabItem ownerTab = TestShell.Editor.SelectedTabItem.Value!;
        var extension = new DisposingDefaultToolExtension(disposeCalls: 2);
        editor.DockHost.DefaultExtensionsOverride = [extension];

        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await editor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(extension.ContextCreationCount, Is.EqualTo(1));
            Assert.That(extension.CreatedContext?.DisposeCount, Is.EqualTo(1));
            Assert.That(TestShell.Editor.TabItems, Does.Not.Contain(ownerTab));
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.Not.SameAs(ownerTab));
        });
    }

    [AvaloniaTest]
    public async Task DefaultContextStartedForClosingEditorIsRejectedAndConsumed()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-default-dispose-admission");
        var extension = new DisposingDefaultToolExtension(waitForDisposal: false);
        editor.DockHost.DefaultExtensionsOverride = [extension];

        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await editor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(extension.ContextCreationCount, Is.EqualTo(1));
            Assert.That(extension.CreatedContext?.DisposeCount, Is.EqualTo(1));
            Assert.That(editor.DockHost.Factory.EnumerateTools(), Does.Not.Contain(
                Has.Property(nameof(BeutlToolDockable.ToolContext))
                    .SameAs(extension.CreatedContext)));
        });
    }

    [AvaloniaTest]
    public async Task InitialDefaultContextCanSynchronouslyRequestEditorDisposal()
    {
        await TestReset.ResetShellAsync();
        var extension = new DisposingDefaultToolExtension();
        TestShell.Extensions.AddExtensions(
            DefaultDisposalExtensionPackageId,
            [extension]);
        try
        {
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "tooltab-initial-default-dispose",
                NewWorkspace("tooltab-initial-default-dispose")))!;
            HeadlessTestHelpers.Settle();
            Scene scene = project.Items.OfType<Scene>().First();

            Assert.Multiple(() =>
            {
                Assert.That(extension.ContextCreationCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(extension.CreatedContexts, Is.All.Matches<FakeToolContext>(
                    context => context.DisposeCount == 1));
                Assert.That(TestShell.Editor.TryGetTabItem(scene, out _), Is.False);
            });
        }
        finally
        {
            _ = TestShell.Extensions.RemoveExtensions(DefaultDisposalExtensionPackageId);
        }
    }

    [AvaloniaTest]
    public async Task RestoredContextCanSynchronouslyRequestEditorDisposalDuringCreation()
    {
        await TestReset.ResetShellAsync();
        var extension = new RestoreReentryToolExtension();
        TestShell.Extensions.AddExtensions(RestoreReentryExtensionPackageId, [extension]);
        try
        {
            EditViewModel editor = await OpenEditorForNewScene("tooltab-restore-create-reentry");
            Assert.That(await editor.DockHost.OpenToolTabFromExtensionAsync(
                extension,
                editor.DockHost.Factory.FindFirstToolDock()), Is.True);
            JsonObject layout = editor.DockHost.CaptureLayout();
            extension.DisposeInCreate = true;

            Task<bool> restore = editor.DockHost.ApplyLayoutAsync(layout);
            Assert.That(await restore.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
            await editor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(editor.DockHost.FindToolContext(extension.GetType()), Is.Null);
        }
        finally
        {
            _ = TestShell.Extensions.RemoveExtensions(RestoreReentryExtensionPackageId);
        }
    }

    [AvaloniaTest]
    public async Task RestoredContextCanSynchronouslyRequestEditorDisposalDuringStateRead()
    {
        await TestReset.ResetShellAsync();
        var extension = new RestoreReentryToolExtension();
        TestShell.Extensions.AddExtensions(RestoreReentryExtensionPackageId, [extension]);
        try
        {
            EditViewModel editor = await OpenEditorForNewScene("tooltab-restore-read-reentry");
            Assert.That(await editor.DockHost.OpenToolTabFromExtensionAsync(
                extension,
                editor.DockHost.Factory.FindFirstToolDock()), Is.True);
            var saved = new JsonObject();
            editor.DockHost.WriteToJson(saved);
            extension.DisposeInRead = true;

            Task<bool> restore = editor.DockHost.ReadFromJsonAsync(saved);
            Assert.That(await restore.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
            await editor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(editor.DockHost.FindToolContext(extension.GetType()), Is.Null);
        }
        finally
        {
            _ = TestShell.Extensions.RemoveExtensions(RestoreReentryExtensionPackageId);
        }
    }

    [AvaloniaTest]
    public async Task RestoredContextThatClosesItselfIsNotPublished()
    {
        await TestReset.ResetShellAsync();
        var extension = new RestoreReentryToolExtension();
        TestShell.Extensions.AddExtensions(RestoreReentryExtensionPackageId, [extension]);
        try
        {
            EditViewModel editor = await OpenEditorForNewScene("tooltab-restore-self-close");
            Assert.That(await editor.DockHost.OpenToolTabFromExtensionAsync(
                extension,
                editor.DockHost.Factory.FindFirstToolDock()), Is.True);
            var saved = new JsonObject();
            editor.DockHost.WriteToJson(saved);
            extension.CloseInRead = true;

            Assert.That(await editor.DockHost.ReadFromJsonAsync(saved)
                .WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(editor.DockHost.FindToolContext(extension.GetType()), Is.Null);
            await editor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _ = TestShell.Extensions.RemoveExtensions(RestoreReentryExtensionPackageId);
        }
    }

    [AvaloniaTest]
    public async Task PublicationCloseCallbackDoesNotDeadlockOrPublishDisposedTool()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-publication-close-reentry");
        var extension = new RestoreReentryToolExtension();
        TestShell.Extensions.AddExtensions(RestoreReentryExtensionPackageId, [extension]);
        try
        {
            Assert.That(await editor.DockHost.OpenToolTabFromExtensionAsync(
                extension,
                editor.DockHost.Factory.FindFirstToolDock()), Is.True);
            var saved = new JsonObject();
            editor.DockHost.WriteToJson(saved);
            editor.DockHost.BeforeLayoutPublication = restored =>
            {
                BeutlToolDockable? restoredTool = BeutlDockFactory.Traverse(restored)
                    .OfType<BeutlToolDockable>()
                    .FirstOrDefault(tool => tool.ToolContext.Extension == extension);
                if (restoredTool?.ToolContext is { } context)
                    editor.CloseToolTabAsync(context).AsTask().GetAwaiter().GetResult();
            };

            Assert.That(await editor.DockHost.ReadFromJsonAsync(saved)
                .WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(editor.DockHost.FindToolContext(extension.GetType()), Is.Null);
            await editor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            editor.DockHost.BeforeLayoutPublication = null;
            _ = TestShell.Extensions.RemoveExtensions(RestoreReentryExtensionPackageId);
        }
    }

    [AvaloniaTest]
    public async Task DeferredPublicationCloseAttemptsEveryContextAfterDisposalFailures()
    {
        await TestReset.ResetShellAsync();
        var extension = new RestoreReentryToolExtension();
        TestShell.Extensions.AddExtensions(RestoreReentryExtensionPackageId, [extension]);
        try
        {
            EditViewModel editor = await OpenEditorForNewScene("tooltab-publication-close-batch");
            Assert.That(await editor.DockHost.OpenToolTabFromExtensionAsync(
                extension,
                editor.DockHost.Factory.FindFirstToolDock()), Is.True);
            Assert.That(await editor.DockHost.OpenToolTabFromExtensionAsync(
                extension,
                editor.DockHost.Factory.FindFirstToolDock()), Is.True);
            var saved = new JsonObject();
            editor.DockHost.WriteToJson(saved);
            int previousContexts = extension.CreatedContexts.Count;
            extension.ThrowOnDispose = true;
            editor.DockHost.BeforeLayoutPublication = restored =>
            {
                foreach (BeutlToolDockable tool in BeutlDockFactory.Traverse(restored)
                    .OfType<BeutlToolDockable>()
                    .Where(tool => tool.ToolContext.Extension == extension))
                {
                    editor.CloseToolTabAsync(tool.ToolContext).AsTask().GetAwaiter().GetResult();
                }
            };

            Assert.That(await editor.DockHost.ReadFromJsonAsync(saved)
                .WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            FakeToolContext[] restoredContexts = extension.CreatedContexts
                .Skip(previousContexts)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(restoredContexts, Has.Length.EqualTo(2));
                Assert.That(restoredContexts, Has.All.Matches<FakeToolContext>(context => context.DisposeCount == 1));
                Assert.That(editor.DockHost.FindToolContext(extension.GetType()), Is.Null);
            });
            await editor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _ = TestShell.Extensions.RemoveExtensions(RestoreReentryExtensionPackageId);
        }
    }

    [AvaloniaTest]
    public async Task DelayedOwnerCallbackCloseAfterApplyUsesNormalAdmission()
    {
        await TestReset.ResetShellAsync();
        var extension = new RestoreReentryToolExtension
        {
            DelayCloseFromRead = true,
        };
        TestShell.Extensions.AddExtensions(RestoreReentryExtensionPackageId, [extension]);
        try
        {
            EditViewModel editor = await OpenEditorForNewScene("tooltab-delayed-owner-close");
            Assert.That(await editor.DockHost.OpenToolTabFromExtensionAsync(
                extension,
                editor.DockHost.Factory.FindFirstToolDock()), Is.True);
            var saved = new JsonObject();
            editor.DockHost.WriteToJson(saved);

            Assert.That(await editor.DockHost.ReadFromJsonAsync(saved)
                .WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            await extension.DelayedCloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            extension.ReleaseDelayedClose.TrySetResult();
            await extension.DelayedCloseCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(editor.DockHost.FindToolContext(extension.GetType()), Is.Null);
            await editor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            extension.ReleaseDelayedClose.TrySetResult();
            _ = TestShell.Extensions.RemoveExtensions(RestoreReentryExtensionPackageId);
        }
    }

    [AvaloniaTest]
    public async Task ReplacementEditContextDisposedBeforePublicationIsRejected()
    {
        await TestReset.ResetShellAsync();
        EditViewModel owner = await OpenEditorForNewScene("tooltab-replacement-admission");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Block the old context through a replacement callback while the new EditViewModel is
        // disposed before its publication reservation can be consumed.
        var replacementScene = new Scene(16, 16, "replacement-admission")
        {
            Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, "replacement-admission.scene"))
        };
        Assert.That(SceneEditorExtension.Instance.TryCreateContext(
            replacementScene,
            new EditorContextServices(TestShell.Editor, owner.ExtensionProvider),
            out IEditorContext? replacementContext), Is.True);
        var replacement = (EditViewModel)replacementContext!;
        var oldBlocking = new BlockingEditorContext(entered, release);
        var blockingTab = new EditorTabItem(oldBlocking);
        TestShell.Editor.AddTabItem(blockingTab);
        Task<bool> replace = blockingTab.ReplaceContextAsync(replacement).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _ = replacement.DisposeAsync();
        release.TrySetResult();

        Assert.That(await replace.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        Assert.That(blockingTab.Context.Value, Is.Null);
        await blockingTab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaTest]
    public async Task ShutdownAdmissionRejectsResetAndApplyWhileDisposalDrains()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-shutdown-admission");
        var context = new FakeToolContext("shutdown-block");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnDispose = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        JsonObject layout = editor.DockHost.CaptureLayout();
        Task dispose = editor.DisposeAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task reset = editor.DockHost.ResetLayoutAsync();
        Assert.That(reset.IsCompleted, Is.True);
        await reset.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(await editor.DockHost.ApplyLayoutAsync(layout).WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        release.TrySetResult();
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaTest]
    public async Task DefaultFactoryCanSynchronouslyCloseEarlierDefaultWithoutDeadlock()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-default-close-reentry");
        var first = new TrackedDefaultToolExtension("first", 0);
        var second = new ClosingDefaultToolExtension(() => first.CreatedContext, 1);
        editor.DockHost.DefaultExtensionsOverride = [first, second];

        await editor.DockHost.ResetLayoutAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(first.CreatedContext?.DisposeCount, Is.EqualTo(1));
            Assert.That(second.CreatedContext?.DisposeCount, Is.Zero);
            Assert.That(editor.DockHost.Factory.EnumerateTools(), Has.One.Matches<BeutlToolDockable>(
                tool => ReferenceEquals(tool.ToolContext, second.CreatedContext)));
        });
    }

    [AvaloniaTest]
    public async Task ExtensionReturningFalseWithContextIsDisposed()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-false-context");
        var extension = new FalseWithContextToolExtension();

        bool opened = await editor.DockHost.OpenToolTabFromExtensionAsync(
            extension,
            editor.DockHost.Factory.FindFirstToolDock());

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.False);
            Assert.That(extension.CreatedContext, Is.Not.Null);
            Assert.That(extension.CreatedContext!.DisposeCount, Is.EqualTo(1));
            Assert.That(editor.DockHost.Factory.EnumerateTools(), Does.Not.Contain(
                Has.Property(nameof(BeutlToolDockable.ToolContext)).SameAs(extension.CreatedContext)));
        });
    }

    [AvaloniaTest]
    public async Task SnapshotDuringTransitionIsRejected()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-stable-snapshot");
        var context = new FakeToolContext("stable-snapshot");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnDispose = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        Task reset = editor.DockHost.ResetLayoutAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var during = new JsonObject();

        Assert.Throws<InvalidOperationException>(() => editor.DockHost.WriteToJson(during));
        Assert.Throws<InvalidOperationException>(() => editor.DockHost.CaptureLayout());
        release.TrySetResult();
        await reset.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaTest]
    public async Task EditorCloseWaitsForLayoutTransitionAndStillTearsDown()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-close-transition");
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        var context = new FakeToolContext("close-transition");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnDispose = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        Task reset = editor.DockHost.ResetLayoutAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task close = TestShell.Editor.CloseTabItem(tab).AsTask();
        Assert.That(close.IsCompleted, Is.False);
        release.TrySetResult();

        await reset.WaitAsync(TimeSpan.FromSeconds(5));
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(TestShell.Editor.TabItems, Does.Not.Contain(tab));
        });
    }

    [AvaloniaTest]
    public async Task ToolSerializerRunsOutsideHostGates()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-serializer-reentry");
        var context = new FakeToolContext("serializer-reentry");
        Assert.That(await editor.OpenToolTabAsync(context), Is.True);
        bool serializedOnUiThread = false;
        context.OnWrite = _ => serializedOnUiThread =
            Avalonia.Threading.Dispatcher.UIThread.CheckAccess();

        var json = new JsonObject();
        Task serialization = Task.Run(() => editor.DockHost.WriteToJson(json));
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!serialization.IsCompleted && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Assert.That(serialization.IsCompleted, Is.True);
        serialization.GetAwaiter().GetResult();
        Assert.That(serializedOnUiThread, Is.True);
        Assert.That(json["DockLayout"], Is.Not.Null);
        context.OnWrite = null;
        await editor.CloseToolTabAsync(context);
    }

    private sealed class FakeToolContext(string header, ToolTabExtension? extension = null) : IToolContext
    {
        public ReactivePropertySlim<string> HeaderSource { get; } = new(header);

        public ToolTabExtension Extension { get; } = extension ?? FakeToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header => HeaderSource;

        public Func<ValueTask>? OnDispose { get; set; }

        public Action<JsonObject>? OnWrite { get; set; }

        public Action<JsonObject>? OnRead { get; set; }

        public int DisposeCount { get; private set; }

        // Keep HeaderSource alive to test the dockable's unsubscribe.
        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (OnDispose is { } onDispose)
                await onDispose();
            IsSelected.Dispose();
        }

        public object? GetService(Type serviceType) => null;

        public void ReadFromJson(JsonObject json)
        {
            OnRead?.Invoke(json);
        }

        public void WriteToJson(JsonObject json)
        {
            OnWrite?.Invoke(json);
        }
    }

    private sealed class BlockingEditorContext(
        TaskCompletionSource entered,
        TaskCompletionSource release) : IEditorContext
    {
        public CoreObject Object { get; } = new Scene(16, 16, "blocking");
        public EditorExtension Extension => SceneEditorExtension.Instance;
        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);
        public IKnownEditorCommands? Commands => null;
        public async ValueTask DisposeAsync()
        {
            entered.TrySetResult();
            await release.Task;
        }
        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;
        public T? FindToolTab<T>() where T : IToolContext => default;
        public ValueTask<bool> OpenToolTabAsync(IToolContext item) => new(false);
        public ValueTask CloseToolTabAsync(IToolContext item) => ValueTask.CompletedTask;
        public object? GetService(Type serviceType) => null;
    }

    private class BlockingToolExtension : ToolTabExtension
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeToolContext? CreatedContext { get; private set; }

        public void Release() => _release.TrySetResult();

        public override bool CanMultiple => true;
        public override string Name => "BlockingToolTab";
        public override string DisplayName => "Blocking tool tab";
        public override string? Header => "Blocking tool tab";

        public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
        {
            Entered.TrySetResult();
            _release.Task.GetAwaiter().GetResult();
            context = CreatedContext = new FakeToolContext("blocking", this);
            return true;
        }
    }

    private sealed class DefaultToolExtension : ToolTabExtension
    {
        public override bool CanMultiple => true;
        public override string Name => "DefaultToolTab";
        public override string DisplayName => "Default tool tab";
        public override string? Header => "Default tool tab";
        public override bool OpenByDefault => true;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = new FakeToolContext("default", this);
            return true;
        }
    }

    private sealed class RestoreReentryToolExtension : ToolTabExtension
    {
        public bool DisposeInCreate { get; set; }
        public bool DisposeInRead { get; set; }
        public bool CloseInRead { get; set; }
        public bool DelayCloseFromRead { get; set; }
        public bool ThrowOnDispose { get; set; }
        public List<FakeToolContext> CreatedContexts { get; } = [];
        public TaskCompletionSource DelayedCloseStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDelayedClose { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DelayedCloseCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanMultiple => true;
        public override string Name => "RestoreReentryToolTab";
        public override string DisplayName => "Restore reentry tool tab";
        public override string? Header => "Restore reentry tool tab";

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            if (DisposeInCreate)
                editorContext.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var created = new FakeToolContext("restore reentry", this);
            CreatedContexts.Add(created);
            if (ThrowOnDispose)
            {
                created.OnDispose = () => ValueTask.FromException(
                    new InvalidOperationException("deferred close failed"));
            }
            if (DisposeInRead)
                created.OnRead = _ => editorContext.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (CloseInRead)
                created.OnRead = _ => editorContext.CloseToolTabAsync(created).AsTask().GetAwaiter().GetResult();
            if (DelayCloseFromRead)
            {
                created.OnRead = _json =>
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            DelayedCloseStarted.TrySetResult();
                            await ReleaseDelayedClose.Task;
                            await editorContext.CloseToolTabAsync(created);
                            DelayedCloseCompleted.TrySetResult();
                        }
                        catch (Exception ex)
                        {
                            DelayedCloseCompleted.TrySetException(ex);
                        }
                    });
                };
            }
            context = created;
            return true;
        }
    }

    private sealed class DisposingDefaultToolExtension(
        bool waitForDisposal = true,
        int disposeCalls = 1) : ToolTabExtension
    {
        public int ContextCreationCount { get; private set; }

        public FakeToolContext? CreatedContext { get; private set; }

        public List<FakeToolContext> CreatedContexts { get; } = [];

        public override bool CanMultiple => true;
        public override string Name => "DisposingDefaultToolTab";
        public override string DisplayName => "Disposing default tool tab";
        public override string? Header => "Disposing default tool tab";
        public override bool OpenByDefault => true;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            ContextCreationCount++;
            for (int i = 0; i < disposeCalls; i++)
            {
                ValueTask disposal = editorContext.DisposeAsync();
                if (waitForDisposal)
                    disposal.AsTask().GetAwaiter().GetResult();
            }
            context = CreatedContext = new FakeToolContext("disposing-default", this);
            CreatedContexts.Add(CreatedContext);
            return true;
        }
    }

    private sealed class TrackedDefaultToolExtension(string suffix, int order) : ToolTabExtension
    {
        public FakeToolContext? CreatedContext { get; private set; }
        public override bool CanMultiple => true;
        public override string Name => $"TrackedDefault{suffix}";
        public override string DisplayName => Name;
        public override string? Header => Name;
        public override bool OpenByDefault => true;
        public override int DefaultOrder => order;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = CreatedContext = new FakeToolContext(Name, this);
            return true;
        }
    }

    private sealed class ClosingDefaultToolExtension(
        Func<FakeToolContext?> first,
        int order) : ToolTabExtension
    {
        public FakeToolContext? CreatedContext { get; private set; }
        public override bool CanMultiple => true;
        public override string Name => "ClosingDefault";
        public override string DisplayName => Name;
        public override string? Header => Name;
        public override bool OpenByDefault => true;
        public override int DefaultOrder => order;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            editorContext.CloseToolTabAsync(first()!).AsTask().GetAwaiter().GetResult();
            context = CreatedContext = new FakeToolContext(Name, this);
            return true;
        }
    }

    private sealed class FalseWithContextToolExtension : ToolTabExtension
    {
        public FakeToolContext? CreatedContext { get; private set; }

        public override bool CanMultiple => true;
        public override string Name => "FalseWithContext";
        public override string DisplayName => Name;
        public override string? Header => Name;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = null;
            return false;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = CreatedContext = new FakeToolContext(Name, this);
            return false;
        }
    }

    private sealed class BlankHeaderToolExtension : ToolTabExtension
    {
        public static readonly BlankHeaderToolExtension Instance = new();

        public override bool CanMultiple => true;

        public override string Name => "BlankHeaderToolTab";

        public override string DisplayName => "Blank header tool tab";

        public override string? Header => "   ";

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = new FakeToolContext(string.Empty, Instance);
            return true;
        }
    }

    private sealed class FakeToolExtension : ToolTabExtension
    {
        public static readonly FakeToolExtension Instance = new();

        public override bool CanMultiple => true;

        public override string Name => "FakeToolTab";

        public override string DisplayName => "Fake tool tab";

        public override string? Header => "Fake tool tab";

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = new FakeToolContext("Fake tool tab");
            return true;
        }
    }
}

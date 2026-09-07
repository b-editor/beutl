using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class EditorServiceTests
{
    [Test]
    public async Task Project_file_writes_and_worktree_mutations_are_mutually_exclusive()
    {
        var editorService = new EditorService(new ExtensionProvider());
        using (IProjectFileWriteLease fileWrite = await editorService.BeginProjectFileWriteAsync(
                   CancellationToken.None))
        {
            Assert.That(editorService.TryBeginWorktreeMutation(), Is.Null);
        }

        using (IDisposable worktreeMutation = editorService.TryBeginWorktreeMutation()!)
        {
            ValueTask<IProjectFileWriteLease> pendingWrite = editorService.BeginProjectFileWriteAsync(
                CancellationToken.None);
            Assert.That(pendingWrite.IsCompleted, Is.False);
            worktreeMutation.Dispose();
            using IProjectFileWriteLease fileWrite = await pendingWrite;
        }
    }

    [Test]
    public async Task Project_file_write_leases_are_serialized()
    {
        var editorService = new EditorService(new ExtensionProvider());
        IProjectFileWriteLease first = await editorService.BeginProjectFileWriteAsync(
            CancellationToken.None);
        ValueTask<IProjectFileWriteLease> second = editorService.BeginProjectFileWriteAsync(
            CancellationToken.None);

        Assert.That(second.IsCompleted, Is.False);

        first.Dispose();
        using IProjectFileWriteLease secondLease = await second;
    }

    [Test]
    public async Task A_completed_project_file_write_can_be_handed_to_a_worktree_mutation()
    {
        var editorService = new EditorService(new ExtensionProvider());
        IProjectFileWriteLease fileWrite = await editorService.BeginProjectFileWriteAsync(
            CancellationToken.None);

        IDisposable? mutation = editorService.TryBeginWorktreeMutation(fileWrite);
        Assert.That(
            mutation,
            Is.Not.Null,
            "The write lease must be foldable into the mutation that follows it.");

        // The handoff already consumed the write, so the caller's own dispose must not release the
        // workspace a second time.
        fileWrite.Dispose();
        Assert.That(editorService.TryBeginProjectFileWrite(), Is.Null);

        mutation!.Dispose();
        using IProjectFileWriteLease afterMutation = await editorService.BeginProjectFileWriteAsync(
            CancellationToken.None);
        Assert.That(afterMutation, Is.Not.Null);
    }

    [Test]
    public async Task Handing_over_a_project_file_write_releases_it_even_when_the_mutation_cannot_start()
    {
        var editorService = new EditorService(new ExtensionProvider());
        using IDisposable output = editorService.TryBeginOutputOperation()!;
        IProjectFileWriteLease fileWrite = await editorService.BeginProjectFileWriteAsync(
            CancellationToken.None);

        Assert.That(editorService.TryBeginWorktreeMutation(fileWrite), Is.Null);

        fileWrite.Dispose();
        using IProjectFileWriteLease next = await editorService.BeginProjectFileWriteAsync(
            CancellationToken.None);
        Assert.That(next, Is.Not.Null);
    }

    [Test]
    public void TryBeginWorktreeMutation_rejects_a_lease_it_did_not_issue()
    {
        var editorService = new EditorService(new ExtensionProvider());
        Assert.Throws<ArgumentException>(
            () => editorService.TryBeginWorktreeMutation(new ForeignLease()));
    }

    [Test]
    public async Task TryBeginProjectFileWrite_refuses_a_reserved_workspace()
    {
        var editorService = new EditorService(new ExtensionProvider());
        using (IDisposable mutation = editorService.TryBeginWorktreeMutation()!)
        {
            Assert.That(editorService.TryBeginProjectFileWrite(), Is.Null);
        }

        using (IProjectFileWriteLease held = await editorService.BeginProjectFileWriteAsync(
                   CancellationToken.None))
        {
            Assert.That(editorService.TryBeginProjectFileWrite(), Is.Null);
        }

        using IProjectFileWriteLease free = editorService.TryBeginProjectFileWrite()!;
        Assert.That(free, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task SaveProjectFilesAsync_requires_a_project_uri()
    {
        var editorService = new EditorService(new ExtensionProvider());
        var project = new Project();

        InvalidOperationException? exception = null;
        try
        {
            await editorService.SaveProjectFilesAsync(project, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        Assert.That(exception, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task SaveProjectFilesAsync_serializes_away_from_the_calling_thread()
    {
        int serializationThread = 0;
        var editorService = new EditorService(
            new ExtensionProvider(),
            (_, _) => serializationThread = Environment.CurrentManagedThreadId);
        var project = new Project { Uri = new Uri("file:///project.bep") };
        int callingThread = Environment.CurrentManagedThreadId;

        Assert.That(
            await editorService.SaveProjectFilesAsync(project, CancellationToken.None),
            Is.True);
        Assert.That(serializationThread, Is.Not.EqualTo(callingThread));
    }

    [AvaloniaTest]
    public async Task SaveProjectFilesAsync_skips_a_tab_whose_context_is_already_torn_down()
    {
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        var project = new Project { Uri = new Uri("file:///project.bep") };
        var tabItem = new EditorTabItem(new StubEditorContext());
        editorService.TabItems.Add(tabItem);
        await tabItem.DisposeAsync();

        Assert.That(
            await editorService.SaveProjectFilesAsync(project, CancellationToken.None),
            Is.True);
    }

    [Test]
    public async Task SwitchEditorExtensionAsync_disposes_the_outgoing_context_asynchronously()
    {
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        var outgoing = new StubEditorContext();
        var tabItem = new EditorTabItem(outgoing);
        editorService.TabItems.Add(tabItem);
        editorService.SelectedTabItem.Value = tabItem;
        var incoming = new StubEditorContext();

        await editorService.SwitchEditorExtensionAsync(new StubEditorExtension(incoming));

        Assert.Multiple(() =>
        {
            // IEditorContext.Dispose() has an empty default implementation, so a Dispose-based swap
            // would leave the outgoing editor running.
            Assert.That(outgoing.AsyncDisposeCount, Is.EqualTo(1));
            Assert.That(tabItem.Context.Value, Is.SameAs(incoming));
        });
    }

    [Test]
    public async Task SwitchEditorExtensionAsync_leaves_the_tab_alone_when_the_selection_moved()
    {
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        var outgoing = new StubEditorContext();
        var tabItem = new EditorTabItem(outgoing);
        var otherTab = new EditorTabItem(new StubEditorContext());
        editorService.TabItems.Add(tabItem);
        editorService.TabItems.Add(otherTab);
        editorService.SelectedTabItem.Value = tabItem;

        var extension = new StubEditorExtension(new StubEditorContext());
        // The selection moves while the write lease is held, standing in for a version-control
        // transition that reopens the project mid-wait.
        using (IDisposable mutation = editorService.TryBeginWorktreeMutation()!)
        {
            Task swap = editorService.SwitchEditorExtensionAsync(extension);
            editorService.SelectedTabItem.Value = otherTab;
            mutation.Dispose();
            await swap;
        }

        Assert.Multiple(() =>
        {
            Assert.That(tabItem.Context.Value, Is.SameAs(outgoing));
            Assert.That(outgoing.AsyncDisposeCount, Is.Zero);
            Assert.That(extension.CreateContextCalls, Is.Zero);
        });
    }

    [Test]
    public void SuspendEditors_keeps_editors_disabled_until_the_outermost_handle_is_disposed()
    {
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        var context = new StubEditorContext();
        editorService.TabItems.Add(new EditorTabItem(context));

        using (IDisposable outer = editorService.SuspendEditors())
        {
            Assert.That(context.IsEnabled.Value, Is.False);

            // A transition suspends around its pre-transition save, which suspends again; the inner
            // handle must not re-enable the editor while the transition is still running.
            using (IDisposable inner = editorService.SuspendEditors())
            {
                Assert.That(context.IsEnabled.Value, Is.False);
            }

            Assert.That(context.IsEnabled.Value, Is.False);
        }

        Assert.That(context.IsEnabled.Value, Is.True);
    }

    [Test]
    public void SuspendEditors_supports_out_of_order_disposal()
    {
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        var context = new StubEditorContext();
        editorService.TabItems.Add(new EditorTabItem(context));

        IDisposable first = editorService.SuspendEditors();
        IDisposable second = editorService.SuspendEditors();

        first.Dispose();
        Assert.That(context.IsEnabled.Value, Is.False);

        second.Dispose();
        Assert.That(context.IsEnabled.Value, Is.True);
    }

    [Test]
    public void SuspendEditors_releases_every_context_when_one_restore_observer_fails()
    {
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        var first = new StubEditorContext();
        var second = new StubEditorContext();
        editorService.TabItems.Add(new EditorTabItem(first));
        editorService.TabItems.Add(new EditorTabItem(second));
        IDisposable suspension = editorService.SuspendEditors();
        using IDisposable subscription = first.IsEnabled.Subscribe(value =>
        {
            if (value)
            {
                throw new InvalidOperationException("restore observer failed");
            }
        });

        Assert.Throws<AggregateException>(suspension.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsEnabled.Value, Is.True);
            Assert.That(second.IsEnabled.Value, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Output_and_save_suspensions_restore_the_editor_only_after_both_finish()
    {
        var serializationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowSerialization = new ManualResetEventSlim();
        var editorService = new EditorService(
            new ExtensionProvider(),
            (_, _) =>
            {
                serializationStarted.TrySetResult();
                allowSerialization.Wait();
            });
        var project = new Project { Uri = new Uri("file:///project.bep") };
        var context = new StubEditorContext();
        editorService.TabItems.Add(new EditorTabItem(context));
        IDisposable outputOperation = editorService.TryBeginOutputOperation()!;
        IDisposable outputSuspension = editorService.SuspendEditor(context);
        Task<bool> save = editorService.SaveProjectFilesAsync(project, CancellationToken.None);

        try
        {
            await serializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(context.IsEnabled.Value, Is.False);

            outputSuspension.Dispose();
            outputOperation.Dispose();

            Assert.That(
                context.IsEnabled.Value,
                Is.False,
                "The save still owns the shared editor suspension.");

            allowSerialization.Set();
            Assert.That(await save.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(context.IsEnabled.Value, Is.True);
        }
        finally
        {
            outputSuspension.Dispose();
            outputOperation.Dispose();
            allowSerialization.Set();
            await save.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ForeignLease : IProjectFileWriteLease
    {
        public void Dispose()
        {
        }
    }

    private sealed class StubEditorExtension(IEditorContext context) : EditorExtension
    {
        public int CreateContextCalls { get; private set; }

        public override FilePickerFileType GetFilePickerFileType() => throw new NotSupportedException();

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [NotNullWhen(true)] out IEditorContext? outContext)
        {
            CreateContextCalls++;
            outContext = context;
            return true;
        }

        public override bool MatchFileExtension(string ext) => true;
    }

    private sealed class StubEditorContext : IEditorContext
    {
        public int AsyncDisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return ValueTask.CompletedTask;
        }

        public CoreObject Object { get; } = new Scene { Uri = new Uri("file:///scene.scene") };

        public EditorExtension Extension => SceneEditorExtension.Instance;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public object? GetService(Type serviceType) => null;

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
            => default;

        public T? FindToolTab<T>()
            where T : IToolContext
            => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }
    }
}

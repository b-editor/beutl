using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
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
        using IDisposable output = editorService.BeginObservedOutputOperation();
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

    [Test]
    public async Task Clipboard_paste_reserves_the_workspace_through_the_model_update()
    {
        var editorService = new EditorService(new ExtensionProvider());
        var pasteStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePaste = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new BlockingClipboardService(pasteStarted, releasePaste.Task);
        var clipboard = new ProjectFileWriteClipboardService(editorService, inner);
        var scene = new Scene { Uri = new Uri("file:///project/main.scene") };

        Task<ElementPasteOutcome> paste = clipboard.PasteAsync(scene, TimeSpan.Zero, 0);
        await pasteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(editorService.TryBeginWorktreeMutation(), Is.Null);

        releasePaste.TrySetResult();
        Assert.That(
            (await paste.WaitAsync(TimeSpan.FromSeconds(5))).Pasted,
            Is.True);
        using IDisposable? mutation = editorService.TryBeginWorktreeMutation();
        Assert.That(mutation, Is.Not.Null);
    }

    [Test]
    public async Task Clipboard_paste_is_rejected_while_the_worktree_is_mutating()
    {
        var editorService = new EditorService(new ExtensionProvider());
        var inner = new BlockingClipboardService(
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            Task.CompletedTask);
        var clipboard = new ProjectFileWriteClipboardService(editorService, inner);
        var scene = new Scene { Uri = new Uri("file:///project/main.scene") };

        using IDisposable mutation = editorService.TryBeginWorktreeMutation()!;
        ElementPasteOutcome outcome = await clipboard.PasteAsync(scene, TimeSpan.Zero, 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.SameAs(ElementPasteOutcome.Empty));
            Assert.That(inner.PasteCalls, Is.Zero);
        });
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
        var outputContext = new StubOutputContext(context.Object);
        using var output = new OutputProfileItem(outputContext, context, editorService);
        outputContext.RaiseStarted();
        Task<bool> save = editorService.SaveProjectFilesAsync(project, CancellationToken.None);

        try
        {
            await serializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(context.IsEnabled.Value, Is.False);

            outputContext.RaiseFinished();

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
            outputContext.RaiseFinished();
            allowSerialization.Set();
            await save.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task Output_dispose_racing_with_started_releases_the_workspace_and_editor()
    {
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        var context = new StubEditorContext();
        editorService.TabItems.Add(new EditorTabItem(context));
        var outputContext = new StubOutputContext(context.Object);
        var output = new OutputProfileItem(outputContext, context, editorService);
        var suspensionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseSuspension = new ManualResetEventSlim();
        using IDisposable subscription = context.IsEnabled.Subscribe(enabled =>
        {
            if (!enabled)
            {
                suspensionStarted.TrySetResult();
                releaseSuspension.Wait();
            }
        });

        Task started = Task.Run(outputContext.RaiseStarted);
        await suspensionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task dispose = Task.Run(output.Dispose);
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        releaseSuspension.Set();
        await started.WaitAsync(TimeSpan.FromSeconds(5));

        using IDisposable? mutation = editorService.TryBeginWorktreeMutation();
        Assert.Multiple(() =>
        {
            Assert.That(mutation, Is.Not.Null);
            Assert.That(context.IsEnabled.Value, Is.True);
            Assert.That(outputContext.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Output_started_during_a_worktree_mutation_is_rejected_without_leaking_a_lease()
    {
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        var context = new StubEditorContext();
        editorService.TabItems.Add(new EditorTabItem(context));
        var outputContext = new StubOutputContext(context.Object);
        using var output = new OutputProfileItem(outputContext, context, editorService);

        using (IDisposable mutation = editorService.TryBeginWorktreeMutation()!)
        {
            Assert.Throws<InvalidOperationException>(outputContext.RaiseStarted);
            outputContext.RaiseFinished();
            Assert.That(context.IsEnabled.Value, Is.True);
        }

        using IDisposable? nextMutation = editorService.TryBeginWorktreeMutation();
        Assert.That(nextMutation, Is.Not.Null);
    }

    private sealed class ForeignLease : IProjectFileWriteLease
    {
        public void Dispose()
        {
        }
    }

    private sealed class BlockingClipboardService(
        TaskCompletionSource pasteStarted,
        Task releasePaste) : IElementClipboardService
    {
        public int PasteCalls { get; private set; }

        public Task<bool> CopyAsync(IReadOnlyList<Element> elements)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CutAsync(
            Scene scene,
            IReadOnlyList<Element> elements,
            bool ripple = false)
        {
            return Task.FromResult(true);
        }

        public async Task<ElementPasteOutcome> PasteAsync(
            Scene scene,
            TimeSpan clickedFrame,
            int clickedLayer)
        {
            PasteCalls++;
            pasteStarted.TrySetResult();
            await releasePaste;
            return new ElementPasteOutcome(true, [], default, 0);
        }
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

    private sealed class StubOutputContext(CoreObject obj) : IOutputContext
    {
        public int DisposeCalls { get; private set; }

        public OutputExtension Extension => throw new NotSupportedException();

        public CoreObject Object { get; } = obj;

        public IReactiveProperty<string> Name { get; } = new ReactivePropertySlim<string>("Output");

        public IReadOnlyReactiveProperty<bool> IsIndeterminate { get; }
            = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<bool> IsEncoding { get; }
            = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<double> Progress { get; }
            = new ReactivePropertySlim<double>();

        public event EventHandler? Started;

        public event EventHandler? Finished;

        public void RaiseStarted() => Started?.Invoke(this, EventArgs.Empty);

        public void RaiseFinished() => Finished?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            DisposeCalls++;
        }

        public void WriteToJson(System.Text.Json.Nodes.JsonObject json)
        {
        }

        public void ReadFromJson(System.Text.Json.Nodes.JsonObject json)
        {
        }
    }
}

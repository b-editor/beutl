using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class EditorHostProjectQueueTests
{
    private const int ExtensionPackageId = 930001;

    [AvaloniaTest]
    public async Task CreateProject_waits_for_old_context_teardown_and_keeps_new_tab()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory { BlockNextDispose = true };
        (ProjectService projectService, EditorService editorService, EditorHostViewModel host) =
            CreateComposition(contexts);

        try
        {
            Project first = (await projectService.CreateProject(
                320, 180, 30, 44100, "queue-first", NewWorkspace("create-first")))!;
            TestContext oldContext = contexts.Contexts.Single();

            Task<Project?> replacement = projectService.CreateProject(
                640, 360, 24, 48000, "queue-second", NewWorkspace("create-second"));
            await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(replacement.IsCompleted, Is.False,
                "CreateProject must not complete while the old tab context is still tearing down.");
            Assert.That(BeutlApplication.Current.Project, Is.SameAs(first),
                "CurrentProject must not advance before the replacement editor is stable.");

            oldContext.ReleaseDispose();
            Project second = (await replacement.WaitAsync(TimeSpan.FromSeconds(5)))!;

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(BeutlApplication.Current.Project, Is.SameAs(second));
                Assert.That(editorService.TabItems.Count, Is.EqualTo(1));
                Assert.That(editorService.TabItems.Single().Context.Value?.Object,
                    Is.SameAs(second.Items.Single()));
                Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            });
        }
        finally
        {
            await DisposeCompositionAsync(projectService, editorService, host);
        }
    }

    [AvaloniaTest]
    public async Task Repeated_close_joins_the_same_pending_editor_teardown()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory { BlockNextDispose = true };
        (ProjectService projectService, EditorService editorService, EditorHostViewModel host) =
            CreateComposition(contexts);

        try
        {
            _ = await projectService.CreateProject(
                320, 180, 30, 44100, "queue-close", NewWorkspace("repeated-close"));
            TestContext context = contexts.Contexts.Single();

            Task first = projectService.CloseProjectAsync();
            await context.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task second = projectService.CloseProjectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.SameAs(first));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
            });

            context.ReleaseDispose();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(BeutlApplication.Current.Project, Is.Null);
                Assert.That(editorService.TabItems, Is.Empty);
                Assert.That(context.DisposeCount, Is.EqualTo(1));
            });
        }
        finally
        {
            await DisposeCompositionAsync(projectService, editorService, host);
        }
    }

    [AvaloniaTest]
    public async Task Close_after_queued_replacement_is_not_deduplicated_with_earlier_close()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory { BlockNextDispose = true };
        (ProjectService projectService, EditorService editorService, EditorHostViewModel host) =
            CreateComposition(contexts);

        try
        {
            _ = await projectService.CreateProject(
                320, 180, 30, 44100, "queue-close-order", NewWorkspace("close-order"));
            TestContext context = contexts.Contexts.Single();

            Task firstClose = projectService.CloseProjectAsync();
            await context.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<Project?> replacement = projectService.CreateProject(
                640, 360, 24, 48000, "queue-close-order-next", NewWorkspace("close-order-next"));
            Task finalClose = projectService.CloseProjectAsync();

            Assert.That(finalClose, Is.Not.SameAs(firstClose));
            context.ReleaseDispose();
            await Task.WhenAll(firstClose, replacement, finalClose).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(BeutlApplication.Current.Project, Is.Null);
                Assert.That(editorService.TabItems, Is.Empty);
            });
        }
        finally
        {
            await DisposeCompositionAsync(projectService, editorService, host);
        }
    }

    [AvaloniaTest]
    public async Task Concurrent_create_requests_preserve_invocation_order()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory();
        (ProjectService projectService, EditorService editorService, EditorHostViewModel host) =
            CreateComposition(contexts);
        var firstPreparationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstPreparation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPreparationCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        projectService.BeforeCreateProjectPreparation = async name =>
        {
            if (name == "queue-order-first")
            {
                firstPreparationStarted.TrySetResult();
                await releaseFirstPreparation.Task;
            }
        };
        projectService.AfterCreateProjectPreparation = name =>
        {
            if (name == "queue-order-second")
                secondPreparationCompleted.TrySetResult();
        };

        try
        {
            Task<Project?> first = projectService.CreateProject(
                320, 180, 30, 44100, "queue-order-first", NewWorkspace("order-first"));
            await firstPreparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<Project?> second = projectService.CreateProject(
                640, 360, 24, 48000, "queue-order-second", NewWorkspace("order-second"));

            Assert.Multiple(() =>
            {
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(secondPreparationCompleted.Task.IsCompleted, Is.False,
                    "A later request must not begin preparation ahead of an earlier accepted transition.");
            });
            releaseFirstPreparation.TrySetResult();

            Project firstProject = (await first.WaitAsync(TimeSpan.FromSeconds(5)))!;
            Project secondProject = (await second.WaitAsync(TimeSpan.FromSeconds(5)))!;

            Assert.Multiple(() =>
            {
                Assert.That(firstProject, Is.Not.SameAs(secondProject));
                Assert.That(BeutlApplication.Current.Project, Is.SameAs(secondProject));
                Assert.That(editorService.TabItems.Single().Context.Value?.Object,
                    Is.SameAs(secondProject.Items.Single()));
            });
        }
        finally
        {
            releaseFirstPreparation.TrySetResult();
            projectService.BeforeCreateProjectPreparation = null;
            projectService.AfterCreateProjectPreparation = null;
            await DisposeCompositionAsync(projectService, editorService, host);
        }
    }

    [AvaloniaTest]
    public async Task Replacement_rejects_old_context_reactivation_during_teardown()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory();
        (ProjectService projectService, EditorService editorService, EditorHostViewModel host) =
            CreateComposition(contexts);

        try
        {
            Project first = (await projectService.CreateProject(
                320, 180, 30, 44100, "queue-reentrant-first", NewWorkspace("reentrant-first")))!;
            ProjectItem oldItem = first.Items.Single();
            contexts.Contexts.Single().OnDispose = () => editorService.ActivateTabItem(oldItem);

            Project second = (await projectService.CreateProject(
                640, 360, 24, 48000, "queue-reentrant-second", NewWorkspace("reentrant-second")))!;

            Assert.Multiple(() =>
            {
                Assert.That(editorService.TryGetTabItem(oldItem, out _), Is.False);
                Assert.That(editorService.TabItems.Count, Is.EqualTo(1));
                Assert.That(editorService.TabItems.Single().Context.Value?.Object,
                    Is.SameAs(second.Items.Single()));
                Assert.That(contexts.Contexts.Count, Is.EqualTo(2),
                    "Rejected reactivation must not create another old-project context.");
            });
        }
        finally
        {
            await DisposeCompositionAsync(projectService, editorService, host);
        }
    }

    [AvaloniaTest]
    public async Task Background_project_item_add_is_applied_on_ui_by_wait_for_pending_changes()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory();
        (ProjectService projectService, EditorService editorService, EditorHostViewModel host) =
            CreateComposition(contexts);

        try
        {
            Project project = (await projectService.CreateProject(
                320, 180, 30, 44100, "queue-add", NewWorkspace("background-add")))!;
            var added = new Scene(160, 90, "background-item")
            {
                Uri = new Uri(Path.Combine(NewWorkspace("background-add-item"), "background-item.scene")),
            };

            await Task.Run(() => project.Items.Add(added));
            await projectService.WaitForPendingProjectChangesAsync();

            Assert.Multiple(() =>
            {
                Assert.That(project.Items, Does.Contain(added));
                Assert.That(editorService.TabItems.Count, Is.EqualTo(2));
                Assert.That(editorService.TryGetTabItem(added, out _), Is.True);
                Assert.That(editorService.SelectedTabItem.Value?.Context.Value?.Object,
                    Is.SameAs(added));
            });
        }
        finally
        {
            await DisposeCompositionAsync(projectService, editorService, host);
        }
    }

    [AvaloniaTest]
    public async Task Queued_old_project_item_event_is_ignored_after_replacement()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory();
        (ProjectService projectService, EditorService editorService, EditorHostViewModel host) =
            CreateComposition(contexts);

        try
        {
            Project first = (await projectService.CreateProject(
                320, 180, 30, 44100, "queue-stale-first", NewWorkspace("stale-first")))!;
            Scene stale = new(160, 90, "stale-item")
            {
                Uri = new Uri(Path.Combine(NewWorkspace("stale-item"), "stale-item.scene")),
            };

            // Keep the dispatcher occupied while the replacement publishes its project-change
            // task. The old-item event is then queued behind that replacement and must be fenced
            // by the host's active-items identity check.
            var dispatcherEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDispatcher = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() =>
            {
                dispatcherEntered.TrySetResult();
                releaseDispatcher.Task.GetAwaiter().GetResult();
            });

            Task<Project> scenario = Task.Run(async () =>
            {
                await dispatcherEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Task<Project?> replacement = projectService.CreateProject(
                    640, 360, 24, 48000, "queue-stale-second", NewWorkspace("stale-second"));
                await WaitForPublishedProjectChangeAsync(projectService, replacement);

                // This event is raised while the replacement is already the pending project
                // change, but before the UI callback can detach the old collection.
                first.Items.Add(stale);
                releaseDispatcher.TrySetResult();

                Project second = (await replacement.WaitAsync(TimeSpan.FromSeconds(5)))!;
                await projectService.WaitForPendingProjectChangesAsync();
                return second;
            });

            Project secondProject = (await scenario.WaitAsync(TimeSpan.FromSeconds(10)))!;

            Assert.Multiple(() =>
            {
                Assert.That(BeutlApplication.Current.Project, Is.SameAs(secondProject));
                Assert.That(editorService.TryGetTabItem(stale, out _), Is.False,
                    "The old project's late item event must not create a tab in the replacement project.");
                Assert.That(editorService.TabItems.Count, Is.EqualTo(1));
                Assert.That(editorService.TabItems.Single().Context.Value?.Object,
                    Is.SameAs(secondProject.Items.Single()));
            });
        }
        finally
        {
            await DisposeCompositionAsync(projectService, editorService, host);
        }
    }

    [AvaloniaTest]
    public async Task Second_editor_host_registration_throws_and_dispose_allows_replacement()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory { BlockNextDispose = true };
        var projectService = new ProjectService();
        var firstEditorService = CreateEditorService(contexts);
        var firstHost = new EditorHostViewModel(projectService, firstEditorService);

        try
        {
            Project project = (await projectService.CreateProject(
                320, 180, 30, 44100, "queue-host-replay", NewWorkspace("host-replay")))!;
            Assert.Throws<InvalidOperationException>(() =>
                new EditorHostViewModel(projectService, new EditorService(new ExtensionProvider())));

            TestContext firstContext = contexts.Contexts.Single();
            Task firstDisposal = firstHost.DisposeAsync().AsTask();
            await firstContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<InvalidOperationException>(() =>
                new EditorHostViewModel(projectService, firstEditorService));
            firstContext.ReleaseDispose();
            await firstDisposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(firstEditorService.TabItems, Is.Empty);

            await using var replacementHost = new EditorHostViewModel(
                projectService, firstEditorService);
            await projectService.WaitForPendingProjectChangesAsync();

            Assert.Multiple(() =>
            {
                Assert.That(BeutlApplication.Current.Project, Is.SameAs(project));
                Assert.That(firstEditorService.TabItems.Count, Is.EqualTo(1));
                Assert.That(firstEditorService.TabItems.Single().Context.Value?.Object,
                    Is.SameAs(project.Items.Single()));
            });
        }
        finally
        {
            await firstHost.DisposeAsync();
            await projectService.CloseProjectAsync();
        }
    }

    [AvaloniaTest]
    public async Task Replacing_project_items_disposes_old_context_before_pending_changes_complete()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory { BlockNextDispose = true };
        (ProjectService projectService, EditorService editorService, EditorHostViewModel host) =
            CreateComposition(contexts);

        try
        {
            Project project = (await projectService.CreateProject(
                320, 180, 30, 44100, "queue-reset", NewWorkspace("reset")))!;
            TestContext oldContext = contexts.Contexts.Single();
            ProjectItem oldItem = project.Items.Single();
            var replacement = new Scene(640, 360, "replacement")
            {
                Uri = new Uri(Path.Combine(NewWorkspace("reset-item"), "replacement.scene")),
            };

            project.Items.Replace([replacement]);
            Task pending = projectService.WaitForPendingProjectChangesAsync();
            await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(pending.IsCompleted, Is.False);

            oldContext.ReleaseDispose();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
                Assert.That(editorService.TryGetTabItem(replacement, out _), Is.True);
                Assert.That(editorService.TryGetTabItem(oldItem, out _), Is.False);
                Assert.That(editorService.TabItems.Count, Is.EqualTo(1));
            });
        }
        finally
        {
            await DisposeCompositionAsync(projectService, editorService, host);
        }
    }

    [AvaloniaTest]
    public async Task Resetting_project_items_disposes_old_context_before_pending_changes_complete()
    {
        await TestReset.ResetShellAsync();
        var contexts = new TestContextFactory { BlockNextDispose = true };
        (ProjectService projectService, EditorService editorService, EditorHostViewModel host) =
            CreateComposition(contexts);

        try
        {
            Project project = (await projectService.CreateProject(
                320, 180, 30, 44100, "queue-clear", NewWorkspace("clear")))!;
            TestContext oldContext = contexts.Contexts.Single();
            project.Items.ResetBehavior = ResetBehavior.Reset;

            project.Items.Clear();
            Task pending = projectService.WaitForPendingProjectChangesAsync();
            await oldContext.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(pending.IsCompleted, Is.False);

            oldContext.ReleaseDispose();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
                Assert.That(editorService.TabItems, Is.Empty);
                Assert.That(editorService.SelectedTabItem.Value, Is.Null);
            });
        }
        finally
        {
            await DisposeCompositionAsync(projectService, editorService, host);
        }
    }

    private static (ProjectService ProjectService, EditorService EditorService, EditorHostViewModel Host)
        CreateComposition(TestContextFactory contexts)
    {
        EditorService editorService = CreateEditorService(contexts);
        var projectService = new ProjectService();
        var host = new EditorHostViewModel(projectService, editorService);
        return (projectService, editorService, host);
    }

    private static EditorService CreateEditorService(TestContextFactory contexts)
    {
        var extensionProvider = new ExtensionProvider();
        extensionProvider.AddExtensions(ExtensionPackageId, [new TestEditorExtension(contexts)]);
        return new EditorService(extensionProvider);
    }

    private static async Task DisposeCompositionAsync(
        ProjectService projectService,
        EditorService editorService,
        EditorHostViewModel host)
    {
        try
        {
            await projectService.CloseProjectAsync();
        }
        finally
        {
            await host.DisposeAsync();
            await editorService.ClearTabItemsAsync();
        }
    }

    private static string NewWorkspace(string name)
    {
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"project-queue-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WaitForPublishedProjectChangeAsync(
        ProjectService projectService,
        Task<Project?> operation)
    {
        FieldInfo field = typeof(ProjectService).GetField(
            "_lastProjectChangeTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        while (!operation.IsCompleted)
        {
            if (field.GetValue(projectService) is Task task && !task.IsCompleted)
                return;

            await Task.Yield();
        }

        Assert.Fail("The replacement operation completed before its project-change task was published.");
    }

    private sealed class TestEditorExtension(TestContextFactory contexts) : EditorExtension
    {
        public override FilePickerFileType GetFilePickerFileType() => new("Queue test");

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
            [NotNullWhen(true)] out IEditorContext? context)
        {
            context = contexts.Create(obj, this, services.CloseService);
            return true;
        }

        public override bool MatchFileExtension(string ext)
            => ext.Equals(".scene", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestContextFactory
    {
        public List<TestContext> Contexts { get; } = [];

        public bool BlockNextDispose { get; set; }

        public TestContext Create(
            CoreObject obj,
            EditorExtension extension,
            IEditorContextCloseService closeService)
        {
            var context = new TestContext(obj, extension, closeService, BlockNextDispose);
            BlockNextDispose = false;
            Contexts.Add(context);
            return context;
        }
    }

    private sealed class TestContext(
        CoreObject obj,
        EditorExtension extension,
        IEditorContextCloseService closeService,
        bool blockDispose) : IEditorContext
    {
        private readonly TaskCompletionSource _releaseDispose = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposeStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public Action? OnDispose { get; set; }

        public CoreObject Object { get; } = obj;

        public EditorExtension Extension { get; } = extension;

        public IEditorContextCloseService CloseService { get; } = closeService;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposeStarted.TrySetResult();
            OnDispose?.Invoke();
            if (blockDispose)
                await _releaseDispose.Task.ConfigureAwait(false);
        }

        public void ReleaseDispose() => _releaseDispose.TrySetResult();

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public ValueTask<bool> OpenToolTabAsync(IToolContext item) => new(false);

        public ValueTask CloseToolTabAsync(IToolContext item) => ValueTask.CompletedTask;

        public object? GetService(Type serviceType) => null;
    }
}

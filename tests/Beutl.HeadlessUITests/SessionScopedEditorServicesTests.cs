using System.Collections.Immutable;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.OutputTab;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Each loaded project has its own editor workspace, version control, palettes and asset host, so work on one project
// neither waits for, suspends nor covers the editors of another (#4792).
[TestFixture]
[NonParallelizable]
public sealed class SessionScopedEditorServicesTests
{
    private bool _oldAutoCommitOnClose;
    private bool _oldAutoSaveSuppressed;

    private static ProjectSessionRegistry Registry => TestShell.Project.Sessions;

    [SetUp]
    public void SuppressBackgroundSaves()
    {
        VersionControlConfig config = TestShell.Configuration.VersionControlConfig;
        _oldAutoCommitOnClose = config.AutoCommitOnClose.Value;
        _oldAutoSaveSuppressed = EditViewModel.IsAutoSaveSuppressedForTesting;
        config.AutoCommitOnClose.Value = false;
        EditViewModel.IsAutoSaveSuppressedForTesting = true;
    }

    [TearDown]
    public void RestoreBackgroundSaves()
    {
        TestShell.Configuration.VersionControlConfig.AutoCommitOnClose.Value = _oldAutoCommitOnClose;
        EditViewModel.IsAutoSaveSuppressedForTesting = _oldAutoSaveSuppressed;
    }

    [AvaloniaTest]
    public async Task Work_on_one_project_neither_blocks_nor_suspends_another()
    {
        await TestReset.ResetShellAsync();
        try
        {
            (ProjectSession background, EditViewModel backgroundEditor, ProjectSession foreground, EditViewModel foregroundEditor)
                = await OpenTwoProjectsAsync("work-isolation");
            EditorWorkspace backgroundWorkspace = TestShell.Editor.WorkspaceFor(background);
            EditorWorkspace foregroundWorkspace = TestShell.Editor.WorkspaceFor(foreground);

            Assert.Multiple(() =>
            {
                Assert.That(backgroundWorkspace, Is.Not.SameAs(foregroundWorkspace));
                Assert.That(TestShell.Editor.ActiveWorkspace.Value, Is.SameAs(foregroundWorkspace));
                Assert.That(backgroundEditor.Workspace, Is.SameAs(backgroundWorkspace));
                Assert.That(foregroundEditor.Workspace, Is.SameAs(foregroundWorkspace));
                Assert.That(backgroundWorkspace.TabItems.Select(tab => tab.Context.Value), Is.EqualTo(new[] { backgroundEditor }));
                Assert.That(foregroundWorkspace.TabItems.Select(tab => tab.Context.Value), Is.EqualTo(new[] { foregroundEditor }));
            });

            // An export still running in the background project leaves the active project's files free.
            using (IDisposable? output = backgroundWorkspace.TryBeginOutputOperation())
            {
                Assert.That(output, Is.Not.Null);
                using IDisposable? mutation = foregroundWorkspace.TryBeginWorktreeMutation();
                Assert.Multiple(() =>
                {
                    Assert.That(mutation, Is.Not.Null);
                    Assert.That(TestShell.Editor.IsWorktreeMutationActive, Is.True);
                    Assert.That(backgroundWorkspace.IsWorktreeMutationActive, Is.False);
                });
            }

            using (backgroundWorkspace.SuspendEditors())
            {
                Assert.Multiple(() =>
                {
                    Assert.That(backgroundEditor.IsEnabled.Value, Is.False);
                    Assert.That(foregroundEditor.IsEnabled.Value, Is.True);
                });
            }

            using (TestShell.Editor.SuspendEditors())
            {
                Assert.Multiple(() =>
                {
                    Assert.That(foregroundEditor.IsEnabled.Value, Is.False);
                    Assert.That(backgroundEditor.IsEnabled.Value, Is.True, "Saving the active project leaves the others usable.");
                });
            }

            Assert.Multiple(() =>
            {
                Assert.That(backgroundEditor.IsEnabled.Value, Is.True);
                Assert.That(foregroundEditor.IsEnabled.Value, Is.True);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Only_the_active_project_and_work_outside_projects_cover_the_editor_area()
    {
        await TestReset.ResetShellAsync();
        try
        {
            (ProjectSession background, _, ProjectSession foreground, _) = await OpenTwoProjectsAsync("activity-isolation");
            EditorWorkspace backgroundWorkspace = TestShell.Editor.WorkspaceFor(background);
            EditorWorkspace foregroundWorkspace = TestShell.Editor.WorkspaceFor(foreground);

            using (backgroundWorkspace.BeginLifecycleActivity(ProjectLifecycleActivity.ClosingProject))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(backgroundWorkspace.LifecycleActivity.Value, Is.EqualTo(ProjectLifecycleActivity.ClosingProject));
                    Assert.That(TestShell.Editor.LifecycleActivity.Value, Is.EqualTo(ProjectLifecycleActivity.None));
                    Assert.That(TestShell.MainViewModel.EditorHost.LifecycleProgress.Value, Is.Null);
                });

                using (foregroundWorkspace.BeginLifecycleActivity(ProjectLifecycleActivity.CommittingVersion))
                {
                    Assert.That(TestShell.Editor.LifecycleActivity.Value, Is.EqualTo(ProjectLifecycleActivity.CommittingVersion));
                    using (TestShell.Editor.BeginLifecycleActivity(ProjectLifecycleActivity.CreatingProject))
                    {
                        Assert.That(TestShell.Editor.LifecycleActivity.Value, Is.EqualTo(ProjectLifecycleActivity.CreatingProject),
                            "Work outside any project, such as creating one, is shown over whichever project is active.");
                    }

                    Assert.That(TestShell.Editor.LifecycleActivity.Value, Is.EqualTo(ProjectLifecycleActivity.CommittingVersion));
                }

                // Activating the background project shows the work running on it.
                await TestShell.Project.OpenProject(background.FilePath!);
                HeadlessTestHelpers.Settle();
                Assert.That(TestShell.Editor.LifecycleActivity.Value, Is.EqualTo(ProjectLifecycleActivity.ClosingProject));
            }

            Assert.That(TestShell.Editor.LifecycleActivity.Value, Is.EqualTo(ProjectLifecycleActivity.None));
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_background_editor_writes_through_its_own_project()
    {
        await TestReset.ResetShellAsync();
        try
        {
            (ProjectSession background, EditViewModel backgroundEditor, _, EditViewModel foregroundEditor)
                = await OpenTwoProjectsAsync("admission-isolation");
            IProjectFileWriteAdmission backgroundAdmission = HostProjectFileWriteAdmission.Resolve(backgroundEditor)!;
            IProjectFileWriteAdmission foregroundAdmission = HostProjectFileWriteAdmission.Resolve(foregroundEditor)!;

            Assert.Multiple(() =>
            {
                Assert.That(backgroundAdmission, Is.SameAs(TestShell.Editor.WorkspaceFor(background).Admission));
                Assert.That(backgroundEditor.GetService(typeof(IProjectFileWriteAdmission)), Is.SameAs(backgroundAdmission));
                Assert.That(foregroundAdmission, Is.SameAs(TestShell.Editor.ActiveWorkspace.Value.Admission));
            });

            using IProjectFileWriteLease foregroundWrite = await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None);
            using IProjectFileWriteLease? backgroundWrite = backgroundAdmission.TryBeginProjectFileWrite();
            Assert.Multiple(() =>
            {
                Assert.That(backgroundWrite, Is.Not.Null, "A write in one project does not wait for one in another.");
                Assert.That(foregroundAdmission.TryBeginProjectFileWrite(), Is.Null);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_file_only_a_background_project_uses_is_in_use()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("file-usage-background");
            string second = await CreateClosedProjectAsync("file-usage-foreground");
            await TestShell.Project.OpenProject(first);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            string media = Path.Combine(Path.GetDirectoryName(first)!, "background-media.png");
            File.WriteAllBytes(media, [0]);
            var source = new ImageSource();
            source.ReadFrom(new Uri(media));
            var element = new Element();
            element.Objects.Add(new SourceImage { Source = { Value = source } });
            editor.Scene.Children.Add(element);

            await TestShell.Project.OpenProject(second);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value!.Uri!.LocalPath, Is.EqualTo(second));
                Assert.That(TestShell.Editor.IsFileInUse(media, false), Is.True);
                Assert.That(TestShell.Editor.IsFileInUse(Path.GetDirectoryName(first)!, true), Is.True);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Each_project_has_its_own_asset_host_and_palettes_and_the_pickers_follow_the_active_one()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("palette-first");
            string second = await CreateClosedProjectAsync("palette-second");
            ImmutableArray<ColorPalette> firstPalettes = [new ColorPalette("First", [new PaletteColor(0xFF112233)])];
            ImmutableArray<ColorPalette> secondPalettes = [new ColorPalette("Second", [new PaletteColor(0xFF445566)])];
            ProjectColorPaletteFile.Write(ProjectColorPaletteFile.GetPath(first), firstPalettes);
            ProjectColorPaletteFile.Write(ProjectColorPaletteFile.GetPath(second), secondPalettes);
            var pickers = TestShell.MainViewModel.AppServices.GetRequiredService<ColorPaletteService>();

            await TestShell.Project.OpenProject(first);
            HeadlessTestHelpers.Settle();
            ProjectSession firstSession = Registry.ActiveSession.Value!;
            Assert.That(pickers.GetPalettes(PaletteScope.Project), Is.EqualTo(firstPalettes));

            await TestShell.Project.OpenProject(second);
            HeadlessTestHelpers.Settle();
            ProjectSession secondSession = Registry.ActiveSession.Value!;
            EditorWorkspace firstWorkspace = TestShell.Editor.WorkspaceFor(firstSession);
            EditorWorkspace secondWorkspace = TestShell.Editor.WorkspaceFor(secondSession);

            Assert.Multiple(() =>
            {
                Assert.That(pickers.GetPalettes(PaletteScope.Project), Is.EqualTo(secondPalettes));
                Assert.That(firstWorkspace.ColorPalettes!.Palettes, Is.EqualTo(firstPalettes), "The background project keeps its palettes.");
                Assert.That(firstWorkspace.ProjectAssets, Is.Not.SameAs(secondWorkspace.ProjectAssets));
                Assert.That(firstWorkspace.ProjectAssets!.CurrentProject.Value, Is.SameAs(firstSession.Project));
                Assert.That(TestShell.Editor.ProjectAssets, Is.SameAs(secondWorkspace.ProjectAssets));
            });

            // A project in the background can still be checked for unused assets.
            Beutl.Editor.ProjectAssetPreview preview = await firstWorkspace.ProjectAssets!.PreviewAsync(firstSession.Project, CancellationToken.None);
            Assert.That(preview, Is.Not.Null);

            Assert.That(await TestShell.Project.TryCloseProjectAsync(secondSession.Project, ProjectService.ProjectCloseIntent.SaveChanges), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(Registry.ActiveSession.Value, Is.Null);
                Assert.That(pickers.IsAvailable(PaletteScope.Project), Is.False, "Without an active project there are no project palettes.");
                Assert.That(secondWorkspace.ProjectAssets!.CurrentProject.Value, Is.Null);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Each_project_is_tracked_by_its_own_coordinator_until_it_closes()
    {
        await TestReset.ResetShellAsync();
        try
        {
            VersionControlCoordinators versionControl = TestShell.MainViewModel.VersionControl;
            string first = await CreateClosedProjectAsync("coordinator-per-project-background");
            string second = await CreateClosedProjectAsync("coordinator-per-project-foreground");
            VersionControlCoordinator standby = versionControl.Current;
            Assert.That(standby.TrackedProject, Is.Null);

            (ProjectSession background, EditViewModel backgroundEditor, ProjectSession foreground, EditViewModel foregroundEditor)
                = await OpenTwoProjectsAsync(first, second);
            VersionControlCoordinator backgroundCoordinator = (VersionControlCoordinator)TestShell.Editor.WorkspaceFor(background).VersionControl!;
            VersionControlCoordinator foregroundCoordinator = versionControl.Current;

            Assert.Multiple(() =>
            {
                Assert.That(backgroundCoordinator, Is.SameAs(standby), "The coordinator that waited claimed the first project.");
                Assert.That(foregroundCoordinator, Is.Not.SameAs(backgroundCoordinator));
                Assert.That(backgroundCoordinator.TrackedProject, Is.SameAs(background.Project));
                Assert.That(foregroundCoordinator.TrackedProject, Is.SameAs(foreground.Project));
                Assert.That(versionControl.Active.Value, Is.SameAs(foregroundCoordinator));
                Assert.That(backgroundEditor.GetService(typeof(IProjectVersionControlCoordinator)), Is.SameAs(backgroundCoordinator));
                Assert.That(foregroundEditor.GetService(typeof(IProjectVersionControlCoordinator)), Is.SameAs(foregroundCoordinator));
                Assert.That(versionControl.All, Has.Count.EqualTo(3), "Two projects and the coordinator waiting for the next one.");
            });

            Func<CancellationToken, Task<GitIdentity?>> requestIdentity = _ => Task.FromResult<GitIdentity?>(null);
            versionControl.RequestIdentityAsync = requestIdentity;
            Assert.That(versionControl.All.Select(c => c.RequestIdentityAsync), Is.All.SameAs(requestIdentity));

            Assert.That(await TestShell.Project.TryCloseProjectAsync(background.Project, ProjectService.ProjectCloseIntent.SaveChanges), Is.True);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(backgroundCoordinator.TrackedProject, Is.Null);
                Assert.That(versionControl.All, Does.Not.Contain(backgroundCoordinator));
                Assert.That(versionControl.Current, Is.SameAs(foregroundCoordinator));
                Assert.That(foregroundCoordinator.TrackedProject, Is.SameAs(foreground.Project));
            });

            // A project opened later is claimed by a coordinator that asks for the identity the same way.
            Assert.That(versionControl.All.Last().RequestIdentityAsync, Is.SameAs(requestIdentity));
        }
        finally
        {
            TestShell.MainViewModel.VersionControl.RequestIdentityAsync = null;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task The_workspace_of_a_closed_project_is_let_go()
    {
        await TestReset.ResetShellAsync();
        try
        {
            (ProjectSession background, _, ProjectSession foreground, _) = await OpenTwoProjectsAsync("workspace-release");
            EditorWorkspace backgroundWorkspace = TestShell.Editor.WorkspaceFor(background);
            Assert.That(TestShell.Editor.Workspaces, Does.Contain(backgroundWorkspace));

            Assert.That(await TestShell.Project.TryCloseProjectAsync(background.Project, ProjectService.ProjectCloseIntent.SaveChanges), Is.True);
            HeadlessTestHelpers.Settle();
            // Released once nothing holds it, the next time the active project changes.
            Assert.That(await TestShell.Project.TryCloseProjectAsync(foreground.Project, ProjectService.ProjectCloseIntent.SaveChanges), Is.True);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(backgroundWorkspace.Project.Value, Is.Null);
                Assert.That(backgroundWorkspace.IsIdle, Is.True);
                Assert.That(TestShell.Editor.Workspaces, Does.Not.Contain(backgroundWorkspace));
                Assert.That(TestShell.Editor.ActiveWorkspace.Value, Is.SameAs(TestShell.Editor.ShellWorkspace));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Outputs_of_every_project_are_counted_as_running()
    {
        await TestReset.ResetShellAsync();
        try
        {
            (ProjectSession background, EditViewModel backgroundEditor, _, _) = await OpenTwoProjectsAsync("running-outputs");
            EditorWorkspace backgroundWorkspace = TestShell.Editor.WorkspaceFor(background);
            var outputContext = new StubOutputContext(backgroundEditor.Scene);
            using var output = new OutputProfileItem(
                outputContext, backgroundEditor, backgroundWorkspace);
            Assert.That(output.TryStart(out Task? execution), Is.True);
            await outputContext.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(backgroundWorkspace.RunningOutputs.Value, Is.EqualTo(new[] { output }));
                    Assert.That(TestShell.Editor.ActiveWorkspace.Value.RunningOutputs.Value, Is.Empty);
                    Assert.That(TestShell.Editor.RunningOutputs.Value, Is.EqualTo(new[] { output }));
                });
                using IDisposable? mutation = TestShell.Editor.ActiveWorkspace.Value.TryBeginWorktreeMutation();
                Assert.That(mutation, Is.Not.Null, "The background export does not hold the active project's files.");
            }
            finally
            {
                outputContext.Finish();
                await execution!.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.That(TestShell.Editor.RunningOutputs.Value, Is.Empty);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Eviction_keeps_the_proxies_of_every_loaded_project()
    {
        await TestReset.ResetShellAsync();
        try
        {
            (ProjectSession background, EditViewModel backgroundEditor, ProjectSession foreground, EditViewModel foregroundEditor)
                = await OpenTwoProjectsAsync("proxy-sources");
            string backgroundMedia = AddImage(backgroundEditor.Scene, background.FilePath!);
            string foregroundMedia = AddImage(foregroundEditor.Scene, foreground.FilePath!);
            string directory = TestShell.NewWorkspace("proxy-sources-store");
            var config = new GlobalConfiguration();
            config.ProxyStoreConfig.StoreRootPath.Value = Path.Combine(directory, "proxy-store");
            await using var proxy = new ProxyMediaServices(config, TestShell.Notifications, new Beutl.Media.Decoding.DecoderRegistry(config.ExtensionConfig));

            IReadOnlySet<string> sources = proxy.CollectOpenProjectSources();

            Assert.Multiple(() =>
            {
                Assert.That(sources, Does.Contain(Path.GetFullPath(foregroundMedia)));
                Assert.That(sources, Does.Contain(Path.GetFullPath(backgroundMedia)), "A project in the background keeps its proxies too.");
            });

            // A project that closes no longer protects its sources.
            Assert.That(await TestShell.Project.TryCloseProjectAsync(background.Project, ProjectService.ProjectCloseIntent.DiscardChanges), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.That(proxy.CollectOpenProjectSources(), Does.Not.Contain(Path.GetFullPath(backgroundMedia)));
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static string AddImage(Scene scene, string projectFile)
    {
        string media = Path.Combine(Path.GetDirectoryName(projectFile)!, $"media-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(media, [0]);
        var source = new ImageSource();
        source.ReadFrom(new Uri(media));
        var element = new Element();
        element.Objects.Add(new SourceImage { Source = { Value = source } });
        scene.Children.Add(element);
        return media;
    }

    // Opens a project, then a second one, each showing its scene. The first one goes to the background.
    private static async Task<(ProjectSession Background, EditViewModel BackgroundEditor, ProjectSession Foreground, EditViewModel ForegroundEditor)>
        OpenTwoProjectsAsync(string name)
        => await OpenTwoProjectsAsync(
            await CreateClosedProjectAsync($"{name}-background"),
            await CreateClosedProjectAsync($"{name}-foreground"));

    private static async Task<(ProjectSession Background, EditViewModel BackgroundEditor, ProjectSession Foreground, EditViewModel ForegroundEditor)>
        OpenTwoProjectsAsync(string first, string second)
    {
        await TestShell.Project.OpenProject(first);
        HeadlessTestHelpers.Settle();
        ProjectSession background = Registry.ActiveSession.Value!;
        var backgroundEditor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        await TestShell.Project.OpenProject(second);
        HeadlessTestHelpers.Settle();
        ProjectSession foreground = Registry.ActiveSession.Value!;
        var foregroundEditor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        return (background, backgroundEditor, foreground, foregroundEditor);
    }

    private static async Task<string> CreateClosedProjectAsync(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            320, 180, new Rational(30), 44100, name, TestShell.NewWorkspace($"session-scoped-{name}")))!;
        HeadlessTestHelpers.Settle();
        string file = project.Uri!.LocalPath;
        Assert.That(await TestShell.Project.TryCloseProjectAsync(project, ProjectService.ProjectCloseIntent.SaveChanges), Is.True);
        HeadlessTestHelpers.Settle();
        return file;
    }

    private sealed class StubOutputContext(CoreObject obj) : IOutputContext
    {
        private readonly TaskCompletionSource _finish = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCalls { get; private set; }

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public OutputExtension Extension => throw new NotSupportedException();

        public CoreObject Object { get; } = obj;

        public IReactiveProperty<string> Name { get; } = new ReactivePropertySlim<string>("Output");

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _finish.Task.WaitAsync(cancellationToken);
        }

        public void Finish() => _finish.TrySetResult();

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

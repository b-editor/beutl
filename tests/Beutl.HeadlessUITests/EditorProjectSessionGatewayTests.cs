using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class EditorProjectSessionGatewayTests
{
    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static (EditorProjectSessionGateway Gateway, AgentSessionManager Sessions) CreateGateway(
        string? workspaceRoot = null)
    {
        var sessions = new AgentSessionManager();
        var gateway = new EditorProjectSessionGateway(
            TestShell.Project,
            TestShell.Editor,
            new LiveSessionSource(),
            sessions,
            new WorkspaceGuard(workspaceRoot ?? BeutlHomeIsolation.CurrentHome!));
        return (gateway, sessions);
    }

    private static async Task<ReconcileException?> ExpectRejectionAsync(Func<ValueTask<ProjectSessionResult>> action)
    {
        try
        {
            await action();
            Assert.Fail("Expected a ReconcileException rejection.");
            return null;
        }
        catch (ReconcileException ex)
        {
            return ex;
        }
    }

    private static string CreateProjectFilesOnDisk(string name, TimeSpan duration)
    {
        string path = Path.Combine(NewWorkspace(name), $"{name}.bep");
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            path, 640, 360, 30, duration));
        ProjectOperations.Save(project);
        return path;
    }

    [AvaloniaTest]
    public async Task OpenProject_opens_the_project_in_the_editor_and_serves_a_live_session()
    {
        await TestReset.ResetShellAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-open", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, AgentSessionManager sessions) = CreateGateway();

        ProjectSessionResult result = await gateway.OpenProjectAsync(projectFile);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.IsOpened.Value, Is.True);
            Assert.That(BeutlApplication.Current.Project!.Uri!.LocalPath, Is.EqualTo(projectFile));
            Assert.That(result.Session.Source, Is.EqualTo(EditingSessionSource.LiveEditor));
            Assert.That(sessions.CurrentSession, Is.Not.Null);
            Assert.That(sessions.CurrentSession!.SessionId, Is.EqualTo(result.Session.SessionId));
            Assert.That(TestShell.Editor.SelectedTabItem.Value?.Context.Value, Is.InstanceOf<EditViewModel>());
            var editViewModel = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            Assert.That(editViewModel.Scene, Is.SameAs(result.Session.Root));
        });
    }

    [AvaloniaTest]
    public async Task OpenProject_rejects_a_second_different_project()
    {
        await TestReset.ResetShellAsync();
        string first = CreateProjectFilesOnDisk("gateway-first", TimeSpan.FromSeconds(4));
        string second = CreateProjectFilesOnDisk("gateway-second", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(first);
        HeadlessTestHelpers.Settle();

        // Assert.ThrowsAsync blocks the UI thread and deadlocks the headless dispatcher,
        // so the rejection is awaited inline instead.
        ReconcileException? rejection = await ExpectRejectionAsync(() => gateway.OpenProjectAsync(second));

        Assert.Multiple(() =>
        {
            Assert.That(rejection!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejection.Error.Message, Does.Contain("single open project"));
            Assert.That(BeutlApplication.Current.Project!.Uri!.LocalPath, Is.EqualTo(first));
        });
    }

    [AvaloniaTest]
    public async Task OpenProject_with_the_open_projects_path_attaches_without_reopening()
    {
        await TestReset.ResetShellAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-reattach", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(projectFile);
        HeadlessTestHelpers.Settle();
        Project opened = BeutlApplication.Current.Project!;

        ProjectSessionResult reattached = await gateway.OpenProjectAsync(projectFile);

        Assert.Multiple(() =>
        {
            Assert.That(BeutlApplication.Current.Project, Is.SameAs(opened));
            Assert.That(reattached.Session.Source, Is.EqualTo(EditingSessionSource.LiveEditor));
            Assert.That(reattached.Project, Is.SameAs(opened));
        });
    }

    [AvaloniaTest]
    public async Task CreateProject_creates_at_the_requested_path_and_opens_in_editor()
    {
        await TestReset.ResetShellAsync();
        string path = Path.Combine(NewWorkspace("gateway-create"), "fresh.bep");
        (EditorProjectSessionGateway gateway, _) = CreateGateway();

        ProjectSessionResult result = await gateway.CreateProjectAsync(new ProjectCreateOptions(
            path, 800, 450, 24, TimeSpan.FromSeconds(6)));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True);
            Assert.That(BeutlApplication.Current.Project!.Uri!.LocalPath, Is.EqualTo(path));
            Assert.That(result.Session.Source, Is.EqualTo(EditingSessionSource.LiveEditor));
            Scene scene = result.Project.Items.OfType<Scene>().Single();
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(scene.FrameSize.Width, Is.EqualTo(800));
        });
    }

    [AvaloniaTest]
    public async Task CreateProject_rejects_when_a_different_project_is_open()
    {
        await TestReset.ResetShellAsync();
        string first = CreateProjectFilesOnDisk("gateway-create-guard", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(first);
        HeadlessTestHelpers.Settle();
        string second = Path.Combine(NewWorkspace("gateway-create-second"), "other.bep");

        ReconcileException? rejection = await ExpectRejectionAsync(async () =>
            await gateway.CreateProjectAsync(new ProjectCreateOptions(
                second, 640, 360, 30, TimeSpan.FromSeconds(4))));

        Assert.Multiple(() =>
        {
            Assert.That(rejection!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(File.Exists(second), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task CreateProject_rejects_recreating_the_already_open_project()
    {
        await TestReset.ResetShellAsync();
        string openPath = CreateProjectFilesOnDisk("gateway-create-samepath", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(openPath);
        HeadlessTestHelpers.Settle();
        Project openedProject = BeutlApplication.Current.Project!;

        ReconcileException? rejection = await ExpectRejectionAsync(async () =>
            await gateway.CreateProjectAsync(new ProjectCreateOptions(
                openPath, 640, 360, 30, TimeSpan.FromSeconds(4))));

        Assert.Multiple(() =>
        {
            Assert.That(rejection!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            // The open project must not have been replaced on disk or in memory by the create.
            Assert.That(BeutlApplication.Current.Project, Is.SameAs(openedProject));
        });
    }

    [AvaloniaTest]
    public async Task AddScene_adds_saves_and_shows_the_scene()
    {
        await TestReset.ResetShellAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-addscene", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(projectFile);
        HeadlessTestHelpers.Settle();

        ProjectSceneResult added = await gateway.AddSceneAsync(new SceneCreateOptions(
            320, 180, TimeSpan.Zero, TimeSpan.FromSeconds(2), "second-scene"));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(added.Project.Items.OfType<Scene>().Count(), Is.EqualTo(2));
            Assert.That(File.Exists(added.Scene.Uri!.LocalPath), Is.True);
            var editViewModel = TestShell.Editor.SelectedTabItem.Value?.Context.Value as EditViewModel;
            Assert.That(editViewModel?.Scene, Is.SameAs(added.Scene));
            // The live session must be rebound to the newly activated scene, not left on the first.
            Assert.That(added.Session.Root, Is.SameAs(added.Scene));
        });
    }

    [AvaloniaTest]
    public async Task AddScene_rejects_saving_a_project_opened_outside_the_workspace()
    {
        await TestReset.ResetShellAsync();
        // open_project reads anywhere, so the editor can hold a project outside the workspace; add_scene
        // must not persist its sidecars there.
        string outsideDir = Path.Combine(Path.GetTempPath(), "beutl-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDir);
        try
        {
            string outsideProject = Path.Combine(outsideDir, "outside.bep");
            Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
                outsideProject, 640, 360, 30, TimeSpan.FromSeconds(4)));
            ProjectOperations.Save(project);
            (EditorProjectSessionGateway gateway, _) = CreateGateway();
            await gateway.OpenProjectAsync(outsideProject);
            HeadlessTestHelpers.Settle();

            WorkspaceBoundaryException? rejection = null;
            try
            {
                await gateway.AddSceneAsync(new SceneCreateOptions(
                    320, 180, TimeSpan.Zero, TimeSpan.FromSeconds(2), "second-scene"));
                Assert.Fail("Expected a workspace-boundary rejection.");
            }
            catch (WorkspaceBoundaryException ex)
            {
                rejection = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(rejection, Is.Not.Null);
                // The boundary guard runs before the live project is mutated, so a rejected add_scene
                // leaves no unsaved extra scene behind in the editor.
                Assert.That(BeutlApplication.Current.Project!.Items.OfType<Scene>().Count(), Is.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(outsideDir, true);
        }
    }
}

using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class SessionToolsTests
{
    [Test]
    public async Task Create_project_starts_file_backed_session_for_document_tools()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);
        var queryTools = new QueryTools(manager);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "motion.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        ToolResult<DocumentSummaryResponse> summary = queryTools.ReadDocumentSummary();
        ToolResult<SaveProjectResponse> saved = sessionTools.SaveProject(created.Value!.Session);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(created.Value!.SavedPath), Is.True);
            Assert.That(created.Value.Summary.Scenes, Has.Count.EqualTo(1));
            Assert.That(summary.IsSuccess, Is.True, summary.Error?.Message);
            Assert.That(summary.Value!.Source, Is.EqualTo("File"));
            Assert.That(summary.Value.Width, Is.EqualTo(640));
            Assert.That(summary.Value.Height, Is.EqualTo(360));
            Assert.That(summary.Value.Duration, Is.EqualTo("00:00:04"));
            Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
            Assert.That(saved.Value!.SavedPath, Is.EqualTo(created.Value.SavedPath));
        });
    }

    [Test]
    public async Task Create_project_existing_path_requires_confirmation()
    {
        string root = CreateWorkspace();
        string path = Path.Combine(root, "exists.bep");
        File.WriteAllText(path, "{}");
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);

        ToolResult<CreateProjectResponse> result = await sessionTools.CreateProject(
            "exists.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.DestructiveIntent));
        });
    }

    [Test]
    public async Task Create_project_does_not_overwrite_existing_default_scene_sidecar()
    {
        string root = CreateWorkspace();
        string existingDir = Path.Combine(root, "demo");
        Directory.CreateDirectory(existingDir);
        string existingSidecar = Path.Combine(existingDir, "demo.scene");
        File.WriteAllText(existingSidecar, "existing scene sidecar");
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "demo.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        Scene scene = source.CurrentFileSession!.Project.Items.OfType<Scene>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
            Assert.That(File.ReadAllText(existingSidecar), Is.EqualTo("existing scene sidecar"));
            Assert.That(scene.Uri!.LocalPath, Is.Not.EqualTo(existingSidecar));
            Assert.That(File.Exists(scene.Uri.LocalPath), Is.True);
            Assert.That(Path.GetFileName(Path.GetDirectoryName(scene.Uri.LocalPath)!), Is.EqualTo("demo-2"));
        });
    }

    [Test]
    public async Task Create_project_rejects_package_extension_and_appends_missing_project_extension()
    {
        string root = CreateWorkspace();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);

        ToolResult<CreateProjectResponse> rejected = await sessionTools.CreateProject(
            "wrong.beutl",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");
        ToolResult<CreateProjectResponse> normalized = await sessionTools.CreateProject(
            "motion",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        Assert.Multiple(() =>
        {
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejected.Error.Message, Does.Contain(".bep"));
            Assert.That(rejected.Error.Message, Does.Contain(".beutl"));
            Assert.That(normalized.IsSuccess, Is.True, normalized.Error?.Message);
            Assert.That(Path.GetExtension(normalized.Value!.SavedPath), Is.EqualTo(".bep"));
        });
    }

    [Test]
    public async Task Save_project_rejects_package_extension()
    {
        string root = CreateWorkspace();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);
        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "motion.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        ToolResult<SaveProjectResponse> rejected = sessionTools.SaveProject(created.Value!.Session, "package.beutl");

        Assert.Multiple(() =>
        {
            Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        });
    }

    [Test]
    public async Task Save_project_uses_current_file_session_when_session_is_omitted()
    {
        string root = CreateWorkspace();
        using var source = new FileSessionSource();
        var manager = new AgentSessionManager();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);
        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "sessionless-save.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        ToolResult<SaveProjectResponse> saved = sessionTools.SaveProject();

        Assert.Multiple(() =>
        {
            Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
            Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
            Assert.That(saved.Value!.Session, Is.EqualTo(created.Value!.Session));
            Assert.That(saved.Value.SavedPath, Is.EqualTo(created.Value.SavedPath));
            Assert.That(File.Exists(created.Value.SavedPath), Is.True);
        });
    }

    [Test]
    public void Save_project_reports_live_editor_sessions_as_not_required()
    {
        string root = CreateWorkspace();
        using var liveSession = new AgentToolkitTestSession(new Scene(), EditingSessionSource.LiveEditor);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(liveSession));
        using var fileSource = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(fileSource, manager, root);

        ToolResult<SaveProjectResponse> saved = sessionTools.SaveProject(liveSession.SessionId);
        ToolResult<OperationStatusResponse> status = sessionTools.ReadOperationStatus();

        Assert.Multiple(() =>
        {
            Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
            Assert.That(saved.Value!.Saved, Is.False);
            Assert.That(saved.Value.Source, Is.EqualTo(nameof(EditingSessionSource.LiveEditor)));
            Assert.That(saved.Value.Message, Does.Contain("save_project is not required or supported"));
            Assert.That(status.IsSuccess, Is.True, status.Error?.Message);
            Assert.That(status.Value!.SaveProjectSupported, Is.False);
            Assert.That(status.Value.Source, Is.EqualTo(nameof(EditingSessionSource.LiveEditor)));
        });
    }

    private static SessionTools CreateSessionTools(FileSessionSource source, AgentSessionManager manager, string root)
    {
        var workspace = new WorkspaceGuard(root);
        return new SessionTools(
            new FileProjectSessionGateway(source, manager, workspace),
            manager,
            workspace,
            new DestructiveGuard(),
            new RenderJobManager());
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

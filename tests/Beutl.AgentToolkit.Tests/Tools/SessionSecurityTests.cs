using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class SessionSecurityTests
{
    [Test]
    public async Task Add_scene_rejects_path_traversal_names()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            Path.Combine(root, "traversal.bep"), width: 320, height: 180, frameRate: 30, duration: "00:00:02");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        ToolResult<AddSceneResponse> added = await sessionTools.AddScene(
            created.Value!.Session, width: 320, height: 180, start: "00:00:00", duration: "00:00:01",
            name: Path.Combine("..", "escape"));

        Assert.Multiple(() =>
        {
            Assert.That(added.IsSuccess, Is.False);
            Assert.That(added.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        });
    }

    [Test]
    public async Task Open_project_rejects_a_relative_path()
    {
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager);

        ToolResult<OpenProjectResponse> opened = await sessionTools.OpenProject("relative.bep");

        Assert.Multiple(() =>
        {
            Assert.That(opened.IsSuccess, Is.False);
            Assert.That(opened.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(manager.CurrentSession, Is.Null);
        });
    }

    [Test]
    public void Create_project_rejects_a_relative_extensionless_path()
    {
        string escaping = Path.Combine("..", "escape-project");

        ToolError error = Assert.Throws<ReconcileException>(() =>
            SessionTools.NormalizeProjectPath(escaping, "path"))!.Error;

        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(error.Target, Is.EqualTo("path"));
        });
    }

    [Test]
    public void Create_project_appends_the_extension_to_an_absolute_extensionless_path()
    {
        string root = CreateWorkspace();

        string normalized = SessionTools.NormalizeProjectPath(Path.Combine(root, "promo"), "path");

        Assert.That(FilePathComparison.AreSameCanonicalPath(normalized, Path.Combine(root, "promo.bep")), Is.True);
    }

    [Test]
    public async Task Save_project_to_a_new_path_does_not_report_a_conflict()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            Path.Combine(root, "original.bep"), width: 320, height: 180, frameRate: 30, duration: "00:00:02");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        ToolResult<SaveProjectResponse> savedAs = sessionTools.SaveProject(
            created.Value!.Session, Path.Combine(root, "copy.bep"));

        Assert.Multiple(() =>
        {
            Assert.That(savedAs.IsSuccess, Is.True, savedAs.Error?.Message);
            Assert.That(savedAs.Value!.Saved, Is.True);
            Assert.That(savedAs.Value.SavedPath, Does.EndWith("copy.bep"));
            Assert.That(File.Exists(savedAs.Value.SavedPath), Is.True);
        });
    }

    [Test]
    public void Read_operation_status_reports_a_running_background_job()
    {
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        using var renderJobs = new RenderJobManager();
        var sessionTools = new SessionTools(
            new FileProjectSessionGateway(source, manager),
            manager,
            new DestructiveGuard(),
            renderJobs);

        var gate = new TaskCompletionSource();
        string jobId = renderJobs.Enqueue("test", async (_, _) =>
        {
            await gate.Task;
            return (JsonNode)JsonValue.Create(true);
        }, StandaloneOutputOperationLeaseProvider.Instance.TryBeginOutputOperation()!);

        try
        {
            Assert.That(SpinWait.SpinUntil(() => renderJobs.Get(jobId)?.State == "running", 2000), Is.True);

            ToolResult<OperationStatusResponse> status = sessionTools.ReadOperationStatus();

            Assert.Multiple(() =>
            {
                Assert.That(status.IsSuccess, Is.True, status.Error?.Message);
                Assert.That(status.Value!.HasLongRunningOperation, Is.True);
            });
        }
        finally
        {
            gate.SetResult();
        }
    }

    [Test]
    public async Task Save_as_relocates_scene_sidecars_under_the_new_project()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            Path.Combine(root, "original.bep"), width: 320, height: 180, frameRate: 30, duration: "00:00:02");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
        string originalSceneDir = Path.GetDirectoryName(((Scene)manager.RequireSession().Root).Uri!.LocalPath)!;

        ToolResult<SaveProjectResponse> savedAs = sessionTools.SaveProject(created.Value!.Session, Path.Combine(root, "copy.bep"));
        Assert.That(savedAs.IsSuccess, Is.True, savedAs.Error?.Message);

        var scene = (Scene)manager.RequireSession().Root;
        string projectDir = Path.GetDirectoryName(savedAs.Value!.SavedPath)!;

        Assert.Multiple(() =>
        {
            // The copy's scene sidecar lives under a directory named for the new project, not the
            // source project's scene directory (which would corrupt the original on save).
            Assert.That(scene.Uri!.LocalPath, Does.StartWith(Path.Combine(projectDir, "copy")));
            Assert.That(scene.Uri.LocalPath, Does.Not.StartWith(originalSceneDir));
            Assert.That(File.Exists(scene.Uri.LocalPath), Is.True);
        });
    }

    [Test]
    public async Task Add_scene_switches_the_file_session_to_the_new_scene()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            Path.Combine(root, "switch.bep"), width: 320, height: 180, frameRate: 30, duration: "00:00:04");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        ToolResult<AddSceneResponse> added = await sessionTools.AddScene(
            created.Value!.Session, width: 320, height: 180, start: "00:00:00", duration: "00:00:02", name: "second");
        Assert.That(added.IsSuccess, Is.True, added.Error?.Message);

        var active = (Scene)manager.RequireSession().Root;

        Assert.Multiple(() =>
        {
            Assert.That(active.Id.ToString(), Is.EqualTo(added.Value!.SceneId));
            Assert.That(active.Name, Is.EqualTo("second"));
        });
    }

    private static SessionTools CreateSessionTools(FileSessionSource source, AgentSessionManager manager)
    {
        return new SessionTools(
            new FileProjectSessionGateway(source, manager),
            manager,
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

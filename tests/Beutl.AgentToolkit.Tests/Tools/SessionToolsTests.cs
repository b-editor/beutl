using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class SessionToolsTests
{
    [Test]
    public void Create_project_starts_file_backed_session_for_document_tools()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        var sessionTools = new SessionTools(source, manager, new WorkspaceGuard(root), new DestructiveGuard());
        var queryTools = new QueryTools(manager);

        ToolResult<CreateProjectResponse> created = sessionTools.CreateProject(
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
    public void Create_project_existing_path_requires_confirmation()
    {
        string root = CreateWorkspace();
        string path = Path.Combine(root, "exists.bep");
        File.WriteAllText(path, "{}");
        using var source = new FileSessionSource();
        var sessionTools = new SessionTools(
            source,
            new AgentSessionManager(),
            new WorkspaceGuard(root),
            new DestructiveGuard());

        ToolResult<CreateProjectResponse> result = sessionTools.CreateProject(
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

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

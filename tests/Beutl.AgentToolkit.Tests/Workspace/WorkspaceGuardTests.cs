using Beutl.AgentToolkit.Workspace;

namespace Beutl.AgentToolkit.Tests.Workspace;

public class WorkspaceGuardTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void ResolveForWrite_AllowsPathInsideRoot()
    {
        var guard = new WorkspaceGuard(_root);

        string resolved = guard.ResolveForWrite("renders/preview.png");

        Assert.That(resolved, Does.StartWith(Path.GetFullPath(_root)));
        Assert.That(resolved, Does.EndWith(Path.Combine("renders", "preview.png")));
    }

    [Test]
    public void ResolveForWrite_RejectsParentEscape()
    {
        var guard = new WorkspaceGuard(_root);

        Assert.Throws<WorkspaceBoundaryException>(() => guard.ResolveForWrite("../outside.png"));
    }

    [Test]
    public async Task CreateProject_RejectsADistinctCaseVariantDirectory()
    {
        string inside = Path.Combine(_root, "Workspace");
        string outside = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(inside);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(inside, "inside.txt"), "inside");
        if (File.Exists(Path.Combine(outside, "inside.txt")))
            Assert.Ignore("Requires a case-sensitive filesystem.");

        var workspace = new WorkspaceGuard(inside);
        var manager = new Beutl.AgentToolkit.Sessions.AgentSessionManager();
        using var source = new Beutl.AgentToolkit.Sessions.FileSessionSource();
        using var jobs = new Beutl.AgentToolkit.Rendering.RenderJobManager();
        var tools = new Beutl.AgentToolkit.Tools.SessionTools(
            new Beutl.AgentToolkit.Sessions.FileProjectSessionGateway(source, manager, workspace),
            manager, workspace, new DestructiveGuard(), jobs);
        string target = Path.Combine(outside, "outside.bep");

        var result = await tools.CreateProject(target, 64, 64, 30, "00:00:01");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("workspace_boundary"));
        Assert.That(File.Exists(target), Is.False);
        Assert.That(Directory.GetFileSystemEntries(outside), Is.Empty);
    }

    [Test]
    public void ResolveForWrite_AllowsACaseAliasOnACaseInsensitiveFilesystem()
    {
        string inside = Directory.CreateDirectory(Path.Combine(_root, "Workspace")).FullName;
        File.WriteAllText(Path.Combine(inside, "inside.txt"), "inside");
        string alias = Path.Combine(_root, "workspace");
        if (!File.Exists(Path.Combine(alias, "inside.txt")))
            Assert.Ignore("Requires a case-insensitive filesystem.");

        string resolved = new WorkspaceGuard(inside).ResolveForWrite(Path.Combine(alias, "new.png"));

        Assert.That(resolved, Is.EqualTo(Path.Combine(inside, "new.png")));
    }

    [Test]
    public void ResolveForWrite_RejectsInRootSymlinkToOutside()
    {
        var outside = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        string link = Path.Combine(_root, "link");

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("Symlink creation is not available in this environment.");
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("Symlink creation is not available in this environment.");
        }

        try
        {
            var guard = new WorkspaceGuard(_root);
            Assert.Throws<WorkspaceBoundaryException>(() => guard.ResolveForWrite(Path.Combine("link", "escape.png")));
        }
        finally
        {
            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
    }
}

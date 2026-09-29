using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class ProjectPathIdentityTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SaveProject_ToACaseDistinctPath_PreservesTheOriginal(bool destinationExists)
    {
        string root = Directory.CreateTempSubdirectory("project-path-").FullName;
        try
        {
            string original = Path.Combine(root, "Project.bep");
            string destination = Path.Combine(root, "project.bep");
            using var source = new FileSessionSource();
            var manager = new AgentSessionManager();
            var workspace = new WorkspaceGuard(root);
            using var jobs = new RenderJobManager();
            var tools = new SessionTools(
                new FileProjectSessionGateway(source, manager, workspace),
                manager, workspace, new DestructiveGuard(), jobs);
            var created = await tools.CreateProject(original, 64, 64, 30, "00:00:01");
            Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
            if (File.Exists(destination))
                Assert.Ignore("Requires a case-sensitive filesystem.");
            byte[] originalBytes = File.ReadAllBytes(original);
            source.CurrentFileSession!.Project.Name = "Independent copy";
            source.CurrentFileSession.MarkDirty();

            if (destinationExists)
            {
                CoreSerializer.StoreToUri(new Project { Name = "Existing destination" }, new Uri(destination));
                byte[] previousDestination = File.ReadAllBytes(destination);
                var rejected = tools.SaveProject(path: destination);
                Assert.That(rejected.IsSuccess, Is.False, "A distinct existing destination requires confirmation.");
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(previousDestination));
                Assert.That(File.ReadAllBytes(original), Is.EqualTo(originalBytes));
            }

            var saved = tools.SaveProject(path: destination, confirmOverwrite: destinationExists);

            Assert.Multiple(() =>
            {
                Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
                Assert.That(File.ReadAllBytes(original), Is.EqualTo(originalBytes));
                Assert.That(File.Exists(destination), Is.True);
                Assert.That(FilePathComparison.AreSameCanonicalPath(saved.Value!.SavedPath, destination), Is.True);
                Assert.That(CoreSerializer.RestoreFromUri<Project>(new Uri(destination)).Name, Is.EqualTo("Independent copy"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

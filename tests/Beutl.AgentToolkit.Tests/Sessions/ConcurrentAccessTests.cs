using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class ConcurrentAccessTests
{
    [Test]
    public void Save_detects_project_modified_after_open()
    {
        string root = TestWorkspace.CreateDirectory();
        string projectPath = Path.Combine(root, "conflict.bep");
        using var source = new FileSessionSource();
        FileEditingSession session = TestWorkspace.CreateFileSession(source, new ProjectCreateOptions(
            projectPath,
            320,
            180,
            new Rational(30),
            TimeSpan.FromSeconds(1),
            Name: "conflict"));
        session.Save(skipConflictCheck: true);

        File.SetLastWriteTimeUtc(projectPath, DateTime.UtcNow.AddSeconds(10));

        Assert.Throws<ProjectConflictException>(() => session.Save(skipConflictCheck: false));
    }

    [Test]
    public void Save_detects_element_edits_saved_by_another_session_and_allows_save_after_reload()
    {
        string root = TestWorkspace.CreateDirectory();
        try
        {
            string projectPath = Path.Combine(root, "conflict.bep");
            Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
                projectPath, 320, 180, new Rational(30), TimeSpan.FromSeconds(3)));
            Scene scene = project.Items.OfType<Scene>().Single();
            Element firstElement = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(1), "first.belm");
            Element secondElement = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), "second.belm");
            firstElement.Name.Value = "Original first";
            secondElement.Name.Value = "Original second";
            ProjectOperations.Save(project);
            // Keep the initial revision distinct even on filesystems with coarse timestamp precision.
            File.SetLastWriteTimeUtc(projectPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            using var firstSource = new FileSessionSource();
            using var secondSource = new FileSessionSource();
            FileEditingSession first = firstSource.OpenProject(projectPath);
            FileEditingSession second = secondSource.OpenProject(projectPath);
            second.Scene.Children.Single(e => e.Id.Value == firstElement.Id.Value).Name.Value = "Saved by second session";
            second.Save(skipConflictCheck: false);

            first.Scene.Children.Single(e => e.Id.Value == secondElement.Id.Value).Name.Value = "Saved by first session";
            Assert.Throws<ProjectConflictException>(() => first.Save(skipConflictCheck: false));
            Assert.That(CoreSerializer.RestoreFromUri<Element>(firstElement.Uri!).Name.Value,
                Is.EqualTo("Saved by second session"));
            Assert.That(CoreSerializer.RestoreFromUri<Element>(secondElement.Uri!).Name.Value,
                Is.EqualTo("Original second"));

            FileEditingSession refreshed = firstSource.OpenProject(projectPath);
            refreshed.Scene.Children.Single(e => e.Id.Value == secondElement.Id.Value).Name.Value = "Saved after reload";
            Assert.DoesNotThrow(() => refreshed.Save(skipConflictCheck: false));
            Assert.DoesNotThrow(() => refreshed.Save(skipConflictCheck: false));
            Assert.That(CoreSerializer.RestoreFromUri<Element>(firstElement.Uri!).Name.Value,
                Is.EqualTo("Saved by second session"));
            Assert.That(CoreSerializer.RestoreFromUri<Element>(secondElement.Uri!).Name.Value,
                Is.EqualTo("Saved after reload"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

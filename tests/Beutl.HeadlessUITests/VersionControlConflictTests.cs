using Avalonia.Headless.NUnit;
using Beutl.ProjectSystem;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlConflictTests
{
    [AvaloniaTest]
    public async Task Opening_project_warns_before_loading_files_with_conflict_markers()
    {
        await TestReset.ResetShellAsync();
        string location = Path.Combine(
            Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
            "conflict-marker-open");
        Directory.CreateDirectory(location);
        Project project = (await TestShell.Project.CreateProject(
            640,
            480,
            30,
            44100,
            "project",
            location))!;
        string projectFile = project.Uri!.LocalPath;
        await TestShell.Project.CloseProject();
        string markerFile = Path.Combine(
            Path.GetDirectoryName(projectFile)!,
            "conflicted.belm");
        await File.WriteAllTextAsync(
            markerFile,
            "<<<<<<< ours\n{}\n=======\n{}\n>>>>>>> theirs\n");

        Func<string, Task> previousWarning
            = TestShell.VersionControl.WarnConflictMarkersAsync;
        string? warnedFile = null;
        bool projectWasClosedAtWarning = false;
        TestShell.VersionControl.WarnConflictMarkersAsync = file =>
        {
            projectWasClosedAtWarning = TestShell.Project.CurrentProject.Value is null;
            warnedFile = file;
            return Task.CompletedTask;
        };
        try
        {
            await TestShell.Project.OpenProject(projectFile);

            Assert.Multiple(() =>
            {
                Assert.That(warnedFile, Is.EqualTo(markerFile));
                Assert.That(
                    projectWasClosedAtWarning,
                    Is.True,
                    "the warning must run before project loading starts");
                Assert.That(
                    TestShell.Project.CurrentProject.Value?.Uri?.LocalPath,
                    Is.EqualTo(projectFile));
            });
        }
        finally
        {
            TestShell.VersionControl.WarnConflictMarkersAsync = previousWarning;
            await TestReset.ResetShellAsync();
        }
    }
}

using Avalonia.Headless.NUnit;
using Beutl.ProjectSystem;
using Beutl.Services.Tutorials;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// A tutorial started without an open project creates its project under the Beutl home, which BEUTL_HOME moves
// (the tests run with it set), like every other folder Beutl writes.
[TestFixture]
public sealed class TutorialProjectLocationTests
{
    [AvaloniaTest]
    public async Task A_tutorial_project_is_created_in_the_Beutl_home()
    {
        await TestReset.ResetShellAsync();
        try
        {
            Assert.That(await TutorialHelpers.EnsureProjectAsync(TestShell.Project, TestShell.Editor), Is.True);

            Project project = TestShell.Project.CurrentProject.Value!;
            string tutorials = Path.Combine(BeutlHomeIsolation.CurrentHome!, "tmp", "tutorials");
            Assert.That(
                Directory.EnumerateFiles(tutorials, Path.GetFileName(project.Uri!.LocalPath), SearchOption.AllDirectories),
                Is.Not.Empty);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }
}

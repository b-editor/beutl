using System.Diagnostics;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ProjectLifecyclePerformanceTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Empty_project_create_open_and_close_preserve_the_project(bool trackHistory)
    {
        await TestReset.ResetShellAsync();
        var config = GlobalConfiguration.Instance.VersionControlConfig;
        string? oldGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        string? oldNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        string? oldGitPath = config.GitExecutablePath;
        bool oldLfs = config.UseLfsWhenAvailable;
        bool oldClose = config.AutoCommitOnClose;
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        try
        {
            config.GitExecutablePath = null;
            config.UseLfsWhenAvailable = false;
            config.AutoCommitOnClose = true;
            if (trackHistory && (await TestShell.VersionControl.GetAvailabilityAsync(CancellationToken.None)).State
                != GitAvailabilityState.Installed)
            {
                Assert.Ignore("Git is unavailable.");
            }

            string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"lifecycle-{trackHistory}-{Guid.NewGuid():N}");
            var watch = Stopwatch.StartNew();
            Project project = (await TestShell.Project.CreateProject(1920, 1080, 30, 44100, "Empty", location))!;
            TestContext.Progress.WriteLine($"LIFECYCLE git={trackHistory} create={watch.Elapsed.TotalMilliseconds:F1}ms");
            Assert.That(project, Is.Not.Null);
            Assert.That(TestShell.Editor.TabItems.Count, Is.EqualTo(1));
            string file = project.Uri!.LocalPath;
            if (trackHistory)
            {
                watch.Restart();
                Assert.That(await TestShell.VersionControl.InitializeCurrentProjectAsync(project,
                    _ => Task.FromResult<GitIdentity?>(new("Lifecycle Test", "lifecycle@example.invalid"))), Is.True);
                TestContext.Progress.WriteLine($"LIFECYCLE git={trackHistory} initialize={watch.Elapsed.TotalMilliseconds:F1}ms");
            }

            watch.Restart();
            await TestShell.Project.CloseProjectAsync();
            TestContext.Progress.WriteLine($"LIFECYCLE git={trackHistory} first-close={watch.Elapsed.TotalMilliseconds:F1}ms");
            Assert.That(TestShell.Editor.TabItems, Is.Empty);
            watch.Restart();
            await TestShell.Project.OpenProject(file);
            TestContext.Progress.WriteLine($"LIFECYCLE git={trackHistory} open={watch.Elapsed.TotalMilliseconds:F1}ms");
            Assert.That(TestShell.Project.CurrentProject.Value?.Uri?.LocalPath, Is.EqualTo(file));
            watch.Restart();
            await TestShell.Project.CloseProjectAsync();
            TestContext.Progress.WriteLine($"LIFECYCLE git={trackHistory} second-close={watch.Elapsed.TotalMilliseconds:F1}ms");
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(File.Exists(file), Is.True);
        }
        finally
        {
            await TestShell.Project.CloseProjectAsync();
            config.GitExecutablePath = oldGitPath;
            config.UseLfsWhenAvailable = oldLfs;
            config.AutoCommitOnClose = oldClose;
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", oldGlobal);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", oldNoSystem);
        }
    }
}

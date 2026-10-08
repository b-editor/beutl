using System.Diagnostics;

namespace Beutl.UnitTests.Build;

public class UnixUpdaterTests
{
    [Test]
    [TestCase("linux", "none")]
    [TestCase("linux", "backup")]
    [TestCase("linux", "install")]
    [TestCase("linux", "restore")]
    [TestCase("linux", "remove")]
    [TestCase("osx", "none")]
    [TestCase("osx", "backup")]
    [TestCase("osx", "install")]
    [TestCase("osx", "restore")]
    public async Task Update_preserves_a_working_app_or_its_backup_and_releases_its_lock(string platform, string failure)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Exercises the Unix updater through bash.");
        string root = Directory.CreateTempSubdirectory("beutl-update-script-").FullName;
        string processName = "beutl-test-" + Guid.NewGuid().ToString("N");
        string lockDirectory = "/tmp/" + processName + "_update.lock";
        try
        {
            string original = Directory.CreateDirectory(Path.Combine(root, "old app")).FullName;
            string update = Directory.CreateDirectory(Path.Combine(root, "new app")).FullName;
            File.WriteAllText(Path.Combine(original, "Beutl"), "original");
            File.WriteAllText(Path.Combine(update, "Beutl"), "updated");
            string bin = Directory.CreateDirectory(Path.Combine(root, "bin")).FullName;
            WriteExecutable(Path.Combine(bin, "sleep"), "#!/bin/bash\nexit 0\n");
            WriteExecutable(Path.Combine(bin, "pgrep"), "#!/bin/bash\nexit 1\n");
            WriteExecutable(Path.Combine(bin, "osascript"), "#!/bin/bash\nexit 0\n");
            WriteExecutable(Path.Combine(bin, "rm"), """
                #!/bin/bash
                if [ "$TEST_FAILURE" = remove ] && [ "$2" = "$TEST_ORIGINAL" ] && [ -f "$2/incomplete-new-file" ]; then
                    exit 13
                fi
                exec /bin/rm "$@"
                """ + "\n");
            WriteExecutable(Path.Combine(bin, "cp"), """
                #!/bin/bash
                if [ "$TEST_FAILURE" = backup ] && [ "$2" = "$TEST_ORIGINAL" ]; then
                    exit 28
                fi
                if [ "$TEST_FAILURE" != none ] && [ "$2" = "$TEST_UPDATE" ]; then
                    mkdir -p "$3"
                    printf partial > "$3/incomplete-new-file"
                    exit 28
                fi
                if [ "$TEST_FAILURE" = restore ] && [[ "$2" = "${TEST_ORIGINAL}_backup_"* ]]; then
                    exit 28
                fi
                exec /bin/cp "$@"
                """ + "\n");
            WriteExecutable(Path.Combine(bin, "ditto"), """
                #!/bin/bash
                if [ "$TEST_FAILURE" = backup ] && [ "$1" = "$TEST_ORIGINAL" ]; then
                    exit 28
                fi
                if [ "$TEST_FAILURE" != none ] && [ "$1" = "$TEST_UPDATE" ]; then
                    mkdir -p "$2"
                    printf partial > "$2/incomplete-new-file"
                    exit 28
                fi
                if [ "$TEST_FAILURE" = restore ] && [[ "$1" = "${TEST_ORIGINAL}_backup_"* ]]; then
                    exit 28
                fi
                if [ -x /usr/bin/ditto ]; then
                    exec /usr/bin/ditto "$@"
                fi
                # Model ditto's merge semantics when running the macOS script on Linux CI.
                mkdir -p "$2"
                exec /bin/cp -R "$1/." "$2"
                """ + "\n");
            var start = new ProcessStartInfo("/bin/bash")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { FindScript(platform), update, original, processName, Path.Combine(original, "Beutl") },
            };
            start.Environment["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment["TEST_FAILURE"] = failure;
            start.Environment["TEST_ORIGINAL"] = original;
            start.Environment["TEST_UPDATE"] = update;
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            try
            {
                try { await process.StandardInput.WriteLineAsync("n"); }
                finally { process.StandardInput.Close(); }
            }
            catch (IOException)
            {
                // Only the Linux success path reads the launch prompt. Every other path may exit
                // first, and then both the write and Close's flush fail with EPIPE. The exit code
                // and file assertions below still report an early exit.
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            string output = await stdout + await stderr;
            string[] backups = Directory.GetDirectories(root, "old app_backup_*");

            Assert.Multiple(() =>
            {
                Assert.That(process.ExitCode, failure == "none" ? Is.Zero : Is.Not.Zero, output);
                Assert.That(Directory.Exists(lockDirectory), Is.False, output);
                if (failure is not ("restore" or "remove"))
                {
                    Assert.That(File.Exists(Path.Combine(original, "Beutl")), Is.True, output);
                    if (File.Exists(Path.Combine(original, "Beutl")))
                        Assert.That(File.ReadAllText(Path.Combine(original, "Beutl")),
                            Is.EqualTo(failure == "none" ? "updated" : "original"));
                    Assert.That(File.Exists(Path.Combine(original, "incomplete-new-file")), Is.False);
                }
                if (failure is "install" or "restore" or "remove")
                {
                    Assert.That(backups, Has.Length.EqualTo(1));
                    if (backups.Length == 1)
                        Assert.That(File.ReadAllText(Path.Combine(backups[0], "Beutl")), Is.EqualTo("original"));
                }
                if (failure == "remove")
                {
                    Assert.That(Directory.GetDirectories(original, "old app_backup_*"), Is.Empty);
                    Assert.That(File.Exists(Path.Combine(original, "incomplete-new-file")), Is.True);
                }
                if (failure == "none") Assert.That(backups, Is.Empty);
            });
        }
        finally
        {
            if (Directory.Exists(lockDirectory)) Directory.Delete(lockDirectory, true);
            Directory.Delete(root, true);
        }
    }

    private static void WriteExecutable(string path, string script)
    {
        File.WriteAllText(path, script.ReplaceLineEndings("\n"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static string FindScript(string platform)
    {
        for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
             directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "src", "Beutl", "Resources", $"{platform}-update.sh");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException($"Could not find the {platform} update script.");
    }
}

using System.Diagnostics;

namespace Beutl.UnitTests.Build;

public class LinuxUpdaterTests
{
    [Test]
    [TestCase("none")]
    [TestCase("backup")]
    [TestCase("install")]
    [TestCase("restore")]
    public async Task Update_preserves_a_working_app_or_its_backup_and_releases_its_lock(string failure)
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
            var start = new ProcessStartInfo("/bin/bash")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { FindScript(), update, original, processName, Path.Combine(original, "Beutl") },
            };
            start.Environment["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment["TEST_FAILURE"] = failure;
            start.Environment["TEST_ORIGINAL"] = original;
            start.Environment["TEST_UPDATE"] = update;
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync("n");
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            string output = await stdout + await stderr;
            string[] backups = Directory.GetDirectories(root, "old app_backup_*");

            Assert.Multiple(() =>
            {
                Assert.That(process.ExitCode, failure == "none" ? Is.Zero : Is.Not.Zero, output);
                Assert.That(Directory.Exists(lockDirectory), Is.False, output);
                if (failure != "restore")
                {
                    Assert.That(File.Exists(Path.Combine(original, "Beutl")), Is.True, output);
                    if (File.Exists(Path.Combine(original, "Beutl")))
                        Assert.That(File.ReadAllText(Path.Combine(original, "Beutl")),
                            Is.EqualTo(failure == "none" ? "updated" : "original"));
                    Assert.That(File.Exists(Path.Combine(original, "incomplete-new-file")), Is.False);
                }
                if (failure is "install" or "restore")
                {
                    Assert.That(backups, Has.Length.EqualTo(1));
                    if (backups.Length == 1)
                        Assert.That(File.ReadAllText(Path.Combine(backups[0], "Beutl")), Is.EqualTo("original"));
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

    private static string FindScript()
    {
        for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
             directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "src", "Beutl", "Resources", "linux-update.sh");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Could not find the Linux update script.");
    }
}

using System.Diagnostics;
using Beutl.Editor.VersionControl;
using static Beutl.UnitTests.Editor.VersionControl.UnixProcessTestMethods;

namespace Beutl.UnitTests.Editor.VersionControl;

// The Process-based fallback, as it behaves on Unix: it owns no group, the tree is killed while the
// command is alive, and only the command itself is waited for.
[TestFixture]
public class ManagedGitProcessTests
{
    [SetUp]
    public void RequireUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("These tests start a Unix shell; on Windows the fallback also walks the tree after exit.");
        }
    }

    [Test]
    public async Task Command_receives_input_and_reports_its_output_and_exit_code()
    {
        using ManagedGitProcess process = ManagedGitProcess.Start(CreateShell("cat; printf 'diagnostic' >&2; exit 3"));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync("入力");
        process.StandardInput.Close();
        await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(process.OwnsDescendants, Is.False);
            Assert.That(process.Id, Is.Positive);
            Assert.That(stdout.Result, Is.EqualTo("入力"));
            Assert.That(stderr.Result, Is.EqualTo("diagnostic"));
            Assert.That(process.TryGetExitCode(out int exitCode), Is.True);
            Assert.That(exitCode, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Kill_ends_a_running_command_and_its_children()
    {
        string pidPath = Path.Combine(Path.GetTempPath(), $"beutl-managed-child-{Guid.NewGuid():N}.pid");
        try
        {
            ProcessStartInfo startInfo = CreateShell("sleep 30 & printf '%s' \"$!\" > \"$1\"; wait", pidPath);
            using ManagedGitProcess process = ManagedGitProcess.Start(startInfo);
            int child = await WaitForRecordedProcessIdAsync(pidPath);

            process.Kill();
            process.CloseStandardStreams();
            await process.WaitForGroupExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            bool childGone = await WaitUntilGoneAsync(child);

            Assert.Multiple(() =>
            {
                Assert.That(process.TryGetExitCode(out int exitCode), Is.True);
                Assert.That(exitCode, Is.EqualTo(128 + 9));
                Assert.That(childGone, Is.True);
            });
        }
        finally
        {
            File.Delete(pidPath);
        }
    }

    [Test]
    public async Task Kill_after_the_command_exited_does_nothing()
    {
        using ManagedGitProcess process = ManagedGitProcess.Start(CreateShell("exit 0"));
        process.StandardInput.Close();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotThrow(process.Kill);
        await process.WaitForGroupExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(process.TryGetExitCode(out int exitCode) && exitCode == 0, Is.True);
    }

    private static ProcessStartInfo CreateShell(string script, string? argument = null)
    {
        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("sh");
        if (argument is not null)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<int> WaitForRecordedProcessIdAsync(string pidPath)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (File.Exists(pidPath) && int.TryParse(await File.ReadAllTextAsync(pidPath), out int pid))
            {
                return pid;
            }

            await Task.Delay(10);
        }

        Assert.Fail("The child did not record its process id.");
        return 0;
    }

    private static async Task<bool> WaitUntilGoneAsync(int pid)
    {
        var stopwatch = Stopwatch.StartNew();
        while (IsSignalable(pid))
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
            {
                return false;
            }

            await Task.Delay(10);
        }

        return true;
    }
}

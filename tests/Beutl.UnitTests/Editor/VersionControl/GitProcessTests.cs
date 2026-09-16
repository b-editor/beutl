using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public partial class GitProcessTests
{
    private const int ErrorNoProcess = 3;
    private readonly List<string> _temporaryDirectories = [];

    [SetUp]
    public void RequireUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("These tests start a Unix shell.");
        }
    }

    [TearDown]
    public void DeleteTemporaryDirectories()
    {
        foreach (string directory in _temporaryDirectories)
        {
            Directory.Delete(directory, recursive: true);
        }

        _temporaryDirectories.Clear();
    }

    [Test]
    public void Start_requires_every_standard_stream_to_be_redirected()
    {
        Assert.Throws<ArgumentException>(() => GitProcess.Start(new ProcessStartInfo("/bin/sh")));
    }

    [Test]
    public async Task Command_receives_its_arguments_environment_input_and_working_directory()
    {
        string directory = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory, "marker"), "in the working directory");
        ProcessStartInfo startInfo = CreateShell(
            "printf '%s|%s|%s|' \"$1\" \"$BEUTL_TEST_VALUE\" \"${BEUTL_TEST_REMOVED-unset}\"; "
            + "cat marker; printf '|'; cat; printf 'diagnostic' >&2; exit 7",
            directory,
            "first argument");
        startInfo.Environment["BEUTL_TEST_VALUE"] = "héllo wörld";
        startInfo.Environment["BEUTL_TEST_REMOVED"] = "present";
        startInfo.Environment.Remove("BEUTL_TEST_REMOVED");

        using GitProcess process = GitProcess.Start(startInfo);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync("入力");
        process.StandardInput.Close();
        await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(
                stdout.Result,
                Is.EqualTo("first argument|héllo wörld|unset|in the working directory|入力"));
            Assert.That(stderr.Result, Is.EqualTo("diagnostic"));
            Assert.That(process.TryGetExitCode(out int exitCode), Is.True);
            Assert.That(exitCode, Is.EqualTo(7));
            Assert.That(process.OwnsDescendants, Is.True);
        });
    }

    [Test]
    public async Task Command_is_found_on_the_path()
    {
        var startInfo = new ProcessStartInfo("sh", ["-c", "printf found"])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using GitProcess process = GitProcess.Start(startInfo);

        Assert.That(await DrainAsync(process), Is.EqualTo("found"));
    }

    [Test]
    public void Missing_executable_or_working_directory_fails_to_start()
    {
        string missing = Path.Combine(CreateTemporaryDirectory(), "missing");
        ProcessStartInfo missingExecutable = CreateShell("exit 0");
        missingExecutable.FileName = missing;

        Assert.Multiple(() =>
        {
            Assert.That(
                Assert.Throws<Win32Exception>(() => GitProcess.Start(missingExecutable))!.Message,
                Does.Contain(missing));
            Assert.That(
                Assert.Throws<Win32Exception>(() => GitProcess.Start(CreateShell("exit 0", missing)))!.Message,
                Does.Contain(missing));
        });
    }

    [Test]
    public async Task Command_ended_by_a_signal_reports_the_shell_exit_code()
    {
        using GitProcess process = GitProcess.Start(CreateShell("kill -KILL $$"));

        await DrainAsync(process);

        Assert.That(process.TryGetExitCode(out int exitCode), Is.True);
        Assert.That(exitCode, Is.EqualTo(128 + 9));
    }

    [Test]
    public async Task Kill_reaches_a_member_whose_parent_has_exited()
    {
        string pidPath = Path.Combine(CreateTemporaryDirectory(), "member.pid");
        ProcessStartInfo startInfo = CreateShell(
            "(sh -c 'printf \"%s\" \"$$\" > \"$1\"; exec sleep 30' sh \"$1\" < /dev/null > /dev/null 2>&1 &)",
            argument: pidPath);
        using GitProcess process = GitProcess.Start(startInfo);
        await DrainAsync(process);
        int member = await WaitForRecordedProcessIdAsync(pidPath);
        Assert.That(IsSignalable(member), Is.True);

        process.Kill();
        await process.WaitForGroupExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(await WaitUntilGoneAsync(member), Is.True);
    }

    // A runtime that started with SIGCHLD ignored reaps every child, taking the status with it.
    [Test]
    public async Task Status_reaped_elsewhere_is_never_reported_as_collected()
    {
        using GitProcess process = GitProcess.Start(CreateShell("exit 0"));
        process.StandardInput.Close();

        Assert.That(waitpid(process.Id, out _, 0), Is.EqualTo(process.Id));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(process.TryGetExitCode(out _), Is.False);
        // The id may already belong to another process, so this must not signal it.
        Assert.DoesNotThrow(process.Kill);
        Assert.DoesNotThrowAsync(async () =>
            await process.WaitForGroupExitAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task Command_disposed_before_it_exits_is_reaped_when_it_does()
    {
        int pid;
        using (GitProcess process = GitProcess.Start(CreateShell("read line; exit 0")))
        {
            pid = process.Id;
            Assert.That(IsSignalable(pid), Is.True);
        }

        // Disposal closed the command's input, so it exits and must not stay a zombie.
        Assert.That(await WaitUntilGoneAsync(pid), Is.True);
    }

    private ProcessStartInfo CreateShell(
        string script,
        string? workingDirectory = null,
        string? argument = null)
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

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        return startInfo;
    }

    private string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-git-process-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _temporaryDirectories.Add(directory);
        return directory;
    }

    private static async Task<string> DrainAsync(GitProcess process)
    {
        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));
        return await stdout;
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

        Assert.Fail("The member did not record its process id.");
        return 0;
    }

    // A zombie is still signalable, so this also waits for the process to be reaped.
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

    private static bool IsSignalable(int pid)
        => kill(pid, 0) == 0 || Marshal.GetLastPInvokeError() != ErrorNoProcess;

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int signal);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int waitpid(int pid, out int status, int options);
}

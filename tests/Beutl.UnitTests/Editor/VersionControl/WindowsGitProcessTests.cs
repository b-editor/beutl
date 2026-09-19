using System.ComponentModel;
using System.Diagnostics;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

// The suspended start and its job object, on Windows. Elsewhere these are skipped; the parts that run on
// every platform are covered by WindowsProcessArgumentsTests and ProcessTreeWalkTests.
[TestFixture]
public class WindowsGitProcessTests
{
    private const string StartDetachedPing =
        "$child = Start-Process -FilePath ping.exe -ArgumentList '-n','60','127.0.0.1' -NoNewWindow -PassThru; "
        + "Set-Content -LiteralPath $env:BEUTL_TEST_PROCESS_PID -Value $child.Id";

    private readonly List<string> _temporaryDirectories = [];

    // A runtime that does not expose the lock Process.Start takes keeps Process, which owns no job.
    [SetUp]
    public void RequireJobObjects()
    {
        if (!OperatingSystem.IsWindows() || !WindowsGitProcess.IsSupported)
        {
            Assert.Ignore("These tests need a suspended Windows start with a job object of its own.");
        }
    }

    [TearDown]
    public void DeleteTemporaryDirectories()
    {
        foreach (string directory in _temporaryDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        _temporaryDirectories.Clear();
    }

    [Test]
    public async Task Command_runs_in_a_job_with_its_arguments_environment_input_and_exit_code()
    {
        string directory = CreateTemporaryDirectory();
        ProcessStartInfo startInfo = CreateStartInfo(
            "cmd.exe",
            "/d",
            "/c",
            "cd & echo %BEUTL_TEST_VALUE% & sort & exit /b 7");
        startInfo.WorkingDirectory = directory;
        startInfo.Environment["BEUTL_TEST_VALUE"] = "environment value";

        using GitProcess process = GitProcess.Start(startInfo);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync("standard input");
        process.StandardInput.Close();
        await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(process.OwnsDescendants, Is.True);
            Assert.That(stdout.Result, Does.Contain(Path.GetFileName(directory)));
            Assert.That(stdout.Result, Does.Contain("environment value"));
            Assert.That(stdout.Result, Does.Contain("standard input"));
            Assert.That(process.TryGetExitCode(out int exitCode), Is.True);
            Assert.That(exitCode, Is.EqualTo(7));
        });
    }

    [Test]
    public void Missing_executable_fails_to_start()
    {
        string missing = Path.Combine(CreateTemporaryDirectory(), "missing.exe");

        Win32Exception? exception = Assert.Throws<Win32Exception>(
            () => GitProcess.Start(CreateStartInfo(missing)));

        Assert.That(exception!.Message, Does.Contain(missing));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Pending_pipe_io_can_be_interrupted_without_ending_the_process(bool useArrayOverloads, bool cancelOperation)
    {
        string pidPath = Path.Combine(CreateTemporaryDirectory(), "ready.pid");
        ProcessStartInfo startInfo = CreateStartInfo(
            "powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
            "Set-Content -LiteralPath $env:BEUTL_TEST_PROCESS_PID -Value $PID; Start-Sleep -Seconds 60");
        startInfo.Environment["BEUTL_TEST_PROCESS_PID"] = pidPath;
        using GitProcess process = GitProcess.Start(startInfo);
        Task exit = process.WaitForExitAsync();
        using var cancellation = new CancellationTokenSource();
        CancellationToken token = cancelOperation ? cancellation.Token : CancellationToken.None;
        // The child neither reads stdin nor writes stdout/stderr, so all three operations block.
        byte[] input = new byte[1024 * 1024];
        byte[] output = new byte[1];
        byte[] error = new byte[1];
        Task stdin = useArrayOverloads
            ? process.StandardInput.BaseStream.WriteAsync(input, 0, input.Length, token)
            : process.StandardInput.BaseStream.WriteAsync(input.AsMemory(), token).AsTask();
        Task stdout = useArrayOverloads
            ? process.StandardOutput.BaseStream.ReadAsync(output, 0, output.Length, token)
            : process.StandardOutput.ReadToEndAsync(token);
        Task stderr = useArrayOverloads
            ? process.StandardError.BaseStream.ReadAsync(error, 0, error.Length, token)
            : process.StandardError.ReadToEndAsync(token);
        Task io = Task.WhenAll(stdin, stdout, stderr);
        try
        {
            await ReadProcessIdAsync(pidPath);
            await Task.Delay(100);
            Assert.That(new[] { stdin, stdout, stderr }.All(task => !task.IsCompleted), Is.True);

            if (cancelOperation)
            {
                cancellation.Cancel();
            }
            else
            {
                process.CloseStandardStreams();
            }
            await ObserveClosedStreamsAsync(io);
            Assert.That(exit.IsCompleted, Is.False, "Closing the pipes must not terminate their other owner.");
            // Closing again, including during Dispose, must be harmless.
            process.CloseStandardStreams();
        }
        finally
        {
            process.Kill();
            await process.WaitForGroupExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await ObserveClosedStreamsAsync(io);
        }
    }

    private static async Task ObserveClosedStreamsAsync(Task io)
    {
        try
        {
            await io.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Closing a pipe can finish its pending I/O with any of these errors. A timeout is a failure.
        }
    }

    [Test]
    public async Task Closing_pipes_does_not_release_quarantine_while_a_job_member_is_alive()
    {
        string directory = CreateTemporaryDirectory();
        string pidPath = Path.Combine(directory, "ready.pid");
        GitProcess? retainedProcess = null;
        var runner = new GitCliRunner(
            "powershell.exe", TimeSpan.FromSeconds(60),
            new Dictionary<string, string?> { ["BEUTL_TEST_PROCESS_PID"] = pidPath },
            killProcessGroup: process => retainedProcess = process);
        var repository = new RepositoryInfo(directory, directory);
        using var cancellation = new CancellationTokenSource();
        Task<GitCommandResult> run = runner.RunAsync(
            repository,
            ["-NoProfile", "-NonInteractive", "-Command",
                "Set-Content -LiteralPath $env:BEUTL_TEST_PROCESS_PID -Value $PID; Start-Sleep -Seconds 60"],
            GitCommandOptions.Local, cancellation.Token);
        using var followUpCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<GitCommandResult>? followUp = null;
        try
        {
            await ReadProcessIdAsync(pidPath);
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await run.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.That(runner.HasActiveProcess, Is.True);
            followUp = runner.RunAsync(
                repository, ["-NoProfile", "-NonInteractive", "-Command", "exit 0"],
                GitCommandOptions.Local, followUpCancellation.Token);
            await Task.Delay(100);
            Assert.That(followUp.IsCompleted, Is.False);

            retainedProcess!.Kill();
            retainedProcess = null;
            GitCommandResult result = await followUp.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(runner.HasActiveProcess, Is.False);
            });
        }
        finally
        {
            cancellation.Cancel();
            followUpCancellation.Cancel();
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                retainedProcess?.Kill();
            }
            if (followUp is not null)
            {
                try
                {
                    await followUp.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    // PowerShell starts ping with its own standard handles and exits, so ping outlives its parent and
    // holds the command's pipes. The job still owns it.
    [Test]
    public async Task Job_owns_a_descendant_whose_parent_has_exited_until_it_is_killed()
    {
        string pidPath = Path.Combine(CreateTemporaryDirectory(), "ping.pid");
        ProcessStartInfo startInfo = CreateStartInfo(
            "powershell.exe",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            StartDetachedPing);
        startInfo.Environment["BEUTL_TEST_PROCESS_PID"] = pidPath;
        using GitProcess process = GitProcess.Start(startInfo);
        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Process? descendant = null;

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            descendant = Process.GetProcessById(await ReadProcessIdAsync(pidPath));
            Task groupExit = process.WaitForGroupExitAsync();
            await Task.Delay(500);
            Assert.That(groupExit.IsCompleted, Is.False);

            process.Kill();

            await groupExit.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(descendant.WaitForExit(TimeSpan.FromSeconds(10)), Is.True);
        }
        finally
        {
            KillQuietly(descendant);
            descendant?.Dispose();
        }
    }

    [Test]
    public async Task Runner_timeout_ends_a_descendant_that_holds_its_pipes()
    {
        string directory = CreateTemporaryDirectory();
        string pidPath = Path.Combine(directory, "ping.pid");
        var runner = new GitCliRunner(
            "powershell.exe",
            TimeSpan.FromSeconds(15),
            new Dictionary<string, string?> { ["BEUTL_TEST_PROCESS_PID"] = pidPath });
        Task<GitCommandResult> runTask = runner.RunAsync(
            new RepositoryInfo(directory, directory),
            ["-NoProfile", "-NonInteractive", "-Command", StartDetachedPing],
            GitCommandOptions.Local,
            CancellationToken.None);
        Process? descendant = null;

        try
        {
            descendant = Process.GetProcessById(await ReadProcessIdAsync(pidPath));
            Assert.ThrowsAsync<TimeoutException>(async () => await runTask.WaitAsync(TimeSpan.FromSeconds(60)));

            Assert.Multiple(() =>
            {
                Assert.That(descendant.WaitForExit(TimeSpan.FromSeconds(10)), Is.True);
                Assert.That(runner.HasActiveProcess, Is.False);
            });
        }
        finally
        {
            KillQuietly(descendant);
            descendant?.Dispose();
            try
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception)
            {
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-windows-process-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _temporaryDirectories.Add(directory);
        return directory;
    }

    private static async Task<int> ReadProcessIdAsync(string pidPath)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(60))
        {
            try
            {
                if (File.Exists(pidPath)
                    && int.TryParse((await File.ReadAllTextAsync(pidPath)).Trim(), out int pid))
                {
                    return pid;
                }
            }
            catch (IOException)
            {
                // PowerShell may still be writing the file.
            }

            await Task.Delay(50);
        }

        Assert.Fail("The descendant did not record its process id.");
        return 0;
    }

    private static void KillQuietly(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }
}

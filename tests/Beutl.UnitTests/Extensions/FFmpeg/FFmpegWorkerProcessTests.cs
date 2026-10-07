using System.IO;
using System.Threading;
using Beutl.Extensions.FFmpeg;
using Beutl.FFmpegIpc;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture, NonParallelizable]
public class FFmpegWorkerProcessTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedStartup_ReleasesProcessAndLogPumpWithoutApplyingGenericCooldown(bool canceled)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Uses a POSIX worker fixture.");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var cooldown = typeof(FFmpegLibraryState).GetField("s_missingSinceTicks", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        object? previousCooldown = cooldown.GetValue(null);
        using var cancellation = new CancellationTokenSource();
        using var worker = new FFmpegWorkerProcess(false, start =>
        {
            start.FileName = "/bin/sh";
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(canceled ? "exec sleep 30" : "exit 2");
            if (canceled) cancellation.Cancel();
        });
        try
        {
            var method = typeof(FFmpegWorkerProcess).GetMethod("StartWorkerWithCooldownAsync", flags)!;
            Task start = (Task)method.Invoke(worker, new object[] { cancellation.Token })!;
            if (canceled) await Assert.CatchAsync<OperationCanceledException>(async () => await start);
            else await Assert.ThrowsAsync<FFmpegLibrariesNotFoundException>(async () => await start);
            Assert.That(worker.WorkerPid, Is.Zero);
            Assert.That(typeof(FFmpegWorkerProcess).GetField("_logPump", flags)!.GetValue(worker), Is.Null);
            Assert.That(typeof(FFmpegWorkerProcess).GetField("_lastStartupFailure", flags)!.GetValue(worker), Is.Null);
            await Task.CompletedTask;
        }
        finally
        {
            cooldown.SetValue(null, previousCooldown);
        }
    }

    private const string DotnetHost = "/usr/bin/dotnet";

    private static string SubDirApphost(string baseDir, bool isWindows) =>
        Path.Combine(baseDir, "FFmpegWorker", "Beutl.FFmpegWorker") + (isWindows ? ".exe" : "");

    private static string SubDirDll(string baseDir) =>
        Path.Combine(baseDir, "FFmpegWorker", "Beutl.FFmpegWorker.dll");

    private static string FlatApphost(string baseDir, bool isWindows) =>
        Path.Combine(baseDir, "Beutl.FFmpegWorker") + (isWindows ? ".exe" : "");

    private static string FlatDll(string baseDir) =>
        Path.Combine(baseDir, "Beutl.FFmpegWorker.dll");

    private static bool IsDeploymentFile(string path, string stem) =>
        path == stem + ".dll" || path == stem + ".runtimeconfig.json" || path == stem + ".deps.json";

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveWorkerCommand_PrefersSubdirApphost_OverFlatLayout(bool isWindows)
    {
        const string baseDir = "/app";
        string subApphost = SubDirApphost(baseDir, isWindows);
        // Both layouts present; the subdir must win so the worker loads its own shared assemblies.
        bool FileExists(string p) => p == subApphost || p == FlatApphost(baseDir, isWindows)
            || IsDeploymentFile(p, Path.Combine(baseDir, "FFmpegWorker", "Beutl.FFmpegWorker"))
            || IsDeploymentFile(p, Path.Combine(baseDir, "Beutl.FFmpegWorker"));

        var command = FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists);

        Assert.That(command.FileName, Is.EqualTo(subApphost));
        Assert.That(command.DllArgument, Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveWorkerCommand_FlatApphost_WhenNoSubdir(bool isWindows)
    {
        const string baseDir = "/app";
        string flatApphost = FlatApphost(baseDir, isWindows);
        bool FileExists(string p) => p == flatApphost
            || IsDeploymentFile(p, Path.Combine(baseDir, "Beutl.FFmpegWorker"));

        var command = FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists);

        Assert.That(command.FileName, Is.EqualTo(flatApphost));
        Assert.That(command.DllArgument, Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveWorkerCommand_SubdirDllMode_WhenNoApphost(bool isWindows)
    {
        const string baseDir = "/app";
        // UseAppHost=false dev build: the DLL and manifests exist, launched via the dotnet host.
        bool FileExists(string p) => IsDeploymentFile(p, Path.Combine(baseDir, "FFmpegWorker", "Beutl.FFmpegWorker"));

        var command = FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists);

        Assert.That(command.FileName, Is.EqualTo(DotnetHost));
        Assert.That(command.DllArgument, Is.EqualTo(SubDirDll(baseDir)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveWorkerCommand_FlatDllMode_WhenNoApphostExists(bool isWindows)
    {
        const string baseDir = "/app";
        bool FileExists(string p) => IsDeploymentFile(p, Path.Combine(baseDir, "Beutl.FFmpegWorker"));

        var command = FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists);

        Assert.That(command.FileName, Is.EqualTo(DotnetHost));
        Assert.That(command.DllArgument, Is.EqualTo(FlatDll(baseDir)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveWorkerCommand_SubdirDll_StillPreferredOverFlatApphost(bool isWindows)
    {
        const string baseDir = "/app";
        // Subdir .dll vs flat apphost: the subdir still wins (isolation first), launched via dotnet host.
        bool FileExists(string p) => p == FlatApphost(baseDir, isWindows)
            || IsDeploymentFile(p, Path.Combine(baseDir, "FFmpegWorker", "Beutl.FFmpegWorker"))
            || IsDeploymentFile(p, Path.Combine(baseDir, "Beutl.FFmpegWorker"));

        var command = FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists);

        Assert.That(command.FileName, Is.EqualTo(DotnetHost));
        Assert.That(command.DllArgument, Is.EqualTo(SubDirDll(baseDir)));
    }

    [TestCase(true, ".dll")]
    [TestCase(false, ".dll")]
    [TestCase(true, ".runtimeconfig.json")]
    [TestCase(false, ".runtimeconfig.json")]
    [TestCase(true, ".deps.json")]
    [TestCase(false, ".deps.json")]
    public void ResolveWorkerCommand_IncompleteSubdir_DoesNotHideCompleteFlatLayout(bool isWindows, string missingExtension)
    {
        const string baseDir = "/app";
        string subStem = Path.Combine(baseDir, "FFmpegWorker", "Beutl.FFmpegWorker");
        string flatStem = Path.Combine(baseDir, "Beutl.FFmpegWorker");
        bool FileExists(string p) => p != subStem + missingExtension
            && (p == SubDirApphost(baseDir, isWindows) || p == FlatApphost(baseDir, isWindows)
                || IsDeploymentFile(p, subStem) || IsDeploymentFile(p, flatStem));

        var command = FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists);

        Assert.That(command.FileName, Is.EqualTo(FlatApphost(baseDir, isWindows)));
        Assert.That(command.DllArgument, Is.Null);
    }

    [TestCase(true, ".dll")]
    [TestCase(false, ".dll")]
    [TestCase(true, ".runtimeconfig.json")]
    [TestCase(false, ".runtimeconfig.json")]
    [TestCase(true, ".deps.json")]
    [TestCase(false, ".deps.json")]
    public void ResolveWorkerCommand_RejectsIncompleteDeployment(bool isWindows, string missingExtension)
    {
        const string baseDir = "/app";
        string flatStem = Path.Combine(baseDir, "Beutl.FFmpegWorker");
        bool FileExists(string p) => p != flatStem + missingExtension
            && (p == FlatApphost(baseDir, isWindows) || IsDeploymentFile(p, flatStem));

        FileNotFoundException? error = Assert.Throws<FileNotFoundException>(() =>
            FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists));

        Assert.That(error!.Message, Does.Contain("No complete FFmpeg worker deployment"));
        Assert.That(error.Message, Does.Contain("Beutl.FFmpegWorker" + missingExtension));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveWorkerCommand_RejectsAbsentWorker(bool isWindows)
    {
        Assert.Throws<FileNotFoundException>(() =>
            FFmpegWorkerProcess.ResolveWorkerCommand("/app", isWindows, DotnetHost, _ => false));
    }

    [Test]
    public async Task EarlyExit_IncludesStandardErrorInStartupException()
    {
        var error = await GetStartupFailureAsync("printf 'The application to execute does not exist: Beutl.FFmpegWorker.dll\\n' >&2; exit 154");

        Assert.That(error.Message, Does.Contain("code 154 before establishing connection."));
        Assert.That(error.Message, Does.Contain("Worker stderr: The application to execute does not exist: Beutl.FFmpegWorker.dll"));
    }

    [Test]
    public async Task EarlyExit_StandardErrorTailIsBoundedAndKeepsLastDiagnostic()
    {
        var error = await GetStartupFailureAsync(
            "printf 'discarded-first-line\\n' >&2; printf '%8192s' '' | tr ' ' x >&2; printf '\\nlast diagnostic\\n' >&2; exit 154");

        Assert.That(error.Message, Does.Not.Contain("discarded-first-line"));
        Assert.That(error.Message, Does.EndWith("last diagnostic"));
        Assert.That(error.Message.Length, Is.LessThan(4300));
    }

    [Test]
    public async Task EarlyExit_WithoutStandardError_PreservesExitCodeMessage()
    {
        var error = await GetStartupFailureAsync("exit 154");

        Assert.That(error.Message, Is.EqualTo("FFmpeg worker exited unexpectedly with code 154 before establishing connection."));
    }

    private static async Task<InvalidOperationException> GetStartupFailureAsync(string script)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Uses a POSIX worker fixture.");
        using var worker = new FFmpegWorkerProcess(false, start =>
        {
            start.FileName = "/bin/sh";
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(script);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var method = typeof(FFmpegWorkerProcess).GetMethod("StartWorkerWithCooldownAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var startup = (Task)method.Invoke(worker, new object[] { timeout.Token })!;
        return (await Assert.ThrowsAsync<InvalidOperationException>(async () => await startup))!;
    }

    [Test]
    public void CreateWorkerStartCanceledException_UserCancellation_RemainsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Exception ex = FFmpegWorkerProcess.CreateWorkerStartCanceledException(cts.Token);

        Assert.That(ex, Is.TypeOf<OperationCanceledException>());
    }

    [Test]
    public void CreateWorkerStartCanceledException_InternalTimeout_RemainsTimeout()
    {
        Exception ex = FFmpegWorkerProcess.CreateWorkerStartCanceledException(CancellationToken.None);

        Assert.That(ex, Is.TypeOf<TimeoutException>());
    }
}

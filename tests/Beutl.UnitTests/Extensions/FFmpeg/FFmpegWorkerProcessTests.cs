using System.IO;
using System.Threading;
using Beutl.Extensions.FFmpeg;
using Beutl.FFmpegIpc;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
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
            if (canceled) Assert.CatchAsync<OperationCanceledException>(async () => await start);
            else Assert.ThrowsAsync<FFmpegLibrariesNotFoundException>(async () => await start);
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

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveWorkerCommand_PrefersSubdirApphost_OverFlatLayout(bool isWindows)
    {
        const string baseDir = "/app";
        string subApphost = SubDirApphost(baseDir, isWindows);
        // Both layouts present; the subdir must win so the worker loads its own shared assemblies.
        bool FileExists(string p) => p == subApphost || p == FlatApphost(baseDir, isWindows);

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
        bool FileExists(string p) => p == flatApphost;

        var command = FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists);

        Assert.That(command.FileName, Is.EqualTo(flatApphost));
        Assert.That(command.DllArgument, Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveWorkerCommand_SubdirDllMode_WhenNoApphost(bool isWindows)
    {
        const string baseDir = "/app";
        // UseAppHost=false dev build: only the subdir .dll exists, launched via the dotnet host.
        bool FileExists(string p) => p == SubDirDll(baseDir);

        var command = FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists);

        Assert.That(command.FileName, Is.EqualTo(DotnetHost));
        Assert.That(command.DllArgument, Is.EqualTo(SubDirDll(baseDir)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveWorkerCommand_FlatDllMode_WhenNothingElseExists(bool isWindows)
    {
        const string baseDir = "/app";
        // No file probe succeeds: fall back to the flat layout in DLL mode.
        bool FileExists(string p) => false;

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
        bool FileExists(string p) => p == SubDirDll(baseDir) || p == FlatApphost(baseDir, isWindows);

        var command = FFmpegWorkerProcess.ResolveWorkerCommand(baseDir, isWindows, DotnetHost, FileExists);

        Assert.That(command.FileName, Is.EqualTo(DotnetHost));
        Assert.That(command.DllArgument, Is.EqualTo(SubDirDll(baseDir)));
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

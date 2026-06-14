using System.IO;
using Beutl.Extensions.FFmpeg;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegWorkerProcessTests
{
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
}

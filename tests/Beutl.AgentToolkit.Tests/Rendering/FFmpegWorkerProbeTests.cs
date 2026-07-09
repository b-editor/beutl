using Beutl.Extensions.FFmpeg;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class FFmpegWorkerProbeTests
{
    [Test]
    public void IsWorkerAvailable_returns_false_for_empty_directory()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Assert.That(FFmpegWorkerProcess.IsWorkerAvailable(dir), Is.False);
    }

    [Test]
    public void IsWorkerAvailable_returns_true_when_worker_in_subdir()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        string workerDir = Path.Combine(dir, "FFmpegWorker");
        Directory.CreateDirectory(workerDir);
        string fileName = OperatingSystem.IsWindows() ? "Beutl.FFmpegWorker.exe" : "Beutl.FFmpegWorker.dll";
        File.WriteAllText(Path.Combine(workerDir, fileName), "stub");
        Assert.That(FFmpegWorkerProcess.IsWorkerAvailable(dir), Is.True);
    }

    [Test]
    public void IsWorkerAvailable_returns_true_when_worker_flat()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string fileName = OperatingSystem.IsWindows() ? "Beutl.FFmpegWorker.exe" : "Beutl.FFmpegWorker.dll";
        File.WriteAllText(Path.Combine(dir, fileName), "stub");
        Assert.That(FFmpegWorkerProcess.IsWorkerAvailable(dir), Is.True);
    }
}

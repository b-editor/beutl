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
        WriteCompleteDeployment(workerDir);
        Assert.That(FFmpegWorkerProcess.IsWorkerAvailable(dir), Is.True);
    }

    [Test]
    public void IsWorkerAvailable_returns_true_when_worker_flat()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        WriteCompleteDeployment(dir);
        Assert.That(FFmpegWorkerProcess.IsWorkerAvailable(dir), Is.True);
    }

    [TestCase(".dll")]
    [TestCase(".runtimeconfig.json")]
    [TestCase(".deps.json")]
    public void IsWorkerAvailable_returns_false_for_incomplete_deployment(string missingExtension)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WriteCompleteDeployment(dir);
            File.WriteAllText(Path.Combine(dir, "Beutl.FFmpegWorker" + (OperatingSystem.IsWindows() ? ".exe" : "")), "stub");
            File.Delete(Path.Combine(dir, "Beutl.FFmpegWorker" + missingExtension));

            Assert.That(FFmpegWorkerProcess.IsWorkerAvailable(dir), Is.False);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void WriteCompleteDeployment(string directory)
    {
        foreach (string extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
            File.WriteAllText(Path.Combine(directory, "Beutl.FFmpegWorker" + extension), "stub");
    }
}

using Beutl.AgentToolkit.Rendering;
using Beutl.Extensibility;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

public class VideoExporterFileSafetyTests
{
    private string _directory = null!;
    private string _destination = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("beutl-agent-export-").FullName;
        _destination = Path.Combine(_directory, "existing.mp4");
        File.WriteAllText(_destination, "previous complete output");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task Cancellation_during_encoding_preserves_the_previous_output()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exporter = CreateExporter(async (controller, token) =>
        {
            File.WriteAllText(controller.OutputFile, "partial");
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        Task export = ExportAsync(exporter, cancellation.Token).AsTask();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(File.ReadAllText(_destination), Is.EqualTo("previous complete output"));
        }
        finally
        {
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await export);
        }
        AssertPreviousOutputAndNoStaging();
    }

    [Test]
    public async Task Encoder_failure_preserves_the_previous_output()
    {
        var exporter = CreateExporter((controller, _) =>
        {
            File.WriteAllText(controller.OutputFile, "partial");
            throw new IOException("encode failed");
        });

        await Assert.ThrowsAsync<IOException>(async () => await ExportAsync(exporter));
        AssertPreviousOutputAndNoStaging();
    }

    [Test]
    public async Task Cancellation_before_publication_preserves_the_previous_output()
    {
        using var cancellation = new CancellationTokenSource();
        var exporter = CreateExporter((controller, _) =>
        {
            File.WriteAllText(controller.OutputFile, "complete but cancelled");
            cancellation.Cancel();
            return Task.CompletedTask;
        });

        await Assert.CatchAsync<OperationCanceledException>(async () => await ExportAsync(exporter, cancellation.Token));
        AssertPreviousOutputAndNoStaging();
    }

    [Test]
    public async Task Successful_export_publishes_before_reporting_completion()
    {
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_destination, mode);
        var exporter = CreateExporter((controller, _) =>
        {
            Assert.That(File.ReadAllText(_destination), Is.EqualTo("previous complete output"));
            Assert.That(controller.OutputFile, Is.Not.EqualTo(_destination));
            Assert.That(Path.GetFileName(controller.OutputFile), Is.EqualTo(Path.GetFileName(_destination)));
            File.WriteAllText(controller.OutputFile, "complete");
            return Task.CompletedTask;
        });
        bool completed = false;

        ExportVideoResponse response = await exporter.ExportAsync(
            CreateScene(), _destination, new Rational(30, 1), 44100, 1, CancellationToken.None,
            onFrameProgress: (done, total) =>
            {
                if (done != total) return;
                Assert.That(File.ReadAllText(_destination), Is.EqualTo("complete"));
                completed = true;
            });

        Assert.That(completed, Is.True);
        if (!OperatingSystem.IsWindows())
            Assert.That(File.GetUnixFileMode(_destination), Is.EqualTo(mode));
        Assert.That(response.OutputPath, Is.EqualTo(_destination));
        Assert.That(File.ReadAllText(_destination), Is.EqualTo("complete"));
        Assert.That(Directory.GetDirectories(_directory), Is.Empty);
    }

    [Test]
    public async Task Fallback_encoder_uses_fresh_staging_and_preserves_the_destination_until_success()
    {
        string? failedOutput = null;
        var first = new ProbeEncoder((controller, _) =>
        {
            failedOutput = controller.OutputFile;
            File.WriteAllText(controller.OutputFile, "partial first attempt");
            throw new FFmpegWorkerException("worker failed");
        });
        var second = new ProbeEncoder((controller, _) =>
        {
            Assert.That(File.ReadAllText(_destination), Is.EqualTo("previous complete output"));
            Assert.That(File.Exists(failedOutput), Is.False);
            Assert.That(controller.OutputFile, Is.Not.EqualTo(failedOutput));
            File.WriteAllText(controller.OutputFile, "fallback complete");
            return Task.CompletedTask;
        });

        await ExportAsync(new VideoExporter(new EncoderRegistration(first, second)));

        Assert.That(File.ReadAllText(_destination), Is.EqualTo("fallback complete"));
        Assert.That(Directory.GetDirectories(_directory), Is.Empty);
    }

    [Test]
    public async Task Publication_failure_does_not_report_completion_or_delete_existing_data()
    {
        File.Delete(_destination);
        Directory.CreateDirectory(_destination);
        File.WriteAllText(Path.Combine(_destination, "keep.txt"), "existing data");
        bool completed = false;
        var exporter = CreateExporter((controller, _) =>
        {
            File.WriteAllText(controller.OutputFile, "complete");
            return Task.CompletedTask;
        });

        Exception? failure = await Assert.CatchAsync<Exception>(async () => await exporter.ExportAsync(
            CreateScene(), _destination, new Rational(30, 1), 44100, 1, CancellationToken.None,
            onFrameProgress: (done, total) => completed |= done == total));

        Assert.That(failure, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        Assert.That(completed, Is.False);
        Assert.That(File.ReadAllText(Path.Combine(_destination, "keep.txt")), Is.EqualTo("existing data"));
        Assert.That(Directory.GetDirectories(_directory, ".beutl-output-*"), Is.Empty);
    }

    [Test]
    public async Task Native_avfoundation_cancellation_preserves_the_previous_output()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("AVFoundation requires macOS.");
            return;
        }
        AgentToolkitGpuTestEnvironment.EnsureAvailable();
        using var cancellation = new CancellationTokenSource();
        var exporter = new VideoExporter(new EncoderRegistration(new AVFEncodingExtension()));

        await Assert.CatchAsync<OperationCanceledException>(async () => await exporter.ExportAsync(
            CreateScene(), _destination, new Rational(30, 1), 44100, 1, cancellation.Token,
            onFrameProgress: (_, _) => cancellation.Cancel()));

        AssertPreviousOutputAndNoStaging();
    }

    private static Scene CreateScene() => new(64, 64, "export") { Duration = TimeSpan.FromSeconds(0.2) };

    private ValueTask<ExportVideoResponse> ExportAsync(VideoExporter exporter, CancellationToken token = default)
        => exporter.ExportAsync(CreateScene(), _destination, new Rational(30, 1), 44100, 1, token);

    private static VideoExporter CreateExporter(Func<EncodingController, CancellationToken, Task> encode)
        => new(new EncoderRegistration(new ProbeEncoder(encode)));

    private void AssertPreviousOutputAndNoStaging()
    {
        Assert.That(File.ReadAllText(_destination), Is.EqualTo("previous complete output"));
        Assert.That(Directory.GetDirectories(_directory), Is.Empty);
    }

    private sealed class ProbeEncoder(Func<EncodingController, CancellationToken, Task> encode) : ControllableEncodingExtension
    {
        public override IEnumerable<string> SupportExtensions() => [".mp4"];
        public override EncodingController CreateController(string file) => new ProbeController(file, encode);
    }

    private sealed class ProbeController(string file, Func<EncodingController, CancellationToken, Task> encode) : EncodingController(file)
    {
        public override VideoEncoderSettings VideoSettings { get; } = new();
        public override AudioEncoderSettings AudioSettings { get; } = new();
        public override ValueTask Encode(IFrameProvider frames, ISampleProvider samples, CancellationToken token) => new(encode(this, token));
    }
}

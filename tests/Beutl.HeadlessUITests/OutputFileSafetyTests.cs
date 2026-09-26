using Avalonia.Headless.NUnit;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.HeadlessUITests;

public class OutputFileSafetyTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Cancellation_preserves_existing_output_and_removes_only_staging(bool writePartialFile)
    {
        EditViewModel editor = await OpenEditorAsync();
        string destination = CreateExistingOutput(editor);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var encoder = new ProbeEncoder(async (controller, token) =>
        {
            if (writePartialFile) File.WriteAllText(controller.OutputFile, "partial");
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        using var output = CreateOutput(editor, destination, encoder);
        using var cancellation = new CancellationTokenSource();
        Task<Exception?> execution = CaptureFailure(output.RunAsync(cancellation.Token));
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(File.ReadAllText(destination), Is.EqualTo("previous complete output"));
            cancellation.Cancel();
            Assert.That(await execution, Is.InstanceOf<OperationCanceledException>());
            Assert.That(output.IsCompleted.Value, Is.False);
            Assert.That(File.ReadAllText(destination), Is.EqualTo("previous complete output"));
            AssertNoStaging(destination);
        }
        finally
        {
            cancellation.Cancel();
            await execution;
        }
    }

    [AvaloniaTest]
    public async Task Encoder_failure_preserves_the_existing_output()
    {
        EditViewModel editor = await OpenEditorAsync();
        string destination = CreateExistingOutput(editor);
        var encoder = new ProbeEncoder((controller, _) =>
        {
            File.WriteAllText(controller.OutputFile, "partial");
            throw new IOException("encoder failed after writing");
        });
        using var output = CreateOutput(editor, destination, encoder);

        Assert.That(await CaptureFailure(output.RunAsync(CancellationToken.None)), Is.TypeOf<IOException>());
        Assert.That(File.ReadAllText(destination), Is.EqualTo("previous complete output"));
        Assert.That(output.IsCompleted.Value, Is.False);
        AssertNoStaging(destination);
    }

    [AvaloniaTest]
    public async Task Success_publishes_the_complete_output_and_preserves_encoder_settings()
    {
        EditViewModel editor = await OpenEditorAsync();
        string destination = CreateExistingOutput(editor);
        ProbeController? executionController = null;
        var encoder = new ProbeEncoder((controller, _) =>
        {
            executionController = controller;
            Assert.That(File.ReadAllText(destination), Is.EqualTo("previous complete output"));
            File.WriteAllText(controller.OutputFile, "new");
            return Task.CompletedTask;
        });
        using var output = CreateOutput(editor, destination, encoder);
        EncodingController settings = output.Controller.Value!;
        settings.VideoSettings.FrameRate = new Rational(24, 1);
        settings.VideoSettings.DestinationSize = new PixelSize(128, 72);
        settings.AudioSettings.SampleRate = 48000;

        await output.RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(destination), Is.EqualTo("new"));
            Assert.That(output.IsCompleted.Value, Is.True);
            Assert.That(output.Controller.Value, Is.SameAs(settings));
            Assert.That(executionController!.OutputFile, Is.Not.EqualTo(destination));
            Assert.That(Path.GetFileName(executionController.OutputFile), Is.EqualTo(Path.GetFileName(destination)));
            Assert.That(executionController.VideoSettings.FrameRate, Is.EqualTo(new Rational(24, 1)));
            Assert.That(executionController.VideoSettings.DestinationSize, Is.EqualTo(new PixelSize(128, 72)));
            Assert.That(executionController.AudioSettings.SampleRate, Is.EqualTo(48000));
        });
        AssertNoStaging(destination);
    }

    [AvaloniaTest]
    public async Task Cancellation_after_encoding_but_before_publication_preserves_the_previous_output()
    {
        EditViewModel editor = await OpenEditorAsync();
        string destination = CreateExistingOutput(editor);
        using var cancellation = new CancellationTokenSource();
        var encoder = new ProbeEncoder((controller, _) =>
        {
            File.WriteAllText(controller.OutputFile, "completed but cancelled");
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        using var output = CreateOutput(editor, destination, encoder);

        Assert.That(await CaptureFailure(output.RunAsync(cancellation.Token)), Is.InstanceOf<OperationCanceledException>());
        Assert.That(File.ReadAllText(destination), Is.EqualTo("previous complete output"));
        AssertNoStaging(destination);
    }

    [AvaloniaTest]
    public async Task Publication_failure_is_not_reported_as_success_and_cleans_staging()
    {
        EditViewModel editor = await OpenEditorAsync();
        string destination = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "directory.mp4");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep");
        var encoder = new ProbeEncoder((controller, _) =>
        {
            File.WriteAllText(controller.OutputFile, "new");
            return Task.CompletedTask;
        });
        using var output = CreateOutput(editor, destination, encoder);

        Assert.That(await CaptureFailure(output.RunAsync(CancellationToken.None)), Is.Not.Null);
        Assert.That(output.IsCompleted.Value, Is.False);
        Assert.That(File.ReadAllText(Path.Combine(destination, "keep.txt")), Is.EqualTo("keep"));
        AssertNoStaging(destination);
    }

    private static async Task<EditViewModel> OpenEditorAsync()
    {
        await TestReset.ResetShellAsync();
        GpuTestGate.EnsureAvailable();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "output-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scene = new Scene
        {
            Uri = new Uri(Path.Combine(directory, "main.scene")),
            FrameSize = new PixelSize(64, 64),
            Duration = TimeSpan.FromMilliseconds(100),
        };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static string CreateExistingOutput(EditViewModel editor)
    {
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "existing movie.mp4");
        File.WriteAllText(path, "previous complete output");
        return path;
    }

    private static OutputViewModel CreateOutput(EditViewModel editor, string destination, ProbeEncoder encoder)
    {
        var output = new OutputViewModel(editor);
        output.DestinationFile.Value = destination;
        output.SelectedEncoder.Value = encoder;
        HeadlessTestHelpers.Settle();
        return output;
    }

    private static void AssertNoStaging(string destination)
        => Assert.That(Directory.GetDirectories(Path.GetDirectoryName(destination)!, ".beutl-output-*"), Is.Empty);

    private static async Task<Exception?> CaptureFailure(Task task)
    {
        try { await task; return null; }
        catch (Exception ex) { return ex; }
    }

    private sealed class ProbeEncoder(Func<ProbeController, CancellationToken, Task> encode) : ControllableEncodingExtension
    {
        public override IEnumerable<string> SupportExtensions() => [".mp4"];
        public override EncodingController CreateController(string file) => new ProbeController(file, encode);
    }

    private sealed class ProbeController(string file, Func<ProbeController, CancellationToken, Task> encode)
        : EncodingController(file)
    {
        public override VideoEncoderSettings VideoSettings { get; } = new();
        public override AudioEncoderSettings AudioSettings { get; } = new();
        public override ValueTask Encode(IFrameProvider frames, ISampleProvider samples, CancellationToken token)
            => new(encode(this, token));
    }
}

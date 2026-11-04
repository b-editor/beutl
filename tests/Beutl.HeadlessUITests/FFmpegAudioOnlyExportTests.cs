using Avalonia.Headless.NUnit;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class FFmpegAudioOnlyExportTests
{
    [AvaloniaTest]
    [TestCase(".wav")]
    [TestCase(".mp3")]
    public async Task Export_skips_the_video_pass(string extension)
    {
        await TestReset.ResetShellAsync();
        var editor = await TestShell.OpenNewSceneEditorAsync("ffmpeg-audio-only", 64, 48);
        editor.Scene.Duration.Value = MediaTime.FromSeconds(0.2);
        // The preflight rejects a missing source when the export uses it.
        string missingPath = Path.Combine(TestShell.ProjectRoot(editor), "missing.mov");
        await TestShell.AddElementAsync(editor, () =>
        {
            var source = new VideoSource();
            source.ReadFrom(new Uri(missingPath));
            return new SourceVideo { Source = { Value = source } };
        }, MediaTime.Zero, editor.Scene.Duration.Value);

        var encoder = new RecordingEncoder();
        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(TestShell.ProjectRoot(editor), Guid.NewGuid() + extension);
        output.SelectedEncoder.Value = encoder;
        HeadlessTestHelpers.Settle();

        await output.RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(encoder.FrameCount, Is.Zero);
            Assert.That(output.ProgressMax.Value, Is.EqualTo(editor.Scene.Duration.Value.ToSeconds()));
        });
    }

    private sealed class RecordingEncoder : ControllableEncodingExtension
    {
        public long? FrameCount { get; set; }

        public override IEnumerable<string> SupportExtensions() => FFmpegHeadlessEncodingExtension.SupportedExtensions;

        public override EncodingController CreateController(string file) => new RecordingController(file, this);
    }

    // The real controller, up to the worker process that this test project does not deploy.
    private sealed class RecordingController(string file, RecordingEncoder owner)
        : FFmpegEncodingControllerProxy(file, new FFmpegEncodingSettings())
    {
        public override async ValueTask Encode(
            IFrameProvider frameProvider, ISampleProvider sampleProvider, CancellationToken cancellationToken)
        {
            owner.FrameCount = frameProvider.FrameCount;
            await File.WriteAllTextAsync(OutputFile, "test output", cancellationToken);
        }
    }
}

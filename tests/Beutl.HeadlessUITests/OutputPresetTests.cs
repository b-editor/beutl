using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.HeadlessUITests;

// A platform preset is named after a frame size and rates ("YouTube 4K 60fps"), so applying one sets them. Every other
// preset, the quality ones and those saved from an output, leaves them to the scene and the project.
[TestFixture]
public sealed class OutputPresetTests
{
    private static readonly PixelSize s_sceneSize = new(1920, 1080);

    [AvaloniaTest]
    public async Task A_platform_preset_applies_the_frame_size_and_rates_it_is_named_after()
    {
        EditViewModel editor = await OpenEditor();
        using var output = CreateOutput(editor);
        JsonObject preset = CreatePreset(new PixelSize(3840, 2160), new Rational(60, 1), keyframeRate: 120, 48000);
        preset[OutputViewModel.AppliesFrameSizeAndRatesKey] = true;

        output.Apply(preset);

        EncodingController controller = output.Controller.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(controller.VideoSettings.SourceSize, Is.EqualTo(s_sceneSize));
            Assert.That(controller.VideoSettings.DestinationSize, Is.EqualTo(new PixelSize(3840, 2160)));
            Assert.That(controller.VideoSettings.FrameRate, Is.EqualTo(new Rational(60, 1)));
            Assert.That(controller.VideoSettings.KeyframeRate, Is.EqualTo(120));
            Assert.That(controller.AudioSettings.SampleRate, Is.EqualTo(48000));
        });
    }

    [AvaloniaTest]
    public async Task A_preset_saved_from_an_output_keeps_the_frame_size_and_rates()
    {
        EditViewModel editor = await OpenEditor();
        JsonObject preset;
        using (OutputViewModel saved = CreateOutput(editor))
        {
            EncodingController source = saved.Controller.Value!;
            source.VideoSettings.DestinationSize = new PixelSize(3840, 2160);
            source.VideoSettings.FrameRate = new Rational(60, 1);
            source.VideoSettings.Bitrate = 45_000_000;
            source.AudioSettings.SampleRate = 48000;
            preset = saved.ToPreset();
        }

        using var output = CreateOutput(editor);
        output.Apply(preset);

        EncodingController controller = output.Controller.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(controller.VideoSettings.DestinationSize, Is.EqualTo(s_sceneSize));
            Assert.That(controller.VideoSettings.FrameRate, Is.EqualTo(new Rational(30, 1)));
            Assert.That(controller.AudioSettings.SampleRate, Is.EqualTo(44100));
            Assert.That(controller.VideoSettings.Bitrate, Is.EqualTo(45_000_000), "the rest of the preset still applies");
        });
    }

    // Renaming the output file keeps the encoder, so the settings move to a new controller. The output size moves
    // with them, like the frame rate, whether the user typed it or a preset set it.
    [AvaloniaTest]
    public async Task Changing_only_the_destination_keeps_the_output_size()
    {
        EditViewModel editor = await OpenEditor();
        using var output = CreateOutput(editor);
        EncodingController original = output.Controller.Value!;
        original.VideoSettings.DestinationSize = new PixelSize(1280, 720);
        original.VideoSettings.FrameRate = new Rational(24, 1);

        output.DestinationFile.Value = Path.Combine(Path.GetDirectoryName(original.OutputFile)!, "renamed.mp4");

        EncodingController renamed = output.Controller.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(renamed, Is.Not.SameAs(original));
            Assert.That(renamed.VideoSettings.DestinationSize, Is.EqualTo(new PixelSize(1280, 720)));
            Assert.That(renamed.VideoSettings.FrameRate, Is.EqualTo(new Rational(24, 1)));
            Assert.That(renamed.VideoSettings.SourceSize, Is.EqualTo(s_sceneSize));
        });
    }

    // The platform presets are saved with the user's own, so the ones saved before presets applied their frame size
    // and rates lack the key that says so. Restoring them adds it, once, and the restored preset then applies them.
    [AvaloniaTest]
    public async Task A_saved_platform_preset_is_updated_to_apply_its_frame_size_and_rates()
    {
        EditViewModel editor = await OpenEditor();
        var saved = new OutputPresetItem(
            SceneOutputExtension.Instance,
            CreatePreset(new PixelSize(3840, 2160), new Rational(60, 1), keyframeRate: 120, 48000),
            "YouTube 4K 60fps",
            "Platform.YouTube_4K60");

        Assert.Multiple(() =>
        {
            Assert.That(OutputPresetService.UpdateSavedPlatformPreset(saved, "Platform.YouTube_4K60"), Is.True,
                "the update has to be reported, or it is never written back to the saved presets");
            Assert.That(OutputPresetService.UpdateSavedPlatformPreset(saved, "Platform.YouTube_4K60"), Is.False);
        });

        using var output = CreateOutput(editor);
        saved.Apply(output);

        EncodingController controller = output.Controller.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(controller.VideoSettings.DestinationSize, Is.EqualTo(new PixelSize(3840, 2160)));
            Assert.That(controller.VideoSettings.FrameRate, Is.EqualTo(new Rational(60, 1)));
            Assert.That(controller.VideoSettings.KeyframeRate, Is.EqualTo(120), "two seconds at 60 fps");
            Assert.That(controller.AudioSettings.SampleRate, Is.EqualTo(48000));
        });
    }

    private static async Task<EditViewModel> OpenEditor()
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "output-presets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(
            s_sceneSize.Width, s_sceneSize.Height, 30, 44100, "presets", directory))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static OutputViewModel CreateOutput(EditViewModel editor)
    {
        var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(
            Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "presets.mp4");
        output.SelectedEncoder.Value = new TestEncoder();
        return output;
    }

    private static JsonObject CreatePreset(PixelSize size, Rational frameRate, int keyframeRate, int sampleRate)
    {
        var video = new VideoEncoderSettings
        {
            SourceSize = size,
            DestinationSize = size,
            FrameRate = frameRate,
            KeyframeRate = keyframeRate,
            Bitrate = 45_000_000
        };
        var audio = new AudioEncoderSettings { SampleRate = sampleRate };
        return new JsonObject
        {
            [nameof(OutputViewModel.VideoSettings)] = EncoderSettingsJson.Serialize(video),
            [nameof(OutputViewModel.AudioSettings)] = EncoderSettingsJson.Serialize(audio)
        };
    }

    private sealed class TestEncoder : ControllableEncodingExtension
    {
        public override IEnumerable<string> SupportExtensions() => [".mp4"];

        public override EncodingController CreateController(string file) => new TestController(file);
    }

    private sealed class TestController(string file) : EncodingController(file)
    {
        public override VideoEncoderSettings VideoSettings { get; } = new();

        public override AudioEncoderSettings AudioSettings { get; } = new();

        public override ValueTask Encode(IFrameProvider frames, ISampleProvider samples, CancellationToken token)
            => throw new NotSupportedException();
    }
}

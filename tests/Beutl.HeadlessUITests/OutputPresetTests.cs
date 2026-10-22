using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Components.OutputTab;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Encoding;
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
    // with them, like the rates, whether the user typed it or a preset set it.
    [AvaloniaTest]
    public async Task Changing_only_the_destination_keeps_the_output_size_and_rates()
    {
        EditViewModel editor = await OpenEditor();
        using var output = CreateOutput(editor);
        EncodingController original = output.Controller.Value!;
        original.VideoSettings.DestinationSize = new PixelSize(1280, 720);
        original.VideoSettings.FrameRate = new Rational(24000, 1001);
        original.AudioSettings.SampleRate = 96000;

        output.DestinationFile.Value = Path.Combine(Path.GetDirectoryName(original.OutputFile)!, "renamed.mp4");

        EncodingController renamed = output.Controller.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(renamed, Is.Not.SameAs(original));
            Assert.That(renamed.VideoSettings.DestinationSize, Is.EqualTo(new PixelSize(1280, 720)));
            Assert.That(renamed.VideoSettings.FrameRate, Is.EqualTo(new Rational(24000, 1001)));
            Assert.That(renamed.AudioSettings.SampleRate, Is.EqualTo(96000));
            Assert.That(renamed.VideoSettings.SourceSize, Is.EqualTo(s_sceneSize));
        });
    }

    // Only settings carried over to a new file keep their output size. A new output, or another encoder, starts at
    // the scene's size even when the encoder gives its settings an output size of its own.
    [AvaloniaTest]
    public async Task A_new_controller_starts_at_the_scene_size_whatever_the_encoder_default()
    {
        EditViewModel editor = await OpenEditor();
        var sized = new TestEncoder(defaultSize: new PixelSize(640, 360));
        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "new.mp4");
        output.SelectedEncoder.Value = sized;
        PixelSize created = output.Controller.Value!.VideoSettings.DestinationSize;

        output.SelectedEncoder.Value = new TestEncoder();
        output.Controller.Value!.VideoSettings.DestinationSize = new PixelSize(1280, 720);
        output.SelectedEncoder.Value = sized;

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.EqualTo(s_sceneSize), "a new output");
            Assert.That(output.Controller.Value!.VideoSettings.DestinationSize, Is.EqualTo(s_sceneSize),
                "another encoder");
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

    // A start that matched the saved presets by name records it, so a preset saved later under a platform preset's
    // name stays the user's own on the next start.
    [Test]
    public void Names_are_matched_once_so_a_later_user_preset_keeps_its_name_and_no_key()
    {
        string file = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"output-presets-{Guid.NewGuid():N}.json");
        var preferences = new TestPreferences();
        try
        {
            SavePresetWithoutKey(file);
            OutputPresetItem firstStart = new OutputPresetService(file, preferences).Items[0];
            Assert.Multiple(() =>
            {
                Assert.That(firstStart.PresetKey, Is.EqualTo("Platform.YouTube_1080p60"), "the first start still matches by name");
                Assert.That(preferences.Matched, Is.True);
            });

            SavePresetWithoutKey(file);
            Assert.That(new OutputPresetService(file, preferences).Items[0].PresetKey, Is.Null);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // Every start reads the record, so one after it was written leaves the preferences file alone.
    [Test]
    public void A_start_after_the_names_were_matched_writes_no_preference()
    {
        string file = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"output-presets-{Guid.NewGuid():N}.json");
        var preferences = new TestPreferences { Matched = true };
        try
        {
            SavePresetWithoutKey(file);
            _ = new OutputPresetService(file, preferences);

            Assert.That(preferences.Writes, Is.Zero);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // A record that cannot be written only means the next start matches the names again, so the presets still load.
    [Test]
    public void A_record_that_cannot_be_written_still_restores_the_presets()
    {
        string file = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"output-presets-{Guid.NewGuid():N}.json");
        var preferences = new TestPreferences { FailsToWrite = true };
        try
        {
            SavePresetWithoutKey(file);
            OutputPresetService? service = null;

            Assert.DoesNotThrow(() => service = new OutputPresetService(file, preferences));
            Assert.Multiple(() =>
            {
                Assert.That(preferences.Writes, Is.EqualTo(1), "the record was tried");
                Assert.That(service!.Items[0].PresetKey, Is.EqualTo("Platform.YouTube_1080p60"));
            });
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void SavePresetWithoutKey(string file)
    {
        var item = new OutputPresetItem(
            SceneOutputExtension.Instance,
            CreatePreset(new PixelSize(1920, 1080), new Rational(60, 1), keyframeRate: 120, 48000),
            Beutl.Language.Strings.Preset_YouTube_1080p60);
        new JsonArray(OutputPresetItem.ToJson(item)).JsonSave(file);
    }

    private static async Task<EditViewModel> OpenEditor()
        => (await TestShell.OpenNewSceneAsync("output-presets", s_sceneSize.Width, s_sceneSize.Height)).Editor;

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

    private sealed class TestPreferences : IPreferences
    {
        public bool Matched { get; set; }

        public bool FailsToWrite { get; init; }

        public int Writes { get; private set; }

        public void Set<T>(string key, T value)
        {
            Writes++;
            if (FailsToWrite) throw new IOException("The preferences file cannot be written.");
            if (key == OutputPresetService.PlatformPresetNamesMatchedKey && value is bool matched) Matched = matched;
        }

        public T Get<T>(string key, T defaultValue)
            => key == OutputPresetService.PlatformPresetNamesMatchedKey && Matched is T matched ? matched : defaultValue;
    }
}

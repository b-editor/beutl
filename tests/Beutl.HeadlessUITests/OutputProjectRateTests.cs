using System.Text.Json.Nodes;
using Beutl.Audio;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class OutputProjectRateTests
{
    [AvaloniaTest]
    public async Task Output_view_shows_project_rates()
    {
        EditViewModel editor = await OpenEditor();
        const int packageId = 2858;
        var encoder = new TestEncoder();
        editor.ExtensionProvider.AddExtensions(packageId, [encoder]);
        using var output = CreateOutput(editor, encoder);
        var view = new OutputView { DataContext = output };
        var window = new Window { Content = view, Width = 650, Height = 720 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(view.GetVisualDescendants().OfType<RationalEditor>().Single().Value,
                    Is.EqualTo(new Rational(60, 1)));
                Assert.That(view.GetVisualDescendants().OfType<NumberEditor<int>>().Select(control => control.Value),
                    Does.Contain(48000));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            editor.ExtensionProvider.RemoveExtensions(packageId);
        }
    }

    [AvaloniaTest]
    public async Task New_output_and_preview_use_project_rates()
    {
        EditViewModel editor = await OpenEditor();
        using var output = CreateOutput(editor, new TestEncoder());

        Assert.Multiple(() =>
        {
            Assert.That(output.Controller.Value!.VideoSettings.FrameRate.Value, Is.EqualTo(new Rational(60, 1)));
            Assert.That(output.Controller.Value.AudioSettings.SampleRate.Value, Is.EqualTo(48000));
            Assert.That(editor.Composer.Value.SampleRate, Is.EqualTo(48000));
        });
    }

    [AvaloniaTest]
    public async Task Switching_encoders_initializes_project_rates()
    {
        EditViewModel editor = await OpenEditor();
        using var output = CreateOutput(editor, new TestEncoder());
        SetCustomRates(output);

        output.SelectedEncoder.Value = new OtherTestEncoder();

        Assert.Multiple(() =>
        {
            Assert.That(output.Controller.Value!.VideoSettings.FrameRate.Value, Is.EqualTo(new Rational(60, 1)));
            Assert.That(output.Controller.Value.AudioSettings.SampleRate.Value, Is.EqualTo(48000));
        });
    }

    [AvaloniaTest]
    public async Task Restoring_profile_preserves_saved_rates()
    {
        EditViewModel editor = await OpenEditor();
        const int packageId = 2858;
        var encoder = new TestEncoder();
        editor.ExtensionProvider.AddExtensions(packageId, [encoder]);
        try
        {
            using var original = CreateOutput(editor, encoder);
            SetCustomRates(original);
            var json = new JsonObject();
            original.WriteToJson(json);

            using var restored = new OutputViewModel(editor);
            restored.ReadFromJson(json);

            AssertCustomRates(restored);
        }
        finally
        {
            editor.ExtensionProvider.RemoveExtensions(packageId);
        }
    }

    [AvaloniaTest]
    public async Task Preview_publishes_the_project_channels_while_export_stays_stereo()
    {
        EditViewModel editor = await OpenEditor();
        Project project = editor.Scene.FindHierarchicalParent<Project>()!;
        bool? playingAtSwap = null;
        using var composerSubscription = editor.Composer.Skip(1).Subscribe(_ => playingAtSwap = editor.Player.IsPlaying.Value);
        editor.Player.IsPlaying.Value = true;
        project.ChannelLayout.Value = SpeakerLayout.Mono;
        project.ChannelLayout.Value = SpeakerLayout.Surround51;
        HeadlessTestHelpers.Settle();
        for (int attempt = 0; attempt < 200 && editor.Composer.Value.ChannelLayout != AudioChannelLayout.Surround51; attempt++)
            await Task.Delay(10);
        Assert.That(editor.Composer.Value.ChannelLayout, Is.EqualTo(AudioChannelLayout.Surround51));
        Assert.That(playingAtSwap, Is.False, "Playback must stop before the composer is replaced.");
        var snapshots = new List<Beutl.Editor.Models.AudioFrameSnapshot>();
        using var subscription = ((IPreviewPlayer)editor.Player).AudioFramePushed.Subscribe(snapshots.Add);
        using var audio = new AudioBuffer(48000, 6, 32);
        for (int ch = 0; ch < 6; ch++) audio.GetChannelData(ch).Fill(ch / 16f);
        editor.Player.PublishAudioSnapshot(audio, MediaTime.Zero);
        Assert.That(snapshots.Single().ChannelCount, Is.EqualTo(6));
        Assert.That(snapshots.Single().Interleaved.Length, Is.EqualTo(192));
        Assert.That(snapshots.Single().Interleaved[5], Is.EqualTo(5 / 16f));

        var encoder = new LayoutEncoder();
        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(BeutlHomeIsolation.CurrentHome!, "stereo-export.mp4");
        output.SelectedEncoder.Value = encoder;
        await output.RunAsync(CancellationToken.None);
        Assert.That(encoder.Channels, Is.EqualTo(2));
    }

    private sealed class LayoutEncoder : ControllableEncodingExtension
    {
        public int Channels;
        public override IEnumerable<string> SupportExtensions() => [".mp4"];
        public override EncodingController CreateController(string file) => new LayoutController(file, this);
    }

    private sealed class LayoutController(string file, LayoutEncoder owner) : EncodingController(file)
    {
        public override bool IsVideoEnabled => false;
        public override VideoEncoderSettings VideoSettings { get; } = new();
        public override AudioEncoderSettings AudioSettings { get; } = new();
        public override async ValueTask Encode(IFrameProvider frames, ISampleProvider samples, CancellationToken token)
        {
            using var audio = await samples.ReadNextSamples(32);
            owner.Channels = audio!.ChannelCount;
            await File.WriteAllTextAsync(OutputFile, "test output", token);
        }
    }

    private static async Task<EditViewModel> OpenEditor()
        => (await TestShell.OpenNewSceneAsync("rates", 320, 180, frameRate: 60, sampleRate: 48000)).Editor;

    private static OutputViewModel CreateOutput(EditViewModel editor, TestEncoder encoder)
    {
        var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(BeutlHomeIsolation.CurrentHome!, "rates.mp4");
        output.SelectedEncoder.Value = encoder;
        return output;
    }

    private static void SetCustomRates(OutputViewModel output)
    {
        output.Controller.Value!.VideoSettings.FrameRate.Value = new Rational(24000, 1001);
        output.Controller.Value.AudioSettings.SampleRate.Value = 96000;
    }

    private static void AssertCustomRates(OutputViewModel output)
    {
        Assert.That(output.Controller.Value, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(output.Controller.Value!.VideoSettings.FrameRate.Value, Is.EqualTo(new Rational(24000, 1001)));
            Assert.That(output.Controller.Value.AudioSettings.SampleRate.Value, Is.EqualTo(96000));
        });
    }

    private sealed class OtherTestEncoder : TestEncoder;
}

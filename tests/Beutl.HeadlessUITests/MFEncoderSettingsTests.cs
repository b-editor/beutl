#if !WITHOUT_MEDIA_FOUNDATION
using System.Globalization;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Embedding.MediaFoundation.Encoding;
using Beutl.Engine;
using Beutl.Media.Source;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public sealed class MFEncoderSettingsTests
{
    [AvaloniaTest]
    [TestCase(".wav", MFVideoCodec.Auto, MFAudioCodec.Auto, true, true)]
    [TestCase(".mp3", MFVideoCodec.Auto, MFAudioCodec.Auto, true, true)]
    [TestCase(".aac", MFVideoCodec.Auto, MFAudioCodec.Auto, true, true)]
    [TestCase(".mp4", MFVideoCodec.None, MFAudioCodec.Auto, true, true)]
    [TestCase(".mp4", MFVideoCodec.Auto, MFAudioCodec.None, false, true)]
    [TestCase(".mp4", MFVideoCodec.Auto, MFAudioCodec.Auto, null, true)]
    [TestCase(".mp4", MFVideoCodec.Auto, MFAudioCodec.Auto, true, false)]
    [TestCase(".wav", MFVideoCodec.Auto, MFAudioCodec.Auto, false, false)]
    public async Task ExportPreflight_OnlyRequiresEnabledStreamSources(
        string extension, MFVideoCodec video, MFAudioCodec audio, bool? missingVideo, bool succeeds)
    {
        await TestReset.ResetShellAsync();
        TestGpu.RequireAnyDevice();
        var editor = await TestShell.OpenNewSceneEditorAsync("mf-preflight", 64, 48);
        editor.Scene.Duration.Value = MediaTime.FromSeconds(0.2);
        var videoProbe = new ExportPassProbe { Target = CompositionTarget.Graphics };
        var audioProbe = new ExportPassProbe { Target = CompositionTarget.Audio };
        await TestShell.AddElementAsync(editor, () => videoProbe, MediaTime.Zero, editor.Scene.Duration.Value);
        await TestShell.AddElementAsync(editor, () => audioProbe, MediaTime.Zero, editor.Scene.Duration.Value);
        string missingPath = Path.Combine(TestShell.ProjectRoot(editor), missingVideo == true ? "missing.mov" : "missing.wav");
        if (missingVideo.HasValue)
        {
            await TestShell.AddElementAsync(editor, () =>
            {
                if (missingVideo == true)
                {
                    var source = new VideoSource();
                    source.ReadFrom(new Uri(missingPath));
                    return new Beutl.Graphics.SourceVideo { Source = { Value = source } };
                }
                var sound = new SoundSource();
                sound.ReadFrom(new Uri(missingPath));
                return new SourceSound { Source = { Value = sound } };
            }, MediaTime.Zero, editor.Scene.Duration.Value);
        }

        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(TestShell.ProjectRoot(editor), Guid.NewGuid() + extension);
        output.SelectedEncoder.Value = new MFEncodingExtension();
        HeadlessTestHelpers.Settle();
        var controller = (MFEncodingController)output.Controller.Value!;
        controller.VideoSettings.UseHardwareAcceleration.Value = false;
        controller.VideoSettings.Codec.Value = video;
        controller.AudioSettings.Codec.Value = audio;
        if (!controller.IsVideoEnabled)
        {
            editor.Scene.FrameSize.Value = new Media.PixelSize(5000, 48);
            output.SupersampleFactor.Value = 4;
            Assert.That(output.SupersampleWarning.Value, Is.Null);
            Assert.That(output.CanEncode.Value, Is.True);
        }
        bool hadIntermediateProgress = false;
        using var progress = output.ProgressValue.Subscribe(value =>
            hadIntermediateProgress |= value > 0 && value < output.ProgressMax.Value);

        if (succeeds)
        {
            await output.RunAsync(CancellationToken.None);
            Assert.That(output.WasCancelled.Value, Is.False);
            Assert.That(File.Exists(output.DestinationFile.Value), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(videoProbe.EvaluationCount > 0, Is.EqualTo(controller.IsVideoEnabled));
                Assert.That(audioProbe.EvaluationCount > 0, Is.EqualTo(controller.IsAudioEnabled));
                Assert.That(hadIntermediateProgress, Is.True, "Progress must advance before completion.");
                Assert.That(output.ProgressMax.Value, Is.EqualTo(editor.Scene.Duration.Value.ToSeconds()
                    * ((controller.IsVideoEnabled ? 1 : 0) + (controller.IsAudioEnabled ? 1 : 0))));
            });
        }
        else
        {
            await Assert.ThatAsync(() => output.RunAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains(missingPath));
            Assert.That(File.Exists(output.DestinationFile.Value), Is.False);
        }
    }

    [AvaloniaTest]
    public async Task SupersamplingWarning_TracksEnabledVideo()
    {
        await TestReset.ResetShellAsync();
        var editor = await TestShell.OpenNewSceneEditorAsync("mf-supersampling", 5000, 48);
        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(TestShell.ProjectRoot(editor), "out.mp4");
        output.SelectedEncoder.Value = new MFEncodingExtension();
        output.SupersampleFactor.Value = 4;

        void AssertState(bool blocked)
        {
            HeadlessTestHelpers.Settle();
            Assert.That(output.SupersampleWarning.Value != null, Is.EqualTo(blocked));
            Assert.That(output.CanEncode.Value, Is.EqualTo(!blocked));
        }

        AssertState(true);
        await Assert.ThatAsync(() => output.RunAsync(CancellationToken.None), Throws.TypeOf<InvalidOperationException>());
        var settings = ((MFEncodingController)output.Controller.Value!).VideoSettings;
        settings.Codec.Value = MFVideoCodec.None;
        AssertState(false);
        // Incompatible selections must not terminate the settings subscription.
        settings.Codec.Value = MFVideoCodec.WMV9;
        AssertState(true);
        settings.Codec.Value = MFVideoCodec.Auto;
        output.DestinationFile.Value = Path.ChangeExtension(output.DestinationFile.Value, ".wav");
        AssertState(false);
        output.DestinationFile.Value = Path.ChangeExtension(output.DestinationFile.Value, ".mp4");
        AssertState(true);
        settings.Codec.Value = MFVideoCodec.None;
        AssertState(true); // The old controller is no longer observed.
        output.DestinationFile.Value = Path.ChangeExtension(output.DestinationFile.Value, ".tmp");
        AssertState(true);
        output.DestinationFile.Value = Path.ChangeExtension(output.DestinationFile.Value, ".mp4");
        output.SelectedEncoder.Value = new MFEncodingExtension();
        AssertState(true);
        output.SupersampleFactor.Value = 1;
        AssertState(false);
        output.SupersampleFactor.Value = 4;
        editor.Scene.FrameSize.Value = new Media.PixelSize(64, 48);
        AssertState(false);
        editor.Scene.FrameSize.Value = new Media.PixelSize(5000, 48);
        AssertState(true);
    }

    [AvaloniaTest]
    [TestCase("ja", false)]
    [TestCase("en", true)]
    public void SettingsAreEditableAndPreservedWhenCopied(string language, bool light)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var settings = new MFVideoEncoderSettings { DestinationSize = { Value = new Media.PixelSize(1920, 1080) } };
        using var videoModel = new EncoderSettingsViewModel(settings, TestShell.Extensions);
        var audioSettings = new MFAudioEncoderSettings();
        using var audioModel = new EncoderSettingsViewModel(audioSettings, TestShell.Extensions);
        var video = new PropertiesEditor { DataContext = videoModel, HideMenu = true };
        var audio = new PropertiesEditor { DataContext = audioModel, HideMenu = true };
        var window = new Window
        {
            Width = 600,
            Height = 700,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Extensions.MediaFoundation.Properties.Strings.EncodingName, FontSize = 18 },
                    new TextBlock { Text = Beutl.Language.Strings.Video }, video,
                    new TextBlock { Text = Beutl.Language.Strings.Audio }, audio,
                }
            }
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(videoModel.Properties.Count, Is.EqualTo(6), "SourceSize must remain hidden.");
            Assert.That(audioModel.Properties.Count, Is.EqualTo(5));
            var hardware = video.GetVisualDescendants().OfType<BooleanEditor>().Single();
            Assert.That(hardware.Value, Is.True);
            Assert.That(hardware.Header, Is.EqualTo(language == "ja"
                ? "ハードウェアアクセラレーションを使用" : "Use hardware acceleration"));
            HeadlessTestHelpers.Click(hardware.GetVisualDescendants().OfType<ToggleButton>().Single());
            Assert.That(settings.UseHardwareAcceleration.Value, Is.False);

            var videoCodec = video.GetVisualDescendants().OfType<EnumEditor>().Single();
            Assert.That(videoCodec.Items!.Select(item => item.Value), Does.Contain(MFVideoCodec.HEVC));
            videoCodec.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = (int)MFVideoCodec.HEVC;
            Assert.That(settings.Codec.Value, Is.EqualTo(MFVideoCodec.HEVC));
            var audioCodec = audio.GetVisualDescendants().OfType<EnumEditor>()
                .Single(editor => editor.Items![0].Value is MFAudioCodec);
            Assert.That(audioCodec.Items!.Select(item => item.Value), Does.Contain(MFAudioCodec.ALAC));
            audioCodec.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = (int)MFAudioCodec.ALAC;
            Assert.That(audioSettings.Codec.Value, Is.EqualTo(MFAudioCodec.ALAC));
            var bitDepth = audio.GetVisualDescendants().OfType<EnumEditor>()
                .Single(editor => editor.Items![0].Value is MFAudioBitDepth);
            bitDepth.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = 2;
            Assert.That(audioSettings.BitDepth.Value, Is.EqualTo(MFAudioBitDepth.Bit24));
            HeadlessTestHelpers.Render(3);

            var restored = new MFVideoEncoderSettings();
            EncoderSettingsJson.CopyTo(settings, restored);
            Assert.That(restored.UseHardwareAcceleration.Value, Is.False);
            Assert.That(restored.Codec.Value, Is.EqualTo(MFVideoCodec.HEVC));
            Assert.That(restored.DestinationSize.Value, Is.EqualTo(settings.DestinationSize.Value));
            Assert.That(restored.FrameRate.Value, Is.EqualTo(settings.FrameRate.Value));
            var restoredAudio = new MFAudioEncoderSettings();
            EncoderSettingsJson.CopyTo(audioSettings, restoredAudio);
            Assert.That(restoredAudio.Codec.Value, Is.EqualTo(MFAudioCodec.ALAC));
            Assert.That(restoredAudio.BitDepth.Value, Is.EqualTo(MFAudioBitDepth.Bit24));
            HeadlessCapture.SaveIfRequested(window, nameof(MFEncoderSettingsTests), $"settings-{language}");
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}

internal sealed partial class ExportPassProbe : EngineObject, IFlowOperator
{
    private int _evaluationCount;
    public CompositionTarget Target { get; init; }
    public int EvaluationCount => Volatile.Read(ref _evaluationCount);
    public override CompositionTarget GetCompositionTarget() => Target;

    public partial class Resource
    {
        partial void PostReconcile(ExportPassProbe obj, CompositionContext context)
        {
            // Final export uses its own resources; live preview must not affect these counts.
            if (context.DisableResourceShare)
                Interlocked.Increment(ref obj._evaluationCount);
        }
    }
}
#endif

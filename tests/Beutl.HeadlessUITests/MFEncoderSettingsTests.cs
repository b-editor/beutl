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
using Beutl.Editor.Services;
using Beutl.Embedding.MediaFoundation.Encoding;
using Beutl.Engine;
using Beutl.Media.Source;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Editors;

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
        editor.Scene.Duration = TimeSpan.FromMilliseconds(200);
        var videoProbe = new ExportPassProbe { Target = CompositionTarget.Graphics };
        var audioProbe = new ExportPassProbe { Target = CompositionTarget.Audio };
        await TestShell.AddElementAsync(editor, () => videoProbe, TimeSpan.Zero, editor.Scene.Duration);
        await TestShell.AddElementAsync(editor, () => audioProbe, TimeSpan.Zero, editor.Scene.Duration);
        string missingPath = Path.Combine(TestShell.ProjectRoot(editor), missingVideo == true ? "missing.mov" : "missing.wav");
        if (missingVideo.HasValue)
        {
            await TestShell.AddElementAsync(editor, () =>
            {
                if (missingVideo == true)
                {
                    var source = new VideoSource();
                    source.ReadFrom(new Uri(missingPath));
                    return new Beutl.Graphics.SourceVideo { Source = { CurrentValue = source } };
                }
                var sound = new SoundSource();
                sound.ReadFrom(new Uri(missingPath));
                return new SourceSound { Source = { CurrentValue = sound } };
            }, TimeSpan.Zero, editor.Scene.Duration);
        }

        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(TestShell.ProjectRoot(editor), Guid.NewGuid() + extension);
        output.SelectedEncoder.Value = new MFEncodingExtension();
        HeadlessTestHelpers.Settle();
        var controller = (MFEncodingController)output.Controller.Value!;
        controller.VideoSettings.UseHardwareAcceleration = false;
        controller.VideoSettings.Codec = video;
        controller.AudioSettings.Codec = audio;
        bool hadIntermediateProgress = false;
        using var progress = output.ProgressValue.Subscribe(value =>
            hadIntermediateProgress |= value > 0 && value < output.ProgressMax.Value);

        Exception? failure = null;
        try
        {
            await output.RunAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        if (succeeds)
        {
            Assert.That(failure, Is.Null);
            Assert.That(output.IsCompleted.Value, Is.True);
            Assert.That(File.Exists(output.DestinationFile.Value), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(videoProbe.EvaluationCount > 0, Is.EqualTo(controller.IsVideoEnabled));
                Assert.That(audioProbe.EvaluationCount > 0, Is.EqualTo(controller.IsAudioEnabled));
                Assert.That(hadIntermediateProgress, Is.True, "Progress must advance before completion.");
                Assert.That(output.ProgressMax.Value, Is.EqualTo(editor.Scene.Duration.TotalSeconds
                    * ((controller.IsVideoEnabled ? 1 : 0) + (controller.IsAudioEnabled ? 1 : 0))));
            });
        }
        else
        {
            Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
            Assert.That(failure!.Message, Does.Contain(missingPath));
            Assert.That(File.Exists(output.DestinationFile.Value), Is.False);
        }
    }

    [AvaloniaTest]
    [TestCase("ja", false)]
    [TestCase("en", true)]
    public void SettingsAreEditableAndPreservedWhenCopied(string language, bool light)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var settings = new MFVideoEncoderSettings { DestinationSize = new Media.PixelSize(1920, 1080) };
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
            Assert.That(settings.UseHardwareAcceleration, Is.False);

            var videoCodec = video.GetVisualDescendants().OfType<EnumEditor>().Single();
            Assert.That(videoCodec.Items!.Select(item => item.Value), Does.Contain(MFVideoCodec.HEVC));
            videoCodec.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = (int)MFVideoCodec.HEVC;
            Assert.That(settings.Codec, Is.EqualTo(MFVideoCodec.HEVC));
            var audioCodec = audio.GetVisualDescendants().OfType<EnumEditor>()
                .Single(editor => editor.Items![0].Value is MFAudioCodec);
            Assert.That(audioCodec.Items!.Select(item => item.Value), Does.Contain(MFAudioCodec.ALAC));
            audioCodec.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = (int)MFAudioCodec.ALAC;
            Assert.That(audioSettings.Codec, Is.EqualTo(MFAudioCodec.ALAC));
            var bitDepth = audio.GetVisualDescendants().OfType<EnumEditor>()
                .Single(editor => editor.Items![0].Value is MFAudioBitDepth);
            bitDepth.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = 2;
            Assert.That(audioSettings.BitDepth, Is.EqualTo(MFAudioBitDepth.Bit24));
            HeadlessTestHelpers.Render(3);

            var restored = new MFVideoEncoderSettings();
            EncoderSettingsJson.CopyTo(settings, restored);
            Assert.That(restored.UseHardwareAcceleration, Is.False);
            Assert.That(restored.Codec, Is.EqualTo(MFVideoCodec.HEVC));
            Assert.That(restored.DestinationSize, Is.EqualTo(settings.DestinationSize));
            Assert.That(restored.FrameRate, Is.EqualTo(settings.FrameRate));
            var restoredAudio = new MFAudioEncoderSettings();
            EncoderSettingsJson.CopyTo(audioSettings, restoredAudio);
            Assert.That(restoredAudio.Codec, Is.EqualTo(MFAudioCodec.ALAC));
            Assert.That(restoredAudio.BitDepth, Is.EqualTo(MFAudioBitDepth.Bit24));
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

#if !WITHOUT_MEDIA_FOUNDATION
using System.Globalization;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Embedding.MediaFoundation.Encoding;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views.Editors;

namespace Beutl.HeadlessUITests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public sealed class MFEncoderSettingsTests
{
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
#endif

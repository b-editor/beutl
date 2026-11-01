using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.PropertyEditors;
using Beutl.Language;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AudioChannelsEditorTests
{
    [AvaloniaTest]
    [TestCase(typeof(FFmpegAudioEncoderSettings))]
    [TestCase(typeof(AVFAudioEncoderSettings))]
#if !WITHOUT_MEDIA_FOUNDATION
    [TestCase(typeof(Beutl.Embedding.MediaFoundation.Encoding.MFAudioEncoderSettings))]
#endif
    public void Built_in_editor_offers_labelled_layouts_and_scales_each_selection_once(Type settingsType)
    {
        var settings = (AudioEncoderSettings)Activator.CreateInstance(settingsType)!;
        int defaultBitrate = settings.Bitrate.Value;
        using var model = new EncoderSettingsViewModel(settings, TestShell.MainViewModel.ExtensionProvider);
        var view = new PropertiesEditor { DataContext = model, HideMenu = true };
        var window = new Window { Content = view, Width = 640, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            EnumEditor editor = ChannelEditor(view);
            AssertChoices(editor);
            Assert.That(editor.Items![editor.SelectedIndex].Value, Is.EqualTo(2));
            Assert.That(settings.Bitrate.Value, Is.EqualTo(defaultBitrate), "Showing the editor is not an edit.");

            var combo = editor.GetVisualDescendants().OfType<ComboBox>().Single();
            foreach (int channels in new[] { 6, 8, 2 })
            {
                combo.SelectedIndex = Array.IndexOf(new[] { 1, 2, 6, 8 }, channels);
                HeadlessTestHelpers.Settle();

                Assert.That(settings.Channels.Value, Is.EqualTo(channels));
                Assert.That(settings.Bitrate.Value, Is.EqualTo(defaultBitrate > 0 ? defaultBitrate * channels / 2 : defaultBitrate));
            }

            Assert.That(view.GetVisualDescendants().OfType<NumberEditor<int>>().Select(e => e.Header),
                Does.Contain(Strings.SampleRate).And.Contain(Strings.Bitrate).And.Not.Contain(Strings.Channels));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Saved_values_copies_and_presets_update_the_open_editor_without_extra_scaling()
    {
        var settings = new FFmpegAudioEncoderSettings { Channels = { Value = 7 }, Bitrate = { Value = 192_000 } };
        using var model = new EncoderSettingsViewModel(settings, TestShell.MainViewModel.ExtensionProvider);
        var view = new PropertiesEditor { DataContext = model, HideMenu = true };
        var window = new Window { Content = view, Width = 640, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            EnumEditor editor = ChannelEditor(view);
            Assert.That(editor.Items!.Select(item => item.Value), Is.EqualTo(new[] { 1, 2, 6, 8, 7 }));
            Assert.That(editor.Items![editor.SelectedIndex].DisplayName, Is.EqualTo("7"));
            Assert.That(settings.Channels.Value, Is.EqualTo(7));
            Assert.That(settings.Bitrate.Value, Is.EqualTo(192_000));

            EncoderSettingsJson.Populate(settings, new JsonObject { ["Channels"] = 3, ["Bitrate"] = 111_000 });
            HeadlessTestHelpers.Settle();
            Assert.That(editor.Items!.Select(item => item.Value), Is.EqualTo(new[] { 1, 2, 6, 8, 3 }));
            Assert.That(editor.Items[editor.SelectedIndex].Value, Is.EqualTo(3));
            Assert.That(settings.Bitrate.Value, Is.EqualTo(111_000));

            EncoderSettingsJson.CopyTo(new AudioEncoderSettings
            {
                Channels = { Value = 6 },
                Bitrate = { Value = 384_000 }
            }, settings);
            HeadlessTestHelpers.Settle();
            AssertChoices(editor);
            Assert.That(editor.Items![editor.SelectedIndex].Value, Is.EqualTo(6));
            Assert.That(settings.Bitrate.Value, Is.EqualTo(384_000));

            EncoderSettingsJson.PopulateAudioPreset(settings,
                new JsonObject { ["Channels"] = 2, ["Bitrate"] = 192_000 }, appliesSampleRate: false);
            HeadlessTestHelpers.Settle();
            Assert.That(editor.Items![editor.SelectedIndex].Value, Is.EqualTo(6));
            Assert.That(settings.Channels.Value, Is.EqualTo(6));
            Assert.That(settings.Bitrate.Value, Is.EqualTo(576_000));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase("en-US")]
    [TestCase("ja-JP")]
    public async Task Output_tab_shows_localized_ffmpeg_audio_settings(string cultureName)
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        try
        {
            var (project, _, edit) = await TestShell.OpenNewSceneAsync("audio-settings", 320, 180);
            project.ChannelLayout.Value = SpeakerLayout.Surround51;
            var encoder = new FFmpegControlledEncodingExtension();
            const int packageId = 3783;
            edit.ExtensionProvider.AddExtensions(packageId, [encoder, new FFmpegEncoderSpecializedPropertyExtension()]);
            using var output = new OutputViewModel(edit);
            output.DestinationFile.Value = Path.Combine(BeutlHomeIsolation.CurrentHome!, "surround.mp4");
            output.SelectedEncoder.Value = encoder;
            var view = new OutputView { DataContext = output };
            var scroll = new ScrollViewer { Content = view };
            var window = new Window { Content = scroll, Width = 640, Height = 640 };
            try
            {
                window.Show();
                HeadlessTestHelpers.Render();
                EnumEditor channels = ChannelEditor(view);
                var audio = view.GetVisualDescendants().OfType<PropertiesEditor>()
                    .Single(p => ReferenceEquals(p.DataContext, output.AudioSettings.Value));
                audio.BringIntoView();
                HeadlessTestHelpers.Render();
                AssertChoices(channels);
                Assert.That(channels.Items![channels.SelectedIndex].Value, Is.EqualTo(6));
                Assert.That(output.Controller.Value!.AudioSettings.Bitrate.Value, Is.EqualTo(384_000));
                HeadlessCapture.SaveIfRequested(window, nameof(AudioChannelsEditorTests), $"ffmpeg-{cultureName}");

                var combo = channels.GetVisualDescendants().OfType<ComboBox>().Single();
                combo.IsDropDownOpen = true;
                HeadlessTestHelpers.Render();
                HeadlessCapture.SaveIfRequested(window, nameof(AudioChannelsEditorTests), $"ffmpeg-{cultureName}-choices");
                combo.IsDropDownOpen = false;
            }
            finally
            {
                window.Close();
                HeadlessTestHelpers.Settle();
                edit.ExtensionProvider.RemoveExtensions(packageId);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static EnumEditor ChannelEditor(Control view)
        => view.GetVisualDescendants().OfType<EnumEditor>().Single(editor => Equals(editor.Header, Strings.Channels));

    private static void AssertChoices(EnumEditor editor)
    {
        Assert.That(editor.Items!.Select(item => item.Value), Is.EqualTo(new[] { 1, 2, 6, 8 }));
        Assert.That(editor.Items!.Select(item => item.DisplayName), Is.EqualTo(new[]
        {
            Strings.SpeakerLayout_Mono, Strings.SpeakerLayout_Stereo,
            Strings.SpeakerLayout_Surround51, Strings.SpeakerLayout_Surround71
        }));
    }
}

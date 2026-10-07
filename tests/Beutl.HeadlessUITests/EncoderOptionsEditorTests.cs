using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Collections;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.PropertyEditors;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class EncoderOptionsEditorTests
{
    [AvaloniaTest]
    public async Task NativeOptionNamesAndConstantsSupportTypedControlsAndCustomNumericValues()
    {
        var settings = new FFmpegVideoEncoderSettings
        {
            Codec = new CodecRecord("libvpx-vp9", "VP9"),
            OutputFile = "out.webm",
            Options = [new("deadline", "1000000"), new("cpu-used", "1")],
        };
        EncoderOptionInfo[] schema =
        [
            new() { Name = "deadline", Kind = EncoderOptionKind.Choice, AllowsNumericValues = true, RequiresInteger = true,
                Minimum = 0, Maximum = 2000000, DefaultValue = "1000000", Choices = [new() { Value = "realtime", NumericValue = 1 }] },
            new() { Name = "cpu-used", Kind = EncoderOptionKind.Integer, Minimum = -8, Maximum = 8, DefaultValue = "1" },
        ];
        string key = EncoderOptionsEditorViewModel.BuildCacheKey(CodecOptionQuery.Create(settings.Codec, settings.OutputFile), settings.Format);
        await FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(key, () => Task.FromResult(new OptionsQueryResult<EncoderOptionInfo>(schema, false)));
        using var model = new EncoderOptionsEditorViewModel(
            new CorePropertyAdapter<CoreList<AdditionalOption>>(FFmpegVideoEncoderSettings.OptionsProperty, settings),
            new FFmpegEncoderSpecializedPropertyExtension());
        var view = new EncoderOptionsEditor(model);
        var window = new Window { Content = view, Width = 320, Height = 480 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(2);
            var deadline = view.GetVisualDescendants().OfType<AutoCompleteStringEditor>().Single(c => c.Name == "EncoderOption_deadline");
            var speed = view.GetVisualDescendants().OfType<NumberEditor<decimal>>().Single(c => c.Name == "EncoderOption_cpu-used");
            deadline.Text = "120000";
            deadline.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            speed.Value = 4;
            speed.RaiseEvent(new PropertyEditorValueChangedEventArgs<decimal>(4, 1, PropertyEditor.ValueConfirmedEvent));
            Assert.That(model.GetValue("deadline"), Is.EqualTo("120000"));
            Assert.That(model.GetWarning(schema[0], "120000"), Is.Null);
            Assert.That(model.GetValue("cpu-used"), Is.EqualTo("4"));
        }
        finally { window.Close(); FFmpegOptionsCaches.ClearAll(); }
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(440, false)]
    [TestCase(440, true)]
    public async Task TypedControlsPreserveInvalidProfilesAndUpdatePersistedOptions(int width, bool dark)
    {
        var settings = new FFmpegVideoEncoderSettings
        {
            Codec = new CodecRecord("libx264", "H.264"),
            OutputFile = "out.mp4",
            Format = FFPixelFormat.YUV420P,
        };
        settings.Options.Single(o => o.Name == "profile").Value = "main10";
        settings.Options.Add(new AdditionalOption("x265-params", "repeat-headers=1"));
        EncoderOptionInfo[] schema =
        [
            new() { Name = "preset", Kind = EncoderOptionKind.Choice, Choices = [new() { Value = "medium" }, new() { Value = "slow" }] },
            new() { Name = "profile", Kind = EncoderOptionKind.Choice, Choices = [new() { Value = "high" }, new() { Value = "high10" }] },
            new() { Name = "crf", Kind = EncoderOptionKind.Number, Minimum = 0, Maximum = 51 },
        ];
        string key = EncoderOptionsEditorViewModel.BuildCacheKey(CodecOptionQuery.Create(settings.Codec, settings.OutputFile), settings.Format);
        await FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(key, () => Task.FromResult(new OptionsQueryResult<EncoderOptionInfo>(schema, false)));
        var extension = new FFmpegEncoderSpecializedPropertyExtension();
        var adapter = new CorePropertyAdapter<CoreList<AdditionalOption>>(FFmpegVideoEncoderSettings.OptionsProperty, settings);
        Assert.That(extension.TryCreateContext([adapter], out var context), Is.True);
        using var model = (EncoderOptionsEditorViewModel)context!;
        Assert.That(extension.TryCreateControl(model, out var control), Is.True);
        Control view = control!;
        var window = new Window
        {
            Content = new ScrollViewer { Content = view },
            Width = width,
            Height = 640,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var profile = view.GetVisualDescendants().OfType<EnumEditor>().Single(c => c.Name == "EncoderOption_profile");
            var quality = view.GetVisualDescendants().OfType<NumberEditor<decimal>>().Single(c => c.Name == "EncoderOption_crf");
            Assert.That(settings.Options.Single(o => o.Name == "profile").Value, Is.EqualTo("main10"));
            Assert.That(profile.Items![profile.SelectedIndex].Value, Is.EqualTo("main10"));
            profile.SelectedIndex = 1;
            quality.Value = 18.5m;
            quality.RaiseEvent(new PropertyEditorValueChangedEventArgs<decimal>(18.5m, 22m, PropertyEditor.ValueConfirmedEvent));
            Assert.That(settings.Options.Single(o => o.Name == "profile").Value, Is.EqualTo("high"));
            Assert.That(settings.Options.Single(o => o.Name == "crf").Value, Is.EqualTo("18.5"));
            Assert.That(settings.Options.Single(o => o.Name == "x265-params").Value, Is.EqualTo("repeat-headers=1"));
            HeadlessTestHelpers.Render(2);
            foreach (Control input in view.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is ComboBox or TextBox or AutoCompleteBox))
                Assert.That(input.Bounds.Width, Is.GreaterThan(80), input.Name);

            settings.Options.Single(o => o.Name == "crf").Value = "999";
            HeadlessTestHelpers.Render(2);
            var invalidQuality = view.GetVisualDescendants().OfType<AutoCompleteStringEditor>().Single(c => c.Name == "EncoderOption_crf");
            Assert.That(invalidQuality.Text, Is.EqualTo("999"));
            Assert.That(settings.Options.Single(o => o.Name == "crf").Value, Is.EqualTo("999"));
            model.SetValue("crf", "18.5");
            HeadlessTestHelpers.Render(2);
            Assert.That(view.GetVisualDescendants().OfType<PropertyEditor>().Where(e => e.IsEffectivelyVisible)
                .All(e => e.Header is not null), Is.True);

            var advanced = view.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "AdvancedEncoderOptions");
            advanced.IsChecked = true;
            HeadlessTestHelpers.Render(2);
            var addName = view.GetVisualDescendants().OfType<AutoCompleteStringEditor>().Single(c => c.Name == "AddEncoderOptionName");
            Assert.That(addName.IsEffectivelyVisible, Is.True);
            Assert.That(view.GetVisualDescendants().OfType<NumericUpDown>(), Is.Empty);

            var disclosure = view.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "EncoderOptionsDisclosure");
            disclosure.IsChecked = false;
            HeadlessTestHelpers.Render(1);
            Assert.That(addName.IsEffectivelyVisible, Is.False);
            disclosure.IsChecked = true;
            advanced.IsChecked = false;
            HeadlessTestHelpers.Render(2);

            string? capture = Environment.GetEnvironmentVariable("BEUTL_ENCODER_OPTIONS_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture) && width == 440)
            {
                using var image = window.CaptureRenderedFrame();
                image?.Save(dark ? Path.ChangeExtension(capture, ".dark.png") : capture, PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); FFmpegOptionsCaches.ClearAll(); }
    }
}

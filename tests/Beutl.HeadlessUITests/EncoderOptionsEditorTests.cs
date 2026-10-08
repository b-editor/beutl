using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Collections;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.Properties;
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
    [TestCase(false, "28", true)]
    [TestCase(true, "custom-crf", true)]
    [TestCase(false, "30", false)]
    public async Task MetadataArrivalPreservesPendingAdvancedText(bool editName, string text, bool edited)
    {
        var settings = new FFmpegVideoEncoderSettings
        {
            Codec = new CodecRecord("libx264", "H.264"),
            OutputFile = "out.mp4",
            Options = [new("crf", "22")],
        };
        EncoderOptionInfo[] schema = [new() { Name = "crf", Kind = EncoderOptionKind.Integer, Minimum = 0, Maximum = 51 }];
        var reply = new TaskCompletionSource<OptionsQueryResult<EncoderOptionInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        string key = EncoderOptionsEditorViewModel.BuildCacheKey(CodecOptionQuery.Create(settings.Codec, settings.OutputFile), settings.Format);
        var query = FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(key, () => reply.Task);
        using var model = new EncoderOptionsEditorViewModel(
            new CorePropertyAdapter<CoreList<AdditionalOption>>(FFmpegVideoEncoderSettings.OptionsProperty, settings),
            new FFmpegEncoderSpecializedPropertyExtension());
        var view = new EncoderOptionsEditor(model);
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.SchemaChanged += () => { if (model.Status == null) applied.TrySetResult(); };
        var window = new Window { Content = view, Width = 440, Height = 480 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(2);
            view.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "AdvancedEncoderOptions").IsChecked = true;
            HeadlessTestHelpers.Render(2);
            var editor = view.GetVisualDescendants().OfType<StringEditor>()
                .Single(c => c.Header == (editName ? Strings.EncoderOptionsOptionName : Strings.EncoderOptionsValue)
                    && c.Name != "AddEncoderOptionName");
            var input = editor.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.That(input.Focus(), Is.True);
            if (edited)
            {
                input.SelectAll();
                window.KeyTextInput(text);
            }
            else settings.Options.Single().Value = text;
            Assert.That(model.GetValue("crf"), Is.EqualTo(edited ? "22" : text));

            reply.SetResult(new OptionsQueryResult<EncoderOptionInfo>(schema, false));
            await query;
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Render(2);

            Assert.That(settings.Options.Single().Name, Is.EqualTo(editName ? text : "crf"));
            Assert.That(settings.Options.Single().Value, Is.EqualTo(editName ? "22" : text));
            Assert.That(view.GetVisualDescendants().OfType<StringEditor>().Any(c => c.Text == text), Is.True);
        }
        finally
        {
            reply.TrySetResult(new OptionsQueryResult<EncoderOptionInfo>(schema, false));
            window.Close();
            FFmpegOptionsCaches.ClearAll();
        }
    }

    [AvaloniaTest]
    [TestCase(10, 100, "2", "0", false, "20")]
    [TestCase(0, 63, "100", "", true, "10")]
    public async Task NumericTypingKeepsFocusUntilTheEditIsConfirmed(
        int minimum, int maximum, string prefix, string suffix, bool backspace, string expected)
    {
        var settings = new FFmpegVideoEncoderSettings
        {
            Codec = new CodecRecord("libvpx-vp9", "VP9"),
            OutputFile = "out.webm",
            Options = [new("crf", "22")],
        };
        EncoderOptionInfo[] schema = [new() { Name = "crf", Kind = EncoderOptionKind.Integer, Minimum = minimum, Maximum = maximum }];
        string key = EncoderOptionsEditorViewModel.BuildCacheKey(CodecOptionQuery.Create(settings.Codec, settings.OutputFile), settings.Format);
        await FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(key, () => Task.FromResult(new OptionsQueryResult<EncoderOptionInfo>(schema, false)));
        using var model = new EncoderOptionsEditorViewModel(
            new CorePropertyAdapter<CoreList<AdditionalOption>>(FFmpegVideoEncoderSettings.OptionsProperty, settings),
            new FFmpegEncoderSpecializedPropertyExtension());
        var view = new EncoderOptionsEditor(model);
        var confirm = new Button { Content = "Confirm" };
        var window = new Window { Content = new StackPanel { Children = { view, confirm } }, Width = 440, Height = 480 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(2);
            var number = view.GetVisualDescendants().OfType<NumberEditor<decimal>>().Single(c => c.Name == "EncoderOption_crf");
            var input = number.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.That(input.Focus(), Is.True);
            input.SelectAll();
            window.KeyTextInput(prefix);
            HeadlessTestHelpers.Render(2);

            Assert.That(input.IsFocused, Is.True);
            Assert.That(view.GetVisualDescendants().OfType<NumberEditor<decimal>>().Single(c => c.Name == number.Name), Is.SameAs(number));
            Assert.That(model.GetValue("crf"), Is.EqualTo("22"));
            if (backspace)
                window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            else
                window.KeyTextInput(suffix);
            HeadlessTestHelpers.Render(2);
            Assert.That(input.IsFocused, Is.True);
            Assert.That(confirm.Focus(), Is.True);
            HeadlessTestHelpers.Render(2);
            Assert.That(model.GetValue("crf"), Is.EqualTo(expected));
        }
        finally { window.Close(); FFmpegOptionsCaches.ClearAll(); }
    }

    [AvaloniaTest]
    [TestCase(false, "28")]
    [TestCase(true, "28")]
    [TestCase(false, "-")]
    [TestCase(true, "-")]
    public async Task SchemaRefreshPreservesPendingNumericText(bool advanced, string text)
    {
        var settings = new FFmpegVideoEncoderSettings
        {
            Codec = new CodecRecord("libx264", "H.264"),
            OutputFile = "out.mp4",
            Format = FFPixelFormat.YUV420P,
            Options = [new("crf", "22")],
        };
        EncoderOptionInfo[] schema = [new() { Name = "crf", Kind = EncoderOptionKind.Integer, Minimum = 0, Maximum = 51 }];
        foreach (int format in new[] { FFPixelFormat.YUV420P, FFPixelFormat.YUV420P10LE })
        {
            string key = EncoderOptionsEditorViewModel.BuildCacheKey(CodecOptionQuery.Create(settings.Codec, settings.OutputFile), format);
            await FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(key, () => Task.FromResult(new OptionsQueryResult<EncoderOptionInfo>(schema, false)));
        }
        using var model = new EncoderOptionsEditorViewModel(
            new CorePropertyAdapter<CoreList<AdditionalOption>>(FFmpegVideoEncoderSettings.OptionsProperty, settings),
            new FFmpegEncoderSpecializedPropertyExtension());
        var view = new EncoderOptionsEditor(model);
        var window = new Window { Content = view, Width = 440, Height = 480 };
        try
        {
            window.Show();
            if (advanced) view.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "AdvancedEncoderOptions").IsChecked = true;
            HeadlessTestHelpers.Render(2);
            var editor = view.GetVisualDescendants().OfType<NumberEditor<decimal>>()
                .Single(c => advanced ? c.Header == Strings.EncoderOptionsValue : c.Name == "EncoderOption_crf");
            var input = editor.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.That(input.Focus(), Is.True);
            input.SelectAll();
            window.KeyTextInput(text);
            Assert.That(model.GetValue("crf"), Is.EqualTo("22"));

            settings.Format = FFPixelFormat.YUV420P10LE;
            HeadlessTestHelpers.Render(2);
            Assert.That(model.GetValue("crf"), Is.EqualTo(text));
        }
        finally { window.Close(); FFmpegOptionsCaches.ClearAll(); }
    }

    [AvaloniaTest]
    public async Task AdvancedRenameRejectsDuplicatesAndRestoresTheNameField()
    {
        var settings = new FFmpegVideoEncoderSettings
        {
            Codec = new CodecRecord("libx264", "H.264"),
            OutputFile = "out.mp4",
            Options = [new("first", "one"), new("second", "two"), new("crf", "100")],
        };
        EncoderOptionInfo[] schema = [new() { Name = "crf", Kind = EncoderOptionKind.Integer, Minimum = 0, Maximum = 63 }];
        string key = EncoderOptionsEditorViewModel.BuildCacheKey(CodecOptionQuery.Create(settings.Codec, settings.OutputFile), settings.Format);
        await FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(key, () => Task.FromResult(new OptionsQueryResult<EncoderOptionInfo>(schema, false)));
        using var model = new EncoderOptionsEditorViewModel(
            new CorePropertyAdapter<CoreList<AdditionalOption>>(FFmpegVideoEncoderSettings.OptionsProperty, settings),
            new FFmpegEncoderSpecializedPropertyExtension());
        var view = new EncoderOptionsEditor(model);
        var window = new Window { Content = view, Width = 440, Height = 640 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(2);
            view.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "AdvancedEncoderOptions").IsChecked = true;
            HeadlessTestHelpers.Render(2);
            string expectedWarning = model.GetWarning(schema[0], "100")!;
            var valueWarning = view.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Text == expectedWarning);
            Assert.That(valueWarning.IsEffectivelyVisible, Is.True);
            var name = view.GetVisualDescendants().OfType<StringEditor>().Single(c => c.Text == "first");
            name.Text = "second";
            name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            HeadlessTestHelpers.Render(2);
            Assert.That(name.Text, Is.EqualTo("first"));
            Assert.That(valueWarning.Text, Is.EqualTo(expectedWarning));
            Assert.That(valueWarning.IsEffectivelyVisible, Is.True);
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>()
                .Any(c => c.IsEffectivelyVisible && c.Text == Strings.EncoderOptionsDuplicate), Is.True);
            Assert.DoesNotThrow(() => settings.Options.ToDictionary(o => o.Name, o => o.Value));
            Assert.That(model.GetValue("first"), Is.EqualTo("one"));
            Assert.That(model.GetValue("second"), Is.EqualTo("two"));
            Assert.That(model.GetValue("crf"), Is.EqualTo("100"));

            name.Text = "unique";
            name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            HeadlessTestHelpers.Render(2);
            Assert.That(model.GetValue("unique"), Is.EqualTo("one"));
            Assert.That(valueWarning.Text, Is.EqualTo(expectedWarning));
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>()
                .Any(c => c.IsEffectivelyVisible && c.Text == Strings.EncoderOptionsDuplicate), Is.False);
        }
        finally { window.Close(); FFmpegOptionsCaches.ClearAll(); }
    }

    [AvaloniaTest]
    [TestCase("en", "Option name cannot be empty.")]
    [TestCase("ja", "オプション名を入力してください。")]
    public async Task EmptyOptionNamesDisplayAnExplanatoryWarning(string culture, string warning)
    {
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        var settings = new FFmpegVideoEncoderSettings
        {
            Codec = new CodecRecord("libx264", "H.264"),
            OutputFile = "out.mp4",
            Options = [new(" ", "value")],
        };
        string key = EncoderOptionsEditorViewModel.BuildCacheKey(CodecOptionQuery.Create(settings.Codec, settings.OutputFile), settings.Format);
        await FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(key, () => Task.FromResult(new OptionsQueryResult<EncoderOptionInfo>([], false)));
        using var model = new EncoderOptionsEditorViewModel(
            new CorePropertyAdapter<CoreList<AdditionalOption>>(FFmpegVideoEncoderSettings.OptionsProperty, settings),
            new FFmpegEncoderSpecializedPropertyExtension());
        var view = new EncoderOptionsEditor(model);
        var window = new Window { Content = view, Width = 440, Height = 480 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(2);
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(c => c.IsEffectivelyVisible && c.Text == warning), Is.True);
        }
        finally
        {
            window.Close();
            FFmpegOptionsCaches.ClearAll();
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
    }

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

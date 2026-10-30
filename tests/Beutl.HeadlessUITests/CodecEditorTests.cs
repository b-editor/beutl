using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.PropertyEditors;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class CodecEditorTests
{
    private const string PinnedItemsKey = CodecPickerViewModel.VideoCodecPinnedItemsKey;

    private static readonly CodecRecord s_libx264 = new("libx264", "libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10");
    private static readonly CodecRecord s_prores = new("prores_ks", "Apple ProRes (iCodec Pro)");
    private static readonly CodecRecord[] s_codecs = [CodecRecord.Default, new("mpeg4", "MPEG-4 part 2"), s_libx264, s_prores];

    private string? _pinnedItems;

    [SetUp]
    public void ClearPinnedCodecs()
    {
        _pinnedItems = Preferences.Default.Get(PinnedItemsKey, "[]");
        Preferences.Default.Set(PinnedItemsKey, "[]");
    }

    [TearDown]
    public void RestorePinnedCodecs()
    {
        Preferences.Default.Set(PinnedItemsKey, _pinnedItems ?? "[]");
    }

    [AvaloniaTest]
    public void The_picker_opens_on_the_codec_in_use()
    {
        using var host = EditorHost.Create(s_libx264);

        host.OpenPicker();

        Assert.That(host.Highlighted, Is.EqualTo(s_libx264));
    }

    // Only the highlighted codec scrolls its long name; the rest stay trimmed until the pointer is over them.
    [AvaloniaTest]
    public void The_highlighted_codec_scrolls_its_long_name()
    {
        using var host = EditorHost.Create(s_libx264);

        host.OpenPicker();
        MarqueeTextBlock[] names = host.Window.GetVisualDescendants().OfType<MarqueeTextBlock>().ToArray();

        Assert.That(names.Where(n => n.IsMarqueeActive).Select(n => n.Text), Is.EqualTo(new[] { s_libx264.LongName }));
    }

    [AvaloniaTest]
    public void Enter_confirms_the_codec_found_by_name()
    {
        using var host = EditorHost.Create(CodecRecord.Default);

        host.OpenPicker();
        host.Window.KeyTextInput("prores");
        HeadlessTestHelpers.Render();
        host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(host.Settings.Codec.Value, Is.EqualTo(s_prores));
            Assert.That(host.Editor.Text, Is.EqualTo(s_prores.LongName));
        });
    }

    [AvaloniaTest]
    public void Escape_keeps_the_codec()
    {
        using var host = EditorHost.Create(s_libx264);

        host.OpenPicker();
        host.Window.KeyTextInput("prores");
        HeadlessTestHelpers.Render();
        host.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(host.Settings.Codec.Value, Is.EqualTo(s_libx264));
            Assert.That(host.Editor.Text, Is.EqualTo(s_libx264.LongName));
        });
    }

    [AvaloniaTest]
    public void Confirming_a_search_without_matches_keeps_the_codec()
    {
        using var host = EditorHost.Create(s_libx264);

        host.OpenPicker();
        host.Window.KeyTextInput("qqqq");
        HeadlessTestHelpers.Render();
        host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        HeadlessTestHelpers.Render();

        Assert.That(host.Settings.Codec.Value, Is.EqualTo(s_libx264));
    }

    private sealed class EditorHost : IDisposable
    {
        private readonly CodecEditorViewModel _model;

        private EditorHost(FFmpegVideoEncoderSettings settings)
        {
            Settings = settings;
            _model = new CodecEditorViewModel(
                new EnginePropertyAdapter<CodecRecord>(settings.Codec, settings),
                new FFmpegEncoderSpecializedPropertyExtension(),
                s_codecs,
                PinnedItemsKey);
            Editor = new DropDownPickerEditor();
            _model.Accept(Editor);
            Window = new Window { Width = 600, Height = 500, Content = Editor };
        }

        public FFmpegVideoEncoderSettings Settings { get; }

        public DropDownPickerEditor Editor { get; }

        public Window Window { get; }

        public CodecRecord? Highlighted => Window.GetVisualDescendants()
            .OfType<LibraryItemPickerFlyoutPresenter>()
            .Single()
            .SelectedItem?.UserData as CodecRecord;

        public static EditorHost Create(CodecRecord codec)
        {
            var host = new EditorHost(new FFmpegVideoEncoderSettings { Codec = { Value = codec } });
            host.Window.Show();
            HeadlessTestHelpers.Render();
            return host;
        }

        public void OpenPicker()
        {
            Editor.GetVisualDescendants().OfType<DropDownButton>()
                .Single(control => control.Name == "PART_InnerButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
        }

        public void Dispose()
        {
            Window.Close();
            HeadlessTestHelpers.Settle();
            _model.Dispose();
        }
    }
}

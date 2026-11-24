using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Audio;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Bitmap = Beutl.Media.Bitmap;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class VideoSourceDeinterlaceMenuTests
{
    [AvaloniaTest]
    [TestCase("en", "Deinterlace", "Off", "Auto", "On", "Decoder does not support deinterlacing", "Checking each frame")]
    [TestCase("ja", "インターレース解除", "無効", "自動", "有効", "デコーダーがインターレース解除に非対応", "フレームごとに自動判定")]
    public async Task SourceMenuAppliesSettingsAndReportsUnsupportedDecoder(
        string culture, string label, string offLabel, string autoLabel, string onLabel, string warning, string autoStatus)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        string path = Path.Combine(Path.GetTempPath(), $"beutl-3316-{Guid.NewGuid():N}", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
        var video = new SourceVideo { Source = { Value = VideoSource.Open(path) } };
        var element = new Element();
        element.Objects.Add(video);
        using var history = new PropertyEditorTestHost.HistoryScope(element);
        var decoders = new DecoderRegistry(new());
        var decoder = new TestDecoder();
        decoders.Register(decoder);
        var (model, control) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(video.Source, video));
        using var vm = (VideoSourceEditorViewModel)model;
        vm.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(HistoryManager), history.History), (typeof(Element), element), (typeof(DecoderRegistry), decoders)));
        var window = CreateWindow(control);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Button button = MenuButton(control);
            FAMenuFlyout flyout = OpenMenu(button);
            var submenu = DeinterlaceSubmenu(flyout);
            var children = submenu.Items.OfType<FAMenuFlyoutSubItem>().ToArray();
            var modes = submenu.Items.OfType<FARadioMenuFlyoutItem>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(submenu.IsEffectivelyVisible, Is.True);
                Assert.That(submenu.Text, Is.EqualTo(label));
                Assert.That(modes.Select(x => x.Text), Is.EqualTo(new[] { offLabel, autoLabel, onLabel }));
                Assert.That(modes.Single(x => x.IsChecked).Tag, Is.EqualTo(DeinterlaceMode.Off));
                Assert.That(children.Select(child => child.IsEnabled), Is.All.False);
                Assert.That(video.GetDisplayProperties(), Does.Not.Contain(video.DeinterlaceMode));
                Assert.That(video.GetDisplayProperties(), Does.Not.Contain(video.DeinterlaceFieldOrder));
                Assert.That(video.GetDisplayProperties(), Does.Not.Contain(video.DeinterlaceMethod));
                Assert.That(video.GetDisplayProperties(), Does.Not.Contain(video.DeinterlaceFrameRateMode));
            });

            Capture(TopLevel.GetTopLevel(submenu)!, $"source-menu-{culture}.png");
            OpenSubmenu(submenu);
            ClickRadio(submenu, DeinterlaceMode.On);
            Assert.That(video.DeinterlaceMode.Value, Is.EqualTo(DeinterlaceMode.On));
            Assert.That(history.History.UndoCount, Is.EqualTo(1));

            OpenMenu(button);
            var status = submenu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "deinterlaceStatusItem");
            await HeadlessTestHelpers.WaitUntilAsync(() => status.IsVisible && status.Text == warning);
            OpenSubmenu(submenu);
            Assert.That(status.IsEffectivelyVisible, Is.True);
            Assert.That(status.IsEnabled, Is.False);
            Assert.That(children.Select(child => child.IsEnabled), Is.All.True);
            Capture(TopLevel.GetTopLevel(status)!, $"deinterlace-unsupported-{culture}.png");
            OpenSubmenu(children[0]);
            Capture(TopLevel.GetTopLevel(children[0].Items.OfType<FARadioMenuFlyoutItem>().First())!,
                $"deinterlace-field-order-{culture}.png");
            ClickRadio(children[0], DeinterlaceFieldOrder.BottomFieldFirst);
            Assert.That(video.DeinterlaceFieldOrder.Value, Is.EqualTo(DeinterlaceFieldOrder.BottomFieldFirst));

            OpenMenu(button);
            OpenSubmenu(submenu);
            OpenSubmenu(children[1]);
            ClickRadio(children[1], DeinterlaceMethod.Yadif);
            OpenMenu(button);
            OpenSubmenu(submenu);
            OpenSubmenu(children[2]);
            ClickRadio(children[2], DeinterlaceFrameRateMode.Field);
            var edited = new VideoDeinterlaceOptions(DeinterlaceMode.On, DeinterlaceFieldOrder.BottomFieldFirst,
                DeinterlaceMethod.Yadif, DeinterlaceFrameRateMode.Field);
            Assert.That(video.GetDeinterlaceOptions(), Is.EqualTo(edited));
            Assert.That(history.History.UndoCount, Is.EqualTo(4));

            var json = CoreSerializer.SerializeToJsonObject(video);
            Assert.That(((SourceVideo)CoreSerializer.DeserializeFromJsonObject(json, typeof(SourceVideo))).GetDeinterlaceOptions(),
                Is.EqualTo(edited));

            history.History.Undo();
            OpenMenu(button);
            Assert.That(children[2].Items.OfType<FARadioMenuFlyoutItem>().Single(item => item.IsChecked).Tag,
                Is.EqualTo(DeinterlaceFrameRateMode.Frame));
            flyout.Hide();
            history.History.Redo();
            Assert.That(video.GetDeinterlaceOptions(), Is.EqualTo(edited));

            element.IsLocked.Value = true;
            OpenMenu(button);
            Assert.That(submenu.IsEnabled, Is.False);
            vm.SetDeinterlaceOption(DeinterlaceMode.Off);
            Assert.That(video.GetDeinterlaceOptions(), Is.EqualTo(edited));
            flyout.Hide();

            element.IsLocked.Value = false;
            decoder.SupportsAuto = true;
            video.DeinterlaceMode.Value = DeinterlaceMode.Auto;
            OpenMenu(button);
            await HeadlessTestHelpers.WaitUntilAsync(() => status.IsVisible && status.Text == autoStatus);
            OpenSubmenu(submenu);
            Capture(TopLevel.GetTopLevel(status)!, $"deinterlace-auto-{culture}.png");
            flyout.Hide();
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = previous;
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [AvaloniaTest]
    public void VideoSourceNodeHidesDeinterlacing()
    {
        var node = new VideoSourceNode();
        var (model, control) = PropertyEditorTestHost.CreateControl(node.Source.Property!, EditorSurface.Node);
        using var vm = (VideoSourceEditorViewModel)model;
        var window = CreateWindow(control);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            FAMenuFlyout flyout = OpenMenu(MenuButton(control));
            Assert.That(DeinterlaceSubmenu(flyout).IsVisible, Is.False);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    internal static Window CreateWindow(Control control) => new()
    {
        Content = new Border { Child = control, Padding = new Avalonia.Thickness(12) },
        Width = 420,
        Height = 360,
    };

    internal static Button MenuButton(Control editor)
        => editor.GetVisualDescendants().OfType<PropertyEditorMenu>().Single()
            .GetVisualDescendants().OfType<Button>().Single();

    internal static FAMenuFlyout OpenMenu(Button button)
    {
        HeadlessTestHelpers.Click(button, move: true, ticks: 3);
        var flyout = (FAMenuFlyout)button.ContextFlyout!;
        Assert.That(flyout.IsOpen, Is.True);
        return flyout;
    }

    internal static FAMenuFlyoutSubItem DeinterlaceSubmenu(FAMenuFlyout flyout)
        => flyout.Items.OfType<FAMenuFlyoutSubItem>().Single(item => item.Name == "deinterlaceMenu");

    internal static void OpenSubmenu(FAMenuFlyoutSubItem submenu)
    {
        Assert.That(submenu.Focus(), Is.True);
        TopLevel.GetTopLevel(submenu)!.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        HeadlessTestHelpers.Render(3);
    }

    internal static void ClickRadio(FAMenuFlyoutSubItem submenu, object value)
    {
        var item = submenu.Items.OfType<FARadioMenuFlyoutItem>().Single(option => Equals(option.Tag, value));
        Assert.That(item.IsEffectivelyVisible, Is.True);
        HeadlessTestHelpers.Click(item, move: true, ticks: 3);
    }

    private static void Capture(TopLevel topLevel, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_VIDEO_DEINTERLACE_CAPTURE") is not { Length: > 0 } directory)
            return;

        Directory.CreateDirectory(directory);
        using var image = topLevel.CaptureRenderedFrame();
        Assert.That(image, Is.Not.Null);
        image!.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

    private sealed class TestDecoder : IDecoderInfo
    {
        public string Name => "Test decoder";
        public bool SupportsAuto { get; set; }
        public DecoderCapabilities Capabilities => SupportsAuto
            ? DecoderCapabilities.DeinterlaceAuto | DecoderCapabilities.DeinterlaceYadif
                | DecoderCapabilities.DeinterlaceFieldOrderOverride | DecoderCapabilities.DeinterlaceDoubleRate
            : DecoderCapabilities.None;
        public IEnumerable<string> VideoExtensions() => [".mp4"];
        public IEnumerable<string> AudioExtensions() => [];
        public MediaReader Open(string file, MediaOptions options)
            => new TestReader(SupportsAuto && options.Deinterlace.Mode == DeinterlaceMode.Auto);
    }

    private sealed class TestReader(bool automatic) : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new("test", 60, new PixelSize(64, 32), new Rational(30, 1))
        {
            DetectedFieldOrder = automatic ? VideoFieldOrder.Progressive : VideoFieldOrder.TopFieldFirst,
            DeinterlaceStatus = automatic ? DeinterlaceApplicationStatus.Applied : DeinterlaceApplicationStatus.Off,
        };

        public override bool HasVideo => true;
        public override bool HasAudio => false;
        public override AudioStreamInfo AudioInfo => throw new NotSupportedException();
        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image) { image = null; return false; }
        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound) { sound = null; return false; }
    }
}

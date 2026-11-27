using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class VideoSourceTransferMenuTests
{
    [AvaloniaTest]
    [TestCase("en", "Transfer Curve", "Auto (Follow Tags)")]
    [TestCase("ja", "伝達曲線", "自動（タグに従う）")]
    public void TheSourceMenuEditsAndTracksHistory(string language, string menuLabel, string autoLabel)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var video = new SourceVideo
        {
            Source = { Value = VideoSource.Open(Path.Combine(Path.GetTempPath(), "sample.mp4")) },
        };
        var element = new Element();
        element.Objects.Add(video);
        using var history = new PropertyEditorTestHost.HistoryScope(element);
        var (model, control) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(video.Source, video));
        using var vm = (VideoSourceEditorViewModel)model;
        vm.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History, element));
        var window = CreateWindow(control);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Button button = MenuButton(control);
            FAMenuFlyout flyout = OpenMenu(button);
            var submenu = TransferSubmenu(flyout);
            var options = submenu.Items.OfType<FARadioMenuFlyoutItem>().ToArray();
            var bt1886 = options.Single(option => option.Tag is VideoTransferInterpretation.Bt1886);

            Assert.Multiple(() =>
            {
                Assert.That(submenu.IsEffectivelyVisible, Is.True);
                Assert.That(submenu.Text, Is.EqualTo(menuLabel));
                Assert.That(options.Select(option => option.Text), Is.EqualTo(
                    new[] { autoLabel, "QuickTime", "BT.1886", "sRGB", GraphicsStrings.VideoTransfer_Bt709Scene }));
                Assert.That(options.Single(option => option.IsChecked).Tag, Is.EqualTo(VideoTransferInterpretation.Auto));
                Assert.That(video.GetDisplayProperties(), Does.Not.Contain(video.TransferInterpretation));
            });

            Capture(TopLevel.GetTopLevel(submenu)!, $"transfer-menu-{language}.png");
            Assert.That(submenu.Focus(), Is.True);
            TopLevel.GetTopLevel(submenu)!.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            HeadlessTestHelpers.Render(3);
            Assert.That(bt1886.IsEffectivelyVisible, Is.True);
            Capture(TopLevel.GetTopLevel(bt1886)!, $"transfer-choices-{language}.png");
            HeadlessTestHelpers.Click(bt1886, move: true, ticks: 3);
            Assert.That(video.TransferInterpretation.Value, Is.EqualTo(VideoTransferInterpretation.Bt1886));
            Assert.That(history.History.UndoCount, Is.EqualTo(1));

            var json = CoreSerializer.SerializeToJsonObject(video);
            Assert.That(((SourceVideo)CoreSerializer.DeserializeFromJsonObject(json, typeof(SourceVideo)))
                .TransferInterpretation.Value, Is.EqualTo(VideoTransferInterpretation.Bt1886));
            Assert.That(json.Remove(nameof(SourceVideo.TransferInterpretation)), Is.True);
            Assert.That(((SourceVideo)CoreSerializer.DeserializeFromJsonObject(json, typeof(SourceVideo)))
                .TransferInterpretation.Value, Is.EqualTo(VideoTransferInterpretation.Auto));

            history.History.Undo();
            OpenMenu(button);
            Assert.That(options.Single(option => option.IsChecked).Tag, Is.EqualTo(VideoTransferInterpretation.Auto));
            flyout.Hide();
            history.History.Redo();
            OpenMenu(button);
            Assert.That(options.Single(option => option.IsChecked).Tag, Is.EqualTo(VideoTransferInterpretation.Bt1886));
            flyout.Hide();

            element.IsLocked.Value = true;
            OpenMenu(button);
            Assert.That(submenu.IsEnabled, Is.False);
            vm.SetTransferInterpretation(VideoTransferInterpretation.Srgb);
            Assert.That(video.TransferInterpretation.Value, Is.EqualTo(VideoTransferInterpretation.Bt1886));
            flyout.Hide();
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [AvaloniaTest]
    public void AVideoSourceNodeDoesNotOfferTheTransferMenu()
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
            Assert.That(TransferSubmenu(flyout).IsVisible, Is.False);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateWindow(Control control) => new()
    {
        Content = new Border { Child = control, Padding = new Avalonia.Thickness(12) },
        Width = 320,
        Height = 360,
    };

    private static Button MenuButton(Control editor)
        => editor.GetVisualDescendants().OfType<PropertyEditorMenu>().Single()
            .GetVisualDescendants().OfType<Button>().Single();

    private static FAMenuFlyout OpenMenu(Button button)
    {
        HeadlessTestHelpers.Click(button, move: true, ticks: 3);
        var menu = (FAMenuFlyout)button.ContextFlyout!;
        Assert.That(menu.IsOpen, Is.True);
        return menu;
    }

    private static FAMenuFlyoutSubItem TransferSubmenu(FAMenuFlyout flyout)
        => flyout.Items.OfType<FAMenuFlyoutSubItem>().Single(item => item.Name == "transferInterpretationMenu");

    private static void Capture(TopLevel topLevel, string filename)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_VIDEO_TRANSFER_CAPTURE") is not { Length: > 0 } directory)
            return;

        Directory.CreateDirectory(directory);
        using var image = topLevel.CaptureRenderedFrame();
        Assert.That(image, Is.Not.Null);
        image!.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
    }
}

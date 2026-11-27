using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class TimelinePasteAttributesTests
{
    [AvaloniaTest]
    public async Task Transform_pastes_from_the_clip_menu_to_all_selected_clips()
    {
        (_, Scene scene, var editor) = await TestShell.OpenNewSceneAsync("paste-attributes", width: 640, height: 360,
            duration: MediaTime.FromSeconds(8));
        var sourceShape = new RectShape { Width = { Value = 150 }, Height = { Value = 120 } };
        var sourceTransform = new TransformGroup();
        sourceTransform.Children.Add(new TranslateTransform(105, 32));
        sourceTransform.Children.Add(new RotationTransform { Rotation = { Value = 28 } });
        sourceShape.Transform.Value = sourceTransform;
        var rectangle = new RectShape
        {
            Width = { Value = 244 },
            Height = { Value = 130 },
            Fill = { Value = new SolidColorBrush(0xFF315DC5) },
            Transform = { Value = new TranslateTransform(-130, -20) }
        };
        var ellipse = new EllipseShape
        {
            Width = { Value = 96 },
            Height = { Value = 110 },
            Fill = { Value = new SolidColorBrush(0xFFFFB84D) },
            Transform = { Value = new TranslateTransform(100, -50) }
        };
        MediaTime start = MediaTime.FromSeconds(3);
        Assert.That((await TestShell.AddElementAsync(editor, () => sourceShape, MediaTime.Zero, MediaTime.FromSeconds(2), 0)).IsSuccess, Is.True);
        Assert.That((await TestShell.AddElementAsync(editor, () => rectangle, start, MediaTime.FromSeconds(4), 1)).IsSuccess, Is.True);
        Assert.That((await TestShell.AddElementAsync(editor, () => ellipse, start, MediaTime.FromSeconds(4), 2)).IsSuccess, Is.True);
        Element source = scene.Children.Single(clip => clip.ZIndex.Value == 0);
        Element first = scene.Children.Single(clip => clip.ZIndex.Value == 2);
        Element second = scene.Children.Single(clip => clip.ZIndex.Value == 1);
        source.Name.Value = "Source style";
        first.Name.Value = "Ellipse";
        second.Name.Value = "Rectangle";
        source.AccentColor.Value = Color.FromArgb(255, 86, 136, 128);
        first.AccentColor.Value = Color.FromArgb(255, 225, 152, 54);
        second.AccentColor.Value = Color.FromArgb(255, 74, 111, 195);
        editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(4));
        editor.HistoryManager.Commit();
        int before = editor.HistoryManager.UndoCount;

        bool overlayPopups = TestAppBuilder.HeadlessOptions.OverlayPopups;
        var previousClipboard = ClipboardHelper.OverrideClipboard;
        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 1250, Height = 840, RequestedThemeVariant = ThemeVariant.Light };
        try
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = true;
            if (HeadlessCapture.IsRequested) window.SetRenderScaling(2);
            window.Show();
            HeadlessTestHelpers.Render(5);
            ClipboardHelper.OverrideClipboard = window.Clipboard;

            var selection = editor.GetRequiredService<IEditorSelection>();
            selection.Select(source);
            await editor.FindToolTab<TimelineTabViewModel>()!.GetViewModelFor(source)!.Copy.ExecuteAsync();
            selection.Select(first);
            selection.SelectedElements.Add(second);
            HeadlessTestHelpers.Render(5);
            await CaptureAsync(window, "before");

            var target = view.GetVisualDescendants().OfType<ElementView>()
                .Single(clip => clip.DataContext is ElementViewModel model && model.Model == first);
            var border = target.FindControl<Border>("border")!;
            HeadlessTestHelpers.Click(border, new Avalonia.Point(40, border.Bounds.Height / 2), MouseButton.Right, move: true);
            Assert.That(((FAMenuFlyout)border.ContextFlyout!).IsOpen, Is.True);
            var submenu = target.FindControl<FAMenuFlyoutSubItem>("pasteAttributesMenu")!;
            Assert.That(submenu.Text, Is.EqualTo(Strings.PasteAttributes));
            Assert.That(submenu.Focus(), Is.True);
            TopLevel topLevel = TopLevel.GetTopLevel(submenu)!;
            topLevel.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            topLevel.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            HeadlessTestHelpers.Render(5);

            FAMenuFlyoutItem[] items = submenu.Items.OfType<FAMenuFlyoutItem>().ToArray();
            Assert.That(items.Select(item => item.Text), Is.EqualTo(new[]
            {
                Strings.PasteAttributes_All, GraphicsStrings.Transform, Strings.PasteAttributes_VideoEffects,
                Strings.PasteAttributes_AudioEffects, Strings.PasteAttributes_Animations
            }));
            Assert.That(items[3].IsEffectivelyEnabled, Is.False);
            FAMenuFlyoutItem transform = items[1];
            Assert.That(transform.Bounds.Height, Is.GreaterThan(0));
            await CaptureAsync(window, "context-menu");

            HeadlessTestHelpers.Click(transform, move: true);
            await HeadlessTestHelpers.WaitUntilAsync(() => ellipse.Transform.Value is TransformGroup && rectangle.Transform.Value is TransformGroup);
            Assert.That((ellipse.Transform.Value as TransformGroup)?.Children[0], Is.TypeOf<TranslateTransform>());
            Assert.That((rectangle.Transform.Value as TransformGroup)?.Children[1], Is.TypeOf<RotationTransform>());
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
            Assert.That(selection.SelectedElements, Is.EquivalentTo(new[] { first, second }));
            window.MouseMove(new Avalonia.Point(window.ClientSize.Width / 2, window.ClientSize.Height - 30));
            await CaptureAsync(window, "after");
        }
        finally
        {
            ClipboardHelper.OverrideClipboard = previousClipboard;
            view.DataContext = null;
            window.Close();
            TestAppBuilder.HeadlessOptions.OverlayPopups = overlayPopups;
            HeadlessTestHelpers.Settle();
        }
    }

    private static async Task CaptureAsync(Window window, string name)
    {
        if (!HeadlessCapture.IsRequested) return;
        await RenderThread.Dispatcher.InvokeAsync(static () => { });
        HeadlessTestHelpers.Render(5);
        HeadlessCapture.SaveIfRequested(window, nameof(TimelinePasteAttributesTests), name);
    }
}

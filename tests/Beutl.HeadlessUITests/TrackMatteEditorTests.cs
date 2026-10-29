using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using MatteMode = Beutl.Graphics.MatteMode;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TrackMatteEditorTests
{
    [AvaloniaTest]
    [TestCase(320, "ja", false)]
    [TestCase(640, "en", false)]
    [TestCase(320, "en", true)]
    [TestCase(640, "ja", true)]
    public async Task ContextMenu_ChangesTheClickedClip_AndReflectsUndo(int width, string language, bool light)
    {
        CultureInfo oldCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            (EditViewModel editor, TimelineTabViewModel timeline, Element source, Element target) = await Open();
            var view = new TimelineTabView { DataContext = timeline };
            var window = new Window
            {
                Content = view,
                Width = width,
                Height = 550,
                RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
            };
            try
            {
                window.Show();
                HeadlessTestHelpers.Render(5);
                ElementView targetView = FindElement(view, target);
                FAMenuFlyoutSubItem menu = OpenMatteMenu(targetView);
                FAMenuFlyoutSubItem sources = menu.Items.OfType<FAMenuFlyoutSubItem>().Single();
                OpenSubMenu(sources);
                FAToggleMenuFlyoutItem[] choices = sources.Items.OfType<FAToggleMenuFlyoutItem>().ToArray();
                Assert.That(choices, Has.Length.EqualTo(2));
                Assert.That(choices[0].IsChecked, Is.True);
                string capture = $"{language}-{width}-{(light ? "light" : "dark")}";
                HeadlessCapture.SaveIfRequested(choices[0], "track-matte", $"sources-{capture}");
                int before = editor.HistoryManager.UndoCount;
                HeadlessTestHelpers.Click(choices[1], move: true);
                Assert.That(Flyout(targetView).IsOpen, Is.False);
                Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.Alpha));
                Assert.That(target.MatteSourceZIndex.Value, Is.EqualTo(0));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));

                menu = OpenMatteMenu(targetView);
                Assert.That(Item(menu, Strings.MatteAlpha).IsChecked, Is.True);
                HeadlessTestHelpers.Click(Item(menu, Strings.MatteInvertedLuma), move: true);
                Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.InvertedLuma));
                Assert.That(editor.HistoryManager.Undo(), Is.True);
                menu = OpenMatteMenu(targetView);
                Assert.That(Item(menu, Strings.MatteAlpha).IsChecked, Is.True);
                Assert.That(Item(menu, Strings.MatteInvertedLuma).IsChecked, Is.False);
                Flyout(targetView).Hide();
                Assert.That(editor.HistoryManager.Redo(), Is.True);
                menu = OpenMatteMenu(targetView);
                Assert.That(Item(menu, Strings.MatteInvertedLuma).IsChecked, Is.True);
                HeadlessCapture.SaveIfRequested(Item(menu, Strings.MatteAlpha), "track-matte", $"menu-{capture}");
                Flyout(targetView).Hide();
                Assert.That(timeline.GetViewModelFor(target)!.MatteIndicator.Value, Is.EqualTo("↑ 0"));

                ElementView sourceView = FindElement(view, source);
                menu = OpenMatteMenu(sourceView);
                Assert.That(Item(menu, Strings.IsVisibleInOutput).IsChecked, Is.True);
                HeadlessTestHelpers.Click(Item(menu, Strings.IsVisibleInOutput), move: true);
                Assert.That(source.IsVisibleInOutput.Value, Is.False);
                Assert.That(target.IsVisibleInOutput.Value, Is.True);
                Assert.That(editor.HistoryManager.Undo(), Is.True);
                menu = OpenMatteMenu(sourceView);
                Assert.That(Item(menu, Strings.IsVisibleInOutput).IsChecked, Is.True);
                Flyout(sourceView).Hide();
                source.IsLocked.Value = true;
                HeadlessTestHelpers.Render();
                Flyout(sourceView).ShowAt(sourceView);
                HeadlessTestHelpers.Render();
                Assert.That(menu.IsEffectivelyEnabled, Is.False);
                timeline.GetViewModelFor(source)!.ToggleOutputVisibility();
                Assert.That(source.IsVisibleInOutput.Value, Is.True, "the service enforces the lock too");
                Flyout(sourceView).Hide();
            }
            finally { TimelineTestHost.Close(view, window); }
        }
        finally { CultureInfo.CurrentUICulture = oldCulture; }
    }

    [AvaloniaTest]
    public async Task ContextMenu_RefreshesSourceLayers_AndCanDisableAndClearTheMatte()
    {
        (EditViewModel editor, TimelineTabViewModel timeline, Element source, Element target) = await Open();
        editor.GetRequiredService<IElementAttributeService>().SetTrackMatte(target, 0, MatteMode.Luma);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 550 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            ElementView element = FindElement(view, target);
            FAMenuFlyoutSubItem menu = OpenMatteMenu(element);
            Assert.That(Item(menu, Strings.MatteLuma).IsChecked, Is.True);
            HeadlessTestHelpers.Click(Item(menu, StatusStrings.None), move: true);
            Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.None));
            Assert.That(target.MatteSourceZIndex.Value, Is.EqualTo(0), "disabling preserves the source");

            editor.GetRequiredService<ILayerAttributeService>()
                .SetColor(editor.Scene, 0, Beutl.Media.Colors.White, "Text mask");
            editor.GetRequiredService<ILayerMoveService>().ApplyMove(editor.Scene, 0, 3, [source]);
            menu = OpenMatteMenu(element);
            FAMenuFlyoutSubItem sources = menu.Items.OfType<FAMenuFlyoutSubItem>().Single();
            OpenSubMenu(sources);
            FAToggleMenuFlyoutItem selected = Item(sources, "3: Text mask");
            Assert.That(selected.IsChecked, Is.True, "the saved source remains available below the target");
            HeadlessTestHelpers.Click(selected, move: true);
            Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.Alpha));
            menu = OpenMatteMenu(element);
            HeadlessTestHelpers.Click(Item(menu, Strings.MatteInvertedAlpha), move: true);
            Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.InvertedAlpha));

            menu = OpenMatteMenu(element);
            sources = menu.Items.OfType<FAMenuFlyoutSubItem>().Single();
            OpenSubMenu(sources);
            int before = editor.HistoryManager.UndoCount;
            HeadlessTestHelpers.Click(Item(sources, StatusStrings.None), move: true);
            Assert.That(target.MatteSourceZIndex.Value, Is.Null);
            Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.None));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
            Assert.That(editor.HistoryManager.Undo(), Is.True);
            Assert.That(target.MatteSourceZIndex.Value, Is.EqualTo(3));
            Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.InvertedAlpha));
        }
        finally { TimelineTestHost.Close(view, window); }
    }

    [AvaloniaTest]
    public async Task Timeline_ShowsTheReferencedLayerAndFollowsUndo()
    {
        (EditViewModel editor, TimelineTabViewModel timeline, Element source, Element target) = await Open();
        editor.GetRequiredService<IElementAttributeService>().SetTrackMatte(target, 0, MatteMode.Alpha);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 350 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            ElementView elementView = view.GetVisualDescendants().OfType<ElementView>()
                .Single(item => ((ElementViewModel)item.DataContext!).Model == target);
            TextBlock arrow = elementView.FindControl<TextBlock>("matteIndicator")!;
            Assert.That(arrow.IsVisible, Is.True);
            Assert.That(arrow.Text, Is.EqualTo("↑ 0"));
            HeadlessCapture.SaveIfRequested(view, "track-matte", "timeline");
            editor.GetRequiredService<ILayerMoveService>().ApplyMove(editor.Scene, 0, 1, [source]);
            HeadlessTestHelpers.Render();
            Assert.That(arrow.Text, Is.EqualTo("↑ 1"));
            Assert.That(editor.HistoryManager.Undo(), Is.True);
            HeadlessTestHelpers.Render();
            Assert.That(arrow.Text, Is.EqualTo("↑ 0"));
        }
        finally { TimelineTestHost.Close(view, window); }
    }

    [AvaloniaTest]
    public async Task ContextMenu_OffersUnselectedLayersAboveAndBelowTheTarget()
    {
        (EditViewModel editor, TimelineTabViewModel timeline, _, Element target) = await Open();
        var below = new Element { ZIndex = { Value = 3 }, Length = { Value = MediaTime.FromSeconds(4) } };
        below.Objects.Add(new RectShape());
        editor.Scene.Children.Add(below);
        editor.HistoryManager.Commit();
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 640, Height = 550 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            FAMenuFlyoutSubItem menu = OpenMatteMenu(FindElement(view, target));
            FAMenuFlyoutSubItem sources = menu.Items.OfType<FAMenuFlyoutSubItem>().Single();
            OpenSubMenu(sources);
            Assert.That(sources.Items.OfType<FAToggleMenuFlyoutItem>().Select(item => item.Text),
                Is.EqualTo(new[] { StatusStrings.None, "0", "3" }));
            HeadlessCapture.SaveIfRequested(Item(sources, "3"), "track-matte", "sources-both-sides");
            HeadlessTestHelpers.Click(Item(sources, "3"), move: true);
            Assert.That(target.MatteSourceZIndex.Value, Is.EqualTo(3));
            Assert.That(timeline.GetViewModelFor(target)!.MatteIndicator.Value, Is.EqualTo("↓ 3"));
            Assert.That(editor.HistoryManager.Undo(), Is.True);
            Assert.That(target.MatteSourceZIndex.Value, Is.Null);
        }
        finally { TimelineTestHost.Close(view, window); }
    }

    [AvaloniaTest]
    public async Task EditingAHiddenSource_InvalidatesItsCachedOutput_OnEditAndUndo()
    {
        (EditViewModel editor, _, Element source, Element target) = await Open();
        var attributes = editor.GetRequiredService<IElementAttributeService>();
        attributes.SetTrackMatte(target, 0, MatteMode.Alpha);
        attributes.SetVisibleInOutput(source, false);
        FrameCacheManager cache = editor.FrameCacheManager.Value;
        bool enabled = cache.IsEnabled;
        cache.IsEnabled = true;
        int frame = (int)MediaTime.FromSeconds(1).ToFrame(editor.Player.GetFrameRate(), MidpointRounding.AwayFromZero);
        int outside = frame * 10;
        try
        {
            await HeadlessTestHelpers.WaitUntilAsync(() => !Contains(frame), TimeSpan.FromSeconds(20));
            Add(frame);
            Add(outside);
            ((RectShape)source.Objects[0]).Width.Value = 123;
            editor.HistoryManager.Commit("Edit matte source");
            await HeadlessTestHelpers.WaitUntilAsync(() => !Contains(frame), TimeSpan.FromSeconds(20));
            Assert.That(Contains(outside), Is.True);
            Add(frame);
            Assert.That(editor.HistoryManager.Undo(), Is.True);
            await HeadlessTestHelpers.WaitUntilAsync(() => !Contains(frame), TimeSpan.FromSeconds(20));
        }
        finally { cache.Clear(); cache.IsEnabled = enabled; }

        void Add(int key)
        {
            using Ref<Beutl.Media.Bitmap> bitmap = Ref<Beutl.Media.Bitmap>.Create(new Beutl.Media.Bitmap(16, 16));
            cache.Add(key, bitmap);
        }
        bool Contains(int key)
        {
            if (!cache.TryGet(key, out Ref<Beutl.Media.Bitmap>? bitmap)) return false;
            bitmap.Dispose();
            return true;
        }
    }

    private static ElementView FindElement(TimelineTabView view, Element element)
        => view.GetVisualDescendants().OfType<ElementView>()
            .Single(item => ((ElementViewModel)item.DataContext!).Model == element);

    private static FAMenuFlyout Flyout(ElementView element)
        => (FAMenuFlyout)element.FindControl<Border>("border")!.ContextFlyout!;

    private static FAToggleMenuFlyoutItem Item(FAMenuFlyoutSubItem menu, string text)
        => menu.Items.OfType<FAToggleMenuFlyoutItem>().Single(item => item.Text == text);

    private static FAMenuFlyoutSubItem OpenMatteMenu(ElementView element)
    {
        HeadlessTestHelpers.Render();
        // Click near the start so the element is reachable even in a narrow timeline viewport.
        HeadlessTestHelpers.Click(element, new Point(24, element.Bounds.Height / 2), MouseButton.Right, move: true);
        Assert.That(Flyout(element).IsOpen, Is.True);
        FAMenuFlyoutSubItem menu = element.FindControl<FAMenuFlyoutSubItem>("trackMatteMenu")!;
        OpenSubMenu(menu);
        return menu;
    }

    private static void OpenSubMenu(FAMenuFlyoutSubItem menu)
    {
        Assert.That(menu.Focus(), Is.True);
        TopLevel popup = TopLevel.GetTopLevel(menu)!;
        popup.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
        popup.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
        HeadlessTestHelpers.Render(3);
        Assert.That(menu.Items.OfType<Control>().First().Bounds.Height, Is.GreaterThan(0));
    }

    private static async Task<(EditViewModel, TimelineTabViewModel, Element, Element)> Open()
    {
        (EditViewModel editor, TimelineTabViewModel timeline) = await TimelineTestHost.OpenTimelineAsync(
            $"track-matte-{Guid.NewGuid():N}",
            [
                new(MediaTime.Zero, MediaTime.FromSeconds(4), 0, new ElementSource.EngineObject(() => new RectShape())),
                new(MediaTime.Zero, MediaTime.FromSeconds(4), 2, new ElementSource.EngineObject(() => new RectShape())),
            ]);
        Element source = editor.Scene.Children.Single(element => element.ZIndex.Value == 0);
        Element target = editor.Scene.Children.Single(element => element.ZIndex.Value == 2);
        editor.HistoryManager.Commit();
        return (editor, timeline, source, target);
    }
}

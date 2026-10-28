using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
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
    public async Task Settings_WorkAtNarrowWidths_AndUndoAndRetarget(int width, string language, bool light)
    {
        CultureInfo oldCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            (EditViewModel editor, TimelineTabViewModel timeline, Element source, Element target) = await Open();
            using var model = new ElementPropertyTabViewModel(editor);
            model.Element.Value = target;
            var view = new ElementPropertyTabView { DataContext = model };
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
                HeadlessTestHelpers.Render();
                var sourceBox = view.FindControl<ComboBox>("matteSource")!;
                var modeBox = view.FindControl<ComboBox>("matteMode")!;
                var visible = view.FindControl<CheckBox>("matteOutputVisibility")!;
                TrackMatteSettingsViewModel settings = model.TrackMatte.Value!;
                Assert.That(settings.Sources.Select(item => item.ZIndex), Is.EqualTo(new int?[] { null, 0 }));
                int before = editor.HistoryManager.UndoCount;
                sourceBox.SelectedItem = settings.Sources.Single(item => item.ZIndex == 0);
                HeadlessTestHelpers.Settle();
                Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.Alpha));
                Assert.That(target.MatteSourceZIndex.Value, Is.EqualTo(0));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
                modeBox.SelectedItem = settings.Modes.Single(item => item.Mode == MatteMode.InvertedLuma);
                HeadlessTestHelpers.Settle();
                Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.InvertedLuma));
                Assert.That(editor.HistoryManager.Undo(), Is.True);
                Assert.That(settings.SelectedMode.Value!.Mode, Is.EqualTo(MatteMode.Alpha));
                Assert.That(editor.HistoryManager.Redo(), Is.True);
                HeadlessTestHelpers.Render();
                foreach (Control control in new Control[] { sourceBox, modeBox, visible })
                {
                    Point position = control.TranslatePoint(default, view)!.Value;
                    Assert.That(position.X, Is.GreaterThanOrEqualTo(0));
                    Assert.That(position.X + control.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width));
                }
                HeadlessCapture.SaveIfRequested(view, "track-matte", $"settings-{language}-{width}-{(light ? "light" : "dark")}");
                Assert.That(timeline.GetViewModelFor(target)!.MatteIndicator.Value, Is.EqualTo("↑ 0"));

                model.Element.Value = source;
                HeadlessTestHelpers.Render();
                Point point = HeadlessTestHelpers.Center(visible, window);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                HeadlessTestHelpers.Settle();
                Assert.That(source.IsVisibleInOutput.Value, Is.False);
                Assert.That(target.IsVisibleInOutput.Value, Is.True);
                Assert.That(editor.HistoryManager.Undo(), Is.True);
                Assert.That(source.IsVisibleInOutput.Value, Is.True);
                source.IsLocked.Value = true;
                HeadlessTestHelpers.Render();
                Assert.That(sourceBox.IsEffectivelyEnabled, Is.False);
                settings = model.TrackMatte.Value!;
                settings.ShowInOutput.Value = false;
                Assert.That(source.IsVisibleInOutput.Value, Is.True, "the service enforces the lock too");
            }
            finally { window.Close(); }
        }
        finally { CultureInfo.CurrentUICulture = oldCulture; }
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

using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Pages.SettingsPages;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class TimelineLayerHeightTests
{
    [AvaloniaTest]
    public async Task Changing_presets_updates_clip_geometry_headers_and_hit_testing()
    {
        await TestReset.ResetShellAsync();
        var size = TestShell.Configuration.EditorConfig.TimelineLayerSize;
        TimelineLayerSize previous = size.Value;
        try
        {
            size.Value = TimelineLayerSize.Small;
            using var host = await TimelineTestHost.CreateAsync(
                "timeline-row-height", MediaTime.FromSeconds(10), [Clip(1)],
                theme: ThemeVariant.Dark);
            TimelineTabViewModel timeline = host.Model;

            foreach (TimelineLayerSize preset in new[]
                     { TimelineLayerSize.Small, TimelineLayerSize.Medium, TimelineLayerSize.Large, TimelineLayerSize.Small })
            {
                size.Value = preset;
                HeadlessTestHelpers.Render(3);
                double rowHeight = (int)preset;
                ElementView clip = FindClip(host, 1);
                LayerHeader header = FindHeader(host, 1);
                Assert.Multiple(() =>
                {
                    Assert.That(clip.Bounds.Height, Is.EqualTo(rowHeight));
                    Assert.That(clip.Bounds.Y, Is.EqualTo(rowHeight));
                    Assert.That(header.Bounds.Height, Is.EqualTo(rowHeight));
                    Assert.That(timeline.LayerHeaders[1].Height.Value, Is.EqualTo(rowHeight));
                    Assert.That(host.Timeline.Children.OfType<TimelineBackground>().Single().ItemHeight, Is.EqualTo(rowHeight));
                    Assert.That(timeline.ToLayerNumber(1.75 * rowHeight), Is.EqualTo(1));
                    // The clip's top is still on row 1, while its centre is on row 2.
                    Assert.That(timeline.ToLayerNumber(new Thickness(0, 1.625 * rowHeight, 0, 0)), Is.EqualTo(2));
                });

                var headerGrid = (Grid)header.Content!;
                Control[] buttons = headerGrid.Children
                    .Where(child => child is ToggleButton or SimpleColorPickerButton).ToArray();
                Assert.That(buttons, Has.Length.EqualTo(5));
                Assert.Multiple(() =>
                {
                    Assert.That(buttons.Select(button => button.Bounds.Height), Is.All.EqualTo(25));
                    Assert.That(buttons.Select(button => button.Bounds.Center.Y), Is.All.EqualTo(rowHeight / 2).Within(0.5));
                });

                int count = timeline.LayerHeaders.Count;
                int overflowRow = timeline.ToLayerNumber((count + 1.75) * rowHeight);
                Assert.Multiple(() =>
                {
                    Assert.That(overflowRow, Is.EqualTo(count + 2));
                    Assert.That(timeline.LayerHeaders, Has.Count.EqualTo(count + 3));
                    Assert.That(timeline.LayerHeaders[overflowRow].Height.Value, Is.EqualTo(rowHeight));
                });
                HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineLayerHeightTests), preset.ToString());
            }
        }
        finally
        {
            size.Value = previous;
        }
    }

    [AvaloniaTest]
    public async Task The_label_and_rename_editor_stay_centered_on_the_same_row()
    {
        await TestReset.ResetShellAsync();
        var size = TestShell.Configuration.EditorConfig.TimelineLayerSize;
        TimelineLayerSize previous = size.Value;
        try
        {
            size.Value = TimelineLayerSize.Small;
            using var host = await TimelineTestHost.CreateAsync(
                "timeline-centered-label", MediaTime.FromSeconds(10), [Clip(0)]);
            ElementView clip = FindClip(host, 0);
            var model = (ElementViewModel)clip.DataContext!;
            // A matte adds a leading column that the rename text must also clear.
            model.SetMatteSource(MatteSourceKind.Below);
            HeadlessTestHelpers.Render(3);
            var label = clip.FindControl<TextBlock>("textBlock")!;
            var row = (Grid)label.Parent!;
            var editor = clip.FindControl<TextBox>("textBox")!;
            Assert.That(clip.FindControl<TextBlock>("matteIndicator")!.IsVisible, Is.True);

            foreach (TimelineLayerSize preset in Enum.GetValues<TimelineLayerSize>())
            {
                size.Value = preset;
                HeadlessTestHelpers.Render(3);
                double rowHeight = (int)preset;
                Point labelPosition = label.TranslatePoint(default, clip)!.Value;
                double rowCenterY = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), clip)!.Value.Y;
                double rowTop = row.TranslatePoint(default, clip)!.Value.Y;
                Assert.Multiple(() =>
                {
                    Assert.That(row.Bounds.Height, Is.EqualTo(21));
                    Assert.That(rowCenterY, Is.EqualTo(rowHeight / 2).Within(0.5));
                });

                model.RenameRequested();
                HeadlessTestHelpers.Render(3);
                Assert.That(editor.IsFocused, Is.True);
                var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().Single();
                Point editPosition = presenter.TranslatePoint(default, clip)!.Value;
                Assert.Multiple(() =>
                {
                    Assert.That(editor.Bounds.Height, Is.EqualTo(21));
                    Assert.That(editor.TranslatePoint(default, clip)!.Value.Y, Is.EqualTo(rowTop).Within(0.5));
                    Assert.That(editPosition.X, Is.EqualTo(labelPosition.X).Within(0.5));
                    Assert.That(editPosition.Y, Is.EqualTo(labelPosition.Y).Within(0.5));
                });
                if (preset == TimelineLayerSize.Large)
                    HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineLayerHeightTests), "large-rename");
                clip.Focus();
                HeadlessTestHelpers.Settle();
            }
        }
        finally
        {
            size.Value = previous;
        }
    }

    [AvaloniaTest]
    public async Task Open_inline_rows_resize_and_keep_the_rows_below_in_sync()
    {
        await TestReset.ResetShellAsync();
        var size = TestShell.Configuration.EditorConfig.TimelineLayerSize;
        TimelineLayerSize previous = size.Value;
        try
        {
            size.Value = TimelineLayerSize.Small;
            var shape = new RectShape();
            using var host = await TimelineTestHost.CreateAsync(
                "timeline-inline-row-height", MediaTime.FromSeconds(10),
                [
                    new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(2), 1,
                        new ElementSource.EngineObject(() => shape)),
                    Clip(2)
                ], theme: ThemeVariant.Dark);
            Element animated = host.Scene.Children.Single(clip => clip.ZIndex.Value == 1);
            foreach (AnimatableProperty<float> property in
                     new[] { (AnimatableProperty<float>)shape.Width, (AnimatableProperty<float>)shape.Opacity })
            {
                var animation = new KeyFrameAnimation<float>();
                animation.KeyFrames.Add(new KeyFrame<float>
                { KeyTime = { Value = MediaTime.FromSeconds(0.5) }, Value = { Value = 100 } });
                property.SetKeyFrameAnimation(animation);
                host.Model.AttachInline(new AnimatablePropertyAdapter<float>(property, shape), animated);
            }

            foreach (TimelineLayerSize preset in new[] { TimelineLayerSize.Medium, TimelineLayerSize.Small, TimelineLayerSize.Large })
            {
                size.Value = preset;
                HeadlessTestHelpers.Render(3);
                double rowHeight = (int)preset;
                LayerHeaderViewModel header = host.Model.LayerHeaders[1];
                Assert.Multiple(() =>
                {
                    Assert.That(header.Inlines, Has.Count.EqualTo(2));
                    Assert.That(header.Height.Value, Is.EqualTo(3 * rowHeight));
                    Assert.That(header.Inlines.Select(inline => inline.Margin.Value.Top),
                        Is.EqualTo(new[] { 2 * rowHeight, 3 * rowHeight }));
                    Assert.That(FindClip(host, 2).Bounds.Y, Is.EqualTo(4 * rowHeight));
                });

                InlineAnimationLayer[] layers = host.Timeline.Children.OfType<InlineAnimationLayer>().ToArray();
                InlineAnimationLayerHeader[] inlineHeaders = FindHeader(host, 1).GetVisualDescendants()
                    .OfType<InlineAnimationLayerHeader>().ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(layers.Select(layer => layer.Bounds.Height), Is.EqualTo(new[] { rowHeight, rowHeight }));
                    Assert.That(layers.Select(layer => layer.Bounds.Y), Is.EqualTo(new[] { 2 * rowHeight, 3 * rowHeight }));
                    Assert.That(inlineHeaders.Select(layer => layer.Bounds.Height), Is.EqualTo(new[] { rowHeight, rowHeight }));
                });
                var inlineGrid = (Grid)inlineHeaders[0].Content!;
                Button[] buttons = inlineGrid.Children.OfType<Button>().ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(buttons.Select(button => button.Bounds.Height), Is.All.EqualTo(25));
                    Assert.That(buttons.Select(button => button.Bounds.Center.Y), Is.All.EqualTo(rowHeight / 2).Within(0.5));
                });
                if (preset == TimelineLayerSize.Large)
                    HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineLayerHeightTests), "large-inline");
            }

            host.Model.DetachInline(host.Model.Inlines[0]);
            Assert.That(host.Model.Inlines.Single().Margin.Value.Top, Is.EqualTo(128));
            Assert.That(host.Model.LayerHeaders[1].Height.Value, Is.EqualTo(128));

            // Changing row height while an inline settle animation is awaiting must commit the new row height.
            size.Value = TimelineLayerSize.Small;
            HeadlessTestHelpers.Render(3);
            InlineAnimationLayerViewModel remaining = host.Model.Inlines.Single();
            var animationStarted = new TaskCompletionSource();
            var finishAnimation = new TaskCompletionSource();
            remaining.AnimationRequested = async (_, _, _) =>
            {
                animationStarted.TrySetResult();
                await finishAnimation.Task;
            };
            remaining.AnimationRequest(remaining.PrepareAnimation(), new Thickness(0, 25, 0, 0), default);
            await animationStarted.Task;
            size.Value = TimelineLayerSize.Large;
            finishAnimation.SetResult();
            await HeadlessTestHelpers.WaitUntilAsync(() => remaining.Margin.Value.Top == 128);
        }
        finally
        {
            size.Value = previous;
        }
    }

    [AvaloniaTest]
    public async Task Portal_scope_and_compact_clip_geometry_follow_row_height()
    {
        await TestReset.ResetShellAsync();
        var size = TestShell.Configuration.EditorConfig.TimelineLayerSize;
        TimelineLayerSize previous = size.Value;
        try
        {
            size.Value = TimelineLayerSize.Small;
            using var host = await TimelineTestHost.CreateAsync(
                "timeline-compact-row-height", MediaTime.FromSeconds(10),
                [
                    Clip(0, start: 1, length: 0.05),
                    new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(2), 1,
                        new ElementSource.EngineObject(() => new PortalObject { Count = { Value = 2 } })),
                    Clip(3, start: 2, length: 0.05)
                ]);
            TimelineTabViewModel timeline = host.Model;
            Element compact = host.Scene.Children.Single(clip => clip.ZIndex.Value == 0);
            Element portal = host.Scene.Children.Single(clip => clip.ZIndex.Value == 1);
            Element unrealized = host.Scene.Children.Single(clip => clip.ZIndex.Value == 3);
            var scope = timeline.GetViewModelFor(portal)!.Scope;
            // This narrow clip takes the ViewportElement path with a view model, and stays on row 0.
            timeline.GetViewModelFor(compact);
            Assert.That(timeline.FindViewModelFor(unrealized), Is.Null);
            Assert.That(scope.Height.Value, Is.EqualTo(75));
            Assert.That(timeline.GetElementBounds(unrealized).Height, Is.EqualTo(25));

            size.Value = TimelineLayerSize.Large;
            HeadlessTestHelpers.Render(3);
            Rect compactBounds = timeline.GetElementBounds(compact);
            Rect unrealizedBounds = timeline.GetElementBounds(unrealized);
            Assert.Multiple(() =>
            {
                Assert.That(scope.Margin.Value.Top, Is.EqualTo(64));
                Assert.That(scope.Height.Value, Is.EqualTo(192));
                Assert.That(timeline.FindViewModelFor(unrealized), Is.Null);
                Assert.That(unrealizedBounds.Y, Is.EqualTo(192));
                Assert.That(unrealizedBounds.Height, Is.EqualTo(64));
                Assert.That(IsCompactHit(compactBounds, 48), Is.True, "The row-0 viewport entry must grow even though its top stays fixed.");
                Assert.That(IsCompactHit(unrealizedBounds, 48), Is.True, "The compact surface must use the taller bounds.");
            });

            bool IsCompactHit(Rect bounds, double rowOffset)
            {
                var point = new Point(bounds.Center.X, bounds.Y + rowOffset);
                return host.Timeline.Children.OfType<CompactElementLayer>()
                    .Any(layer => layer.HitTest(host.Timeline.TranslatePoint(point, layer)!.Value));
            }
        }
        finally
        {
            size.Value = previous;
        }
    }

    [AvaloniaTest]
    public async Task Changing_all_rows_emits_one_notification_and_rebuilds_the_layer_tops()
    {
        await TestReset.ResetShellAsync();
        var size = TestShell.Configuration.EditorConfig.TimelineLayerSize;
        TimelineLayerSize previous = size.Value;
        try
        {
            size.Value = TimelineLayerSize.Small;
            using var host = await TimelineTestHost.CreateAsync(
                "timeline-batched-row-height", MediaTime.FromSeconds(10), show: false);
            TimelineTabViewModel timeline = host.Model;
            timeline.Options.Value = timeline.Options.Value with { MaxLayerCount = 60 };
            var notifications = new List<LayerHeaderViewModel?>();
            var lastRowTops = new List<double>();
            using var heightChanges = timeline.LayerHeightChanged.Subscribe(notifications.Add);
            using var trackedTop = timeline.GetTrackedLayerTopObservable(59).Subscribe(lastRowTops.Add);

            size.Value = TimelineLayerSize.Large;
            Assert.Multiple(() =>
            {
                Assert.That(notifications, Is.EqualTo(new LayerHeaderViewModel?[] { null }));
                Assert.That(lastRowTops, Is.EqualTo(new[] { 59 * 25d, 59 * 64d }));
                Assert.That(timeline.LayerHeaders.Select(header => header.Height.Value), Is.All.EqualTo(64));
            });

            size.Value = (TimelineLayerSize)0;
            size.Value = TimelineLayerSize.Small;
            Assert.Multiple(() =>
            {
                Assert.That(notifications, Has.Count.EqualTo(2), "An invalid preset falls back to Small without an extra update.");
                Assert.That(lastRowTops[^1], Is.EqualTo(59 * 25));
            });
        }
        finally
        {
            size.Value = previous;
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_drag_lands_on_the_expected_large_row(bool reorderHeader)
    {
        await TestReset.ResetShellAsync();
        var size = TestShell.Configuration.EditorConfig.TimelineLayerSize;
        TimelineLayerSize previous = size.Value;
        try
        {
            size.Value = TimelineLayerSize.Large;
            using var host = await TimelineTestHost.CreateAsync(
                "timeline-large-drag", MediaTime.FromSeconds(10), [Clip(0), Clip(1)]);
            host.Model.ClearSelected();
            HeadlessTestHelpers.Render(3);
            Element moved = host.Scene.Children.Single(clip => clip.ZIndex.Value == 0);
            Element other = host.Scene.Children.Single(clip => clip.ZIndex.Value == 1);
            Control source = reorderHeader ? FindHeader(host, 0) : FindClip(host, 0);
            Border handle = source.FindControl<Border>("border")!;
            Point start = HeadlessTestHelpers.Center(handle, host.Window);
            Point end = start + new Vector(0, 104);
            TimelineTestHost.WaitForHitTest(host.Window, handle, start);
            host.Window.MouseMove(start);
            host.Window.MouseDown(start, MouseButton.Left);
            host.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            host.Window.MouseUp(end, MouseButton.Left);

            await HeadlessTestHelpers.WaitUntilAsync(() => moved.ZIndex.Value == 2);
            ElementViewModel model = host.Model.FindViewModelFor(moved)!;
            await HeadlessTestHelpers.WaitUntilAsync(() => model.Margin.Value.Top == 128);
            Assert.That(other.ZIndex.Value, Is.EqualTo(reorderHeader ? 0 : 1));
        }
        finally
        {
            size.Value = previous;
        }
    }

    [AvaloniaTest]
    public async Task The_settings_picker_round_trips_the_preset_and_ignores_invalid_indices()
    {
        await TestReset.ResetShellAsync();
        EditorConfig config = TestShell.Configuration.EditorConfig;
        TimelineLayerSize previous = config.TimelineLayerSize.Value;
        try
        {
            config.TimelineLayerSize.Value = TimelineLayerSize.Small;
            using var viewModel = new EditorSettingsPageViewModel(TestShell.Configuration);
            var page = new EditorSettingsPage { DataContext = viewModel };
            var window = new Window { Content = page, Width = 900, Height = 700, RequestedThemeVariant = ThemeVariant.Dark };
            try
            {
                window.Show();
                HeadlessTestHelpers.Render(3);
                var row = page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                    .Single(item => Equals(item.Header, SettingsStrings.TimelineLayerSize));
                var picker = (ComboBox)row.ActionButton!;
                Assert.Multiple(() =>
                {
                    Assert.That(picker.Items.OfType<ComboBoxItem>().Select(item => item.Content), Is.EqualTo(new[]
                    {
                        SettingsStrings.TimelineLayerSize_Small,
                        SettingsStrings.TimelineLayerSize_Medium,
                        SettingsStrings.TimelineLayerSize_Large
                    }));
                    Assert.That(picker.SelectedIndex, Is.Zero);
                });

                config.TimelineLayerSize.Value = TimelineLayerSize.Medium;
                HeadlessTestHelpers.Render(3);
                Assert.That(picker.SelectedIndex, Is.EqualTo(1));
                picker.SelectedIndex = 2;
                Assert.That(config.TimelineLayerSize.Value, Is.EqualTo(TimelineLayerSize.Large));
                picker.SelectedIndex = -1;
                viewModel.TimelineLayerSize.Value = 3;
                Assert.That(config.TimelineLayerSize.Value, Is.EqualTo(TimelineLayerSize.Large));

                var restored = new EditorConfig();
                CoreSerializer.PopulateFromJsonObject(restored, CoreSerializer.SerializeToJsonObject(config));
                Assert.That(restored.TimelineLayerSize.Value, Is.EqualTo(TimelineLayerSize.Large));

                config.TimelineLayerSize.Value = TimelineLayerSize.Medium;
                row.BringIntoView();
                HeadlessTestHelpers.Render(3);
                HeadlessCapture.SaveIfRequested(window, nameof(TimelineLayerHeightTests), "editor-settings");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            config.TimelineLayerSize.Value = previous;
        }
    }

    [AvaloniaTest]
    public async Task The_timeline_context_menu_has_no_layer_height_picker()
    {
        await TestReset.ResetShellAsync();
        using var host = await TimelineTestHost.CreateAsync(
            "timeline-layer-height-menu", MediaTime.FromSeconds(10), show: false);
        var menu = (FAMenuFlyout)host.Timeline.ContextFlyout!;
        Assert.That(MenuLabels(menu.Items), Does.Not.Contain(SettingsStrings.TimelineLayerSize));

        static IEnumerable<string?> MenuLabels(System.Collections.IEnumerable items)
        {
            foreach (object item in items)
            {
                if (item is FAMenuFlyoutSubItem submenu)
                {
                    yield return submenu.Text;
                    foreach (string? label in MenuLabels(submenu.Items))
                        yield return label;
                }
                else if (item is FAMenuFlyoutItem action)
                {
                    yield return action.Text;
                }
            }
        }
    }

    private static ElementDescription Clip(int layer, double start = 0, double length = 2)
        => new(MediaTime.FromSeconds(start), MediaTime.FromSeconds(length), layer, new ElementSource.EngineObject(() => new RectShape()));

    private static ElementView FindClip(TimelineTestHost host, int layer)
        => host.Timeline.Children.OfType<ElementView>()
            .Single(view => ((ElementViewModel)view.DataContext!).Model.ZIndex.Value == layer);

    private static LayerHeader FindHeader(TimelineTestHost host, int layer)
        => host.View.GetVisualDescendants().OfType<LayerHeader>()
            .Single(header => ((LayerHeaderViewModel)header.DataContext!).Number == layer);
}

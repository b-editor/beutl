using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ElementViewMoveSnappingTests
{
    [AvaloniaTest]
    [TestCase(150, 1f, false)]
    [TestCase(151, 1f, false)]
    [TestCase(20, 1f, false)]
    [TestCase(38.5, 0.5f, false)]
    [TestCase(151, 1f, true)]
    public async Task Move_SnapsTheClipStartRegardlessOfGrabOffset(double grabOffset, float scale, bool grouped)
    {
        bool originalSnap = GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value;
        GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value = true;
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            var descriptions = new List<ElementDescription>
            {
                new(MediaTime.Zero, MediaTime.FromSeconds(3), 0, new ElementSource.EngineObject(() => new RectShape())),
                new(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2), 1, new ElementSource.EngineObject(() => new RectShape()))
            };
            if (grouped)
                descriptions.Add(new(MediaTime.FromSeconds(4.5), MediaTime.FromSeconds(2), 2,
                    new ElementSource.EngineObject(() => new RectShape())));

            (EditViewModel editor, TimelineTabViewModel timeline) = await TimelineTestHost.OpenTimelineAsync(
                $"move-snapping-{Guid.NewGuid():N}", descriptions);
            timeline.Options.Value = timeline.Options.Value with { Scale = scale };
            ElementViewModel clip = timeline.GetViewModelFor(editor.Scene.Children.Single(e => e.ZIndex.Value == 1))!;
            ElementViewModel? companion = grouped
                ? timeline.GetViewModelFor(editor.Scene.Children.Single(e => e.ZIndex.Value == 2))
                : null;
            timeline.ClearSelected();
            timeline.SelectElement(clip);
            if (companion is not null)
            {
                timeline.SelectElement(companion);
                clip.GroupSelectedElements.Execute();
            }
            editor.HistoryManager.Commit();
            int undoCount = editor.HistoryManager.UndoCount;
            int feedbackCount = 0;
            view = new TimelineTabView(NativeScrollInput.UsesGestureAxes,
                alignmentFeedback: (_, _) => feedbackCount++)
            { DataContext = timeline };
            window = new Window { Content = view, Width = 1200, Height = 420 };
            window.Show();
            HeadlessTestHelpers.Render(5);
            Border border = view.GetVisualDescendants().OfType<ElementView>()
                .Single(v => ReferenceEquals(v.DataContext, clip)).FindControl<Border>("border")!;
            Point press = border.TranslatePoint(new Point(grabOffset, border.Bounds.Height / 2), window)!.Value;
            TimelineTestHost.WaitForHitTest(window, border, press);
            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);

            Move(3.02);
            AssertPreview(3, 3);
            Move(3.03);
            Assert.That(feedbackCount, Is.EqualTo(1), "Staying on the same snap target produces one pulse.");

            // Alt pressed after the drag begins temporarily disables snapping without duplicating the clip.
            Move(3.02, RawInputModifiers.Alt);
            AssertPreview(91.0 / 30, null);
            Assert.That(feedbackCount, Is.EqualTo(1));
            Move(3.02);
            AssertPreview(3, 3);
            Assert.That(feedbackCount, Is.EqualTo(2), "Returning after Alt re-enables feedback.");

            timeline.IsSnapEnabled.Value = false;
            Move(3.02);
            AssertPreview(91.0 / 30, null);
            Assert.That(feedbackCount, Is.EqualTo(2), "Disabled snapping is silent.");
            timeline.IsSnapEnabled.Value = true;

            // Crossing a candidate with the grabbed point must not magnetize the clip's centre.
            double pointerOnlyStart = 3.02 - grabOffset.PixelToTime(scale).ToSeconds();
            Move(pointerOnlyStart);
            AssertPreview(Math.Round(pointerOnlyStart * 30, MidpointRounding.AwayFromZero) / 30, null);

            Move(-1.0 / 30);
            AssertPreview(0, 0);
            Assert.That(feedbackCount, Is.EqualTo(3));
            Move(0.2);
            AssertPreview(0.2, null);

            Point release = Move(3.02);
            AssertPreview(3, 3);
            Assert.That(feedbackCount, Is.EqualTo(4));
            Assert.That(clip.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)), "The model changes only on commit.");
            HeadlessCapture.SaveIfRequested(window, nameof(ElementViewMoveSnappingTests), $"grab-{grabOffset}-scale-{scale}-group-{grouped}");
            window.MouseUp(release, MouseButton.Left);
            await Task.Delay(300);
            HeadlessTestHelpers.Render(5);
            Assert.Multiple(() =>
            {
                Assert.That(clip.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
                Assert.That(clip.BorderMargin.Value.Left, Is.EqualTo(MediaTime.FromSeconds(3).TimeToPixel(scale)).Within(0.0001));
                Assert.That(timeline.SnapBarPosition.Value, Is.Null);
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
                Assert.That(feedbackCount, Is.EqualTo(4), "Releasing does not repeat the snap feedback.");
                if (companion is not null)
                    Assert.That(companion.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3.5)));
            });
            Assert.That(await editor.UndoAsync(), Is.True);
            Assert.That(feedbackCount, Is.EqualTo(4), "Undo is silent.");
            Assert.That(clip.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            if (companion is not null)
                Assert.That(companion.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4.5)));

            Point Move(double start, RawInputModifiers modifiers = RawInputModifiers.None)
            {
                Point point = press + new Vector(MediaTime.FromSeconds(start - 4).TimeToPixel(scale), 0);
                window.MouseMove(point, modifiers | RawInputModifiers.LeftMouseButton);
                HeadlessTestHelpers.Render(3);
                return point;
            }

            void AssertPreview(double start, double? guide)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(clip.BorderMargin.Value.Left, Is.EqualTo(MediaTime.FromSeconds(start).TimeToPixel(scale)).Within(0.0001));
                    if (guide is { } seconds)
                        Assert.That(timeline.SnapBarPosition.Value, Is.EqualTo(MediaTime.FromSeconds(seconds).TimeToPixel(scale)).Within(0.0001));
                    else
                        Assert.That(timeline.SnapBarPosition.Value, Is.Null);
                    if (companion is not null)
                        Assert.That(companion.BorderMargin.Value.Left - clip.BorderMargin.Value.Left,
                            Is.EqualTo(MediaTime.FromSeconds(0.5).TimeToPixel(scale)).Within(0.0001));
                });
            }
        }
        finally
        {
            TimelineTestHost.Close(view, window);
            GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value = originalSnap;
        }
    }
}

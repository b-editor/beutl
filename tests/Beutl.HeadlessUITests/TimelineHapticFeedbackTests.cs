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

[TestFixture, NonParallelizable]
public class TimelineHapticFeedbackTests
{
    [AvaloniaTest]
    [TestCase("left")]
    [TestCase("right")]
    [TestCase("roll")]
    [TestCase("slide")]
    public async Task Drag_OnlyPulsesForReachableSnaps_AndResetsAfterCancellation(string kind)
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool originalSnap = config.IsTimelineSnapEnabled.Value;
        bool originalRipple = config.IsRippleEnabled.Value;
        config.IsTimelineSnapEnabled.Value = true;
        config.IsRippleEnabled.Value = false;
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            bool trim = kind is "roll" or "slide";
            ElementDescription[] descriptions = trim
                ? [Clip(0, 3, 0), Clip(3, 2, 0), Clip(5, 3, 0), Clip(4, 2, 1)]
                : [Clip(4, 2, 0), Clip(7, 1, 0), Clip(1, 2, 1)];
            (EditViewModel editor, TimelineTabViewModel timeline) = await TimelineTestHost.OpenTimelineAsync(
                $"haptic-{kind}-{Guid.NewGuid():N}", descriptions);
            double initialStart = kind == "roll" ? 0 : kind == "slide" ? 3 : 4;
            ElementViewModel clip = timeline.GetViewModelFor(editor.Scene.Children.Single(e =>
                e.ZIndex.Value == 0 && e.Start.Value == MediaTime.FromSeconds(initialStart)))!;
            timeline.ClearSelected();
            timeline.SelectElement(clip);
            timeline.IsRollMode.Value = kind == "roll";
            timeline.IsSlideMode.Value = kind == "slide";
            editor.HistoryManager.Commit();

            using var feedback = new HapticFeedbackRecorder();
            view = new TimelineTabView { DataContext = timeline };
            window = new Window { Content = view, Width = 1400, Height = 420 };
            window.Show();
            HeadlessTestHelpers.Render(5);
            ElementView element = view.GetVisualDescendants().OfType<ElementView>()
                .Single(v => ReferenceEquals(v.DataContext, clip));
            Border border = element.FindControl<Border>("border")!;
            double pressX = kind == "left" ? 2 : kind == "slide" ? border.Bounds.Width / 2 : border.Bounds.Width - 2;
            Point press = border.TranslatePoint(new Point(pressX, border.Bounds.Height / 2), window)!.Value;
            TimelineTestHost.WaitForHitTest(window, border, press);
            double target = kind switch { "left" => 3, "right" => 7, "roll" => 4, _ => 6 };
            double unreachable = kind switch { "left" => 7, "roll" => 6, _ => 8 };

            Begin();
            Move(target + 0.02);
            Assert.That(feedback.Pulses.Count, Is.EqualTo(1));
            Move(target + 0.03);
            Assert.That(feedback.Pulses.Count, Is.EqualTo(1), "Repeated trim calculations must not pulse for the press point.");
            Assert.That(timeline.SnapBarPosition.Value,
                Is.EqualTo(MediaTime.FromSeconds(target).TimeToPixel(1)).Within(0.001));
            HeadlessCapture.SaveIfRequested(window, nameof(TimelineHapticFeedbackTests), kind);

            Move(unreachable + 0.02);
            Assert.Multiple(() =>
            {
                Assert.That(feedback.Pulses.Count, Is.EqualTo(1), "A constrained edge did not reach the candidate.");
                Assert.That(timeline.SnapBarPosition.Value, Is.Null);
            });

            Move(target + 0.02);
            Assert.That(feedback.Pulses.Count, Is.EqualTo(2), "Returning to a reachable snap produces feedback again.");
            window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
            window.MouseUp(press, MouseButton.Left);
            HeadlessTestHelpers.Render(3);
            Assert.Multiple(() =>
            {
                Assert.That(feedback.Pulses.Count, Is.EqualTo(2));
                Assert.That(timeline.SnapBarPosition.Value, Is.Null);
                Assert.That(clip.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(initialStart)));
                Assert.That(clip.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(kind == "roll" ? 3 : 2)));
            });

            Begin();
            Point release = Move(target + 0.02);
            Assert.That(feedback.Pulses.Count, Is.EqualTo(3), "A new gesture can snap to the same target.");
            window.MouseUp(release, MouseButton.Left);
            HeadlessTestHelpers.Settle(4);
            Assert.Multiple(() =>
            {
                Assert.That(feedback.Pulses.Count, Is.EqualTo(3), "Commit calculations are silent.");
                Assert.That(timeline.SnapBarPosition.Value, Is.Null);
                Assert.That(clip.Model.Start.Value.ToSeconds(), Is.EqualTo(kind == "left" ? 3 : kind == "slide" ? 5 : initialStart));
                Assert.That(clip.Model.Range.End.ToSeconds(), Is.EqualTo(kind == "roll" ? 4 : kind is "right" or "slide" ? 7 : 6));
            });
            Assert.That(await editor.UndoAsync(), Is.True);
            Assert.That(feedback.Pulses.Count, Is.EqualTo(3));

            void Begin()
            {
                int beforePress = feedback.Pulses.Count;
                window.MouseMove(press);
                window.MouseDown(press, MouseButton.Left);
                Assert.That(feedback.Pulses.Count, Is.EqualTo(beforePress), "Press alone is silent.");
            }

            Point Move(double seconds)
            {
                Point point = element.TranslatePoint(
                    new Point(MediaTime.FromSeconds(seconds).TimeToPixel(1), border.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point, RawInputModifiers.LeftMouseButton);
                HeadlessTestHelpers.Render(3);
                return point;
            }
        }
        finally
        {
            TimelineTestHost.Close(view, window);
            config.IsTimelineSnapEnabled.Value = originalSnap;
            config.IsRippleEnabled.Value = originalRipple;
        }
    }

    private static ElementDescription Clip(double start, double length, int layer)
        => new(MediaTime.FromSeconds(start), MediaTime.FromSeconds(length), layer,
            new ElementSource.EngineObject(() => new RectShape()));
}

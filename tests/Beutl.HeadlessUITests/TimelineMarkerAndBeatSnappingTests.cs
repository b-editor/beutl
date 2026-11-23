using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

// The timeline's drags stop on markers and on the BPM grid's lines as it shows them, and so does a drag of an inline
// row's key, which also stops on the keys of the other open rows.
[TestFixture, NonParallelizable]
public class TimelineMarkerAndBeatSnappingTests
{
    public enum ClipGesture { Move, TrimEnd }

    public enum ClipTarget { Marker, Beat, HiddenBeat }

    public enum KeyTarget
    {
        Marker, Playhead, Beat, OtherRowKey, ElementEnd, TimeAnchor, OffFrameAnchor, Shift, OwnRowKey, Alt, ShiftAlt, SnapOff
    }

    [AvaloniaTest]
    [TestCase(ClipGesture.Move, ClipTarget.Marker)]
    [TestCase(ClipGesture.Move, ClipTarget.Beat)]
    [TestCase(ClipGesture.Move, ClipTarget.HiddenBeat)]
    [TestCase(ClipGesture.TrimEnd, ClipTarget.Marker)]
    [TestCase(ClipGesture.TrimEnd, ClipTarget.Beat)]
    [TestCase(ClipGesture.TrimEnd, ClipTarget.HiddenBeat)]
    public async Task A_clip_edge_stops_on_a_marker_and_on_a_beat_while_the_grid_shows(ClipGesture gesture, ClipTarget target)
    {
        EditorConfig config = TestShell.Configuration.EditorConfig;
        bool originalSnap = config.IsTimelineSnapEnabled.Value;
        bool originalRipple = config.IsRippleEnabled.Value;
        config.IsTimelineSnapEnabled.Value = true;
        config.IsRippleEnabled.Value = false;
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, TimelineTabViewModel timeline) = await TimelineTestHost.OpenTimelineAsync(
                $"snap-{gesture}-{target}", [new(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), 0,
                    new ElementSource.EngineObject(() => new RectShape()))]);
            ElementViewModel clip = timeline.GetViewModelFor(editor.Scene.Children.Single())!;
            // 4 s is a marker, or a beat of a 120 BPM grid; the edge is dragged to 4.03 s, 4.5 px past it.
            MediaTime stop = MediaTime.FromSeconds(4);
            MediaTime reached = MediaTime.FromSeconds(4.03);
            if (target == ClipTarget.Marker)
                editor.Scene.Markers.Add(new SceneMarker(stop));
            else
                timeline.Options.Value = timeline.Options.Value with
                {
                    BpmGrid = new BpmGridOptions(120, 1, MediaTime.Zero, target == ClipTarget.Beat)
                };

            view = new TimelineTabView { DataContext = timeline };
            window = new Window { Content = view, Width = 1400, Height = 420 };
            window.Show();
            HeadlessTestHelpers.Render(5);
            ElementView element = view.GetVisualDescendants().OfType<ElementView>()
                .Single(v => ReferenceEquals(v.DataContext, clip));
            Border border = element.FindControl<Border>("border")!;
            // A move grabs the body and carries the start; a trim takes the end to the pointer.
            Point press = border.TranslatePoint(
                new Point(gesture == ClipGesture.Move ? 40 : border.Bounds.Width - 2, border.Bounds.Height / 2), window)!.Value;
            Point release = gesture == ClipGesture.Move
                ? press + new Vector((reached - clip.Model.Start.Value).TimeToPixel(1), 0)
                : element.TranslatePoint(new Point(reached.TimeToPixel(1), border.Bounds.Height / 2), window)!.Value;
            TimelineTestHost.WaitForHitTest(window, border, press);

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(release, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            bool snaps = target != ClipTarget.HiddenBeat;
            Assert.That(timeline.SnapBarPosition.Value,
                snaps ? Is.EqualTo(stop.TimeToPixel(1)).Within(0.001) : Is.Null);
            HeadlessCapture.SaveIfRequested(window, nameof(TimelineMarkerAndBeatSnappingTests), $"{gesture}-{target}");

            window.MouseUp(release, MouseButton.Left);
            HeadlessTestHelpers.Settle(4);
            Rational rate = SceneTimeRangeService.GetFrameRate(editor.Scene);
            MediaTime edge = gesture == ClipGesture.Move ? clip.Model.Start.Value : clip.Model.Range.End;
            Assert.Multiple(() =>
            {
                Assert.That(edge, Is.EqualTo(snaps ? stop : reached.RoundToFrame(rate, MidpointRounding.AwayFromZero)));
                Assert.That(timeline.SnapBarPosition.Value, Is.Null);
            });
        }
        finally
        {
            TimelineTestHost.Close(view, window);
            config.IsTimelineSnapEnabled.Value = originalSnap;
            config.IsRippleEnabled.Value = originalRipple;
        }
    }

    // The clip runs from 1 s to 3 s and its Width has keys at 0.5 s and 1.5 s of its own time, 1.5 s and 2.5 s on the
    // timeline. The first key is dragged to 1.98 s, 3 px before a target at 2 s.
    [AvaloniaTest]
    [TestCase(KeyTarget.Marker)]
    [TestCase(KeyTarget.Playhead)]
    [TestCase(KeyTarget.Beat)]
    [TestCase(KeyTarget.OtherRowKey)]
    [TestCase(KeyTarget.ElementEnd)]
    [TestCase(KeyTarget.TimeAnchor)]
    [TestCase(KeyTarget.OffFrameAnchor)]
    [TestCase(KeyTarget.Shift)]
    [TestCase(KeyTarget.OwnRowKey)]
    [TestCase(KeyTarget.Alt)]
    [TestCase(KeyTarget.ShiftAlt)]
    [TestCase(KeyTarget.SnapOff)]
    public async Task An_inline_key_stops_on_the_timeline_targets_in_scene_time(KeyTarget target)
    {
        EditorConfig config = TestShell.Configuration.EditorConfig;
        bool originalSnap = config.IsTimelineSnapEnabled.Value;
        config.IsTimelineSnapEnabled.Value = target != KeyTarget.SnapOff;
        await TestReset.ResetShellAsync();
        using GraphScope graph = await GraphScope.CreateAsync(elementStart: MediaTime.FromSeconds(1));
        var editor = (EditViewModel)graph.Model.EditorContext;
        Element element = graph.Model.Element!;
        var shape = element.Objects.OfType<RectShape>().Single();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        MediaTime stop = MediaTime.FromSeconds(2);
        InlineAnimationLayerViewModel? otherRow = null;
        switch (target)
        {
            case KeyTarget.Playhead:
                ((IEditorClock)editor.GetService(typeof(IEditorClock))!).Seek(stop);
                break;
            case KeyTarget.Beat:
                timeline.Options.Value = timeline.Options.Value with
                {
                    BpmGrid = new BpmGridOptions(120, 1, MediaTime.Zero, true)
                };
                break;
            case KeyTarget.OtherRowKey:
                var height = new KeyFrameAnimation<float>();
                height.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 50 } });
                shape.Height.SetKeyFrameAnimation(height);
                var heightProperty = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Height, shape);
                timeline.AttachInline(heightProperty, element);
                otherRow = timeline.Inlines.Single(item => ReferenceEquals(item.Property, heightProperty));
                break;
            case KeyTarget.ElementEnd:
                element.Length.Value = MediaTime.FromSeconds(1);
                break;
            case KeyTarget.OwnRowKey:
                graph.Second.KeyTime.Value = MediaTime.FromSeconds(1);
                break;
            case KeyTarget.TimeAnchor or KeyTarget.OffFrameAnchor:
                // The keys count from the shape's own start, not from the clip's. At 0.81 s it falls between frames,
                // and a key can only be on a frame of its own clock, the one nearest the marker.
                shape.IsTimeAnchor.Value = true;
                shape.TimeRange.Value = new TimeRange(
                    MediaTime.FromSeconds(target == KeyTarget.TimeAnchor ? 0.8 : 0.81), MediaTime.FromSeconds(10));
                editor.Scene.Markers.Add(new SceneMarker(stop));
                break;
            default:
                editor.Scene.Markers.Add(new SceneMarker(stop));
                break;
        }

        var property = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape);
        timeline.AttachInline(property, element);
        InlineAnimationLayerViewModel inline = timeline.Inlines.Single(item => ReferenceEquals(item.Property, property));
        var view = new TimelineTabView { DataContext = timeline };
        graph.Window.Content = view;
        graph.Window.Width = 960;
        using var feedback = new HapticFeedbackRecorder();
        try
        {
            HeadlessTestHelpers.Render(3);
            InlineAnimationLayer layer = view.GetVisualDescendants().OfType<InlineAnimationLayer>()
                .Single(item => ReferenceEquals(item.DataContext, inline));
            AvaloniaPath key = layer.GetVisualDescendants().OfType<AvaloniaPath>()
                .Single(item => item.DataContext is InlineKeyFrameViewModel { Model: var model } && model == graph.First);
            Point press = key.TranslatePoint(new Point(key.Bounds.Width / 2, key.Bounds.Height / 2), graph.Window)!.Value;
            Assert.That(graph.HitTest(press), Is.SameAs(key));
            MediaTime origin = graph.Animation.ToScene(MediaTime.Zero);
            MediaTime reached = MediaTime.FromSeconds(1.98);
            Point release = press + new Vector((reached - graph.Animation.ToScene(graph.First.KeyTime.Value)).TimeToPixel(1), 0);
            RawInputModifiers modifiers = target switch
            {
                KeyTarget.Shift => RawInputModifiers.Shift,
                KeyTarget.Alt => RawInputModifiers.Alt,
                KeyTarget.ShiftAlt => RawInputModifiers.Shift | RawInputModifiers.Alt,
                _ => RawInputModifiers.None
            };

            bool snaps = target is not (KeyTarget.OwnRowKey or KeyTarget.Alt or KeyTarget.ShiftAlt or KeyTarget.SnapOff);
            Rational rate = SceneTimeRangeService.GetFrameRate(editor.Scene);
            MediaTime first = (snaps ? stop - origin : reached - origin).RoundToFrame(rate, MidpointRounding.AwayFromZero);

            graph.Window.MouseMove(press, modifiers);
            graph.Window.MouseDown(press, MouseButton.Left, modifiers);
            graph.Window.MouseMove(release, modifiers | RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            Assert.Multiple(() =>
            {
                Assert.That(timeline.SnapBarPosition.Value,
                    snaps ? Is.EqualTo((origin + first).TimeToPixel(1)).Within(0.001) : Is.Null);
                Assert.That(feedback.Pulses, Has.Count.EqualTo(snaps ? 1 : 0));
                // A snapped key already shows where the release leaves it.
                if (snaps)
                {
                    Assert.That(((InlineKeyFrameViewModel)key.DataContext!).Left.Value,
                        Is.EqualTo(first.TimeToPixel(1)).Within(0.001));
                }
            });
            HeadlessCapture.SaveIfRequested(graph.Window, nameof(TimelineMarkerAndBeatSnappingTests), $"Key-{target}");

            graph.Window.MouseUp(release, MouseButton.Left, modifiers);
            HeadlessTestHelpers.Settle(4);
            // A Shift drag moves the rest of the row as far as the key it holds; both keys start on frames.
            MediaTime second = target is KeyTarget.Shift or KeyTarget.ShiftAlt
                ? MediaTime.FromSeconds(1.5) + first - MediaTime.FromSeconds(0.5)
                : MediaTime.FromSeconds(target == KeyTarget.OwnRowKey ? 1 : 1.5);
            Assert.Multiple(() =>
            {
                Assert.That(graph.First.KeyTime.Value, Is.EqualTo(first));
                Assert.That(graph.Second.KeyTime.Value, Is.EqualTo(second));
                Assert.That(timeline.SnapBarPosition.Value, Is.Null);
            });
        }
        finally
        {
            view.DataContext = null;
            timeline.DetachInline(inline);
            if (otherRow != null) timeline.DetachInline(otherRow);
            config.IsTimelineSnapEnabled.Value = originalSnap;
        }
    }
}

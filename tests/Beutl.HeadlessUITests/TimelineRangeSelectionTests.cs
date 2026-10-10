using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineRangeSelectionTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Range_crossing_ruler_selects_overlapping_clips(bool reverse, bool scrolled)
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        if (scrolled)
        {
            session.Scroll(session.Model.LayerHeaders[0].Height.Value / 2);
        }

        Point rulerPoint = session.Ruler.TranslatePoint(new Point(15, 10), session.Window)!.Value;
        Point contentPoint = session.Timeline.TranslatePoint(
            new Point(TimeSpan.FromSeconds(1.5).TimeToPixel(1), session.Model.CalculateLayerTop(3) - 10),
            session.Window)!.Value;

        session.Drag(reverse ? contentPoint : rulerPoint, reverse ? rulerPoint : contentPoint,
            $"ruler-{reverse}-{scrolled}", [0, 2]);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Range_crossing_ruler_after_deep_scroll_selects_only_visible_clips(bool reverse)
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        session.Scroll(100);
        Point rulerPoint = session.Ruler.TranslatePoint(new Point(15, 5), session.Window)!.Value;
        Point contentPoint = session.ToWindow(new Point(225, session.Model.CalculateLayerTop(7) - 10));

        session.Drag(reverse ? contentPoint : rulerPoint, reverse ? rulerPoint : contentPoint,
            $"deep-ruler-{reverse}", [6]);
    }

    [AvaloniaTest]
    public async Task Range_crossing_left_edge_selects_overlapping_clips()
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        session.Drag(session.ToWindow(new Point(225, session.Model.CalculateLayerTop(3) - 10)),
            session.ToWindow(new Point(-30, 5)), "left", [0, 2]);
    }

    [AvaloniaTest]
    public async Task Range_crossing_bottom_edge_selects_clips_without_adding_layers()
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        int layerCount = session.Model.LayerHeaders.Count;
        session.Drag(session.ToWindow(new Point(15, 5)),
            session.ToWindow(new Point(225, session.Timeline.Bounds.Height + 40)), "bottom", [0, 2, 3, 6]);
        Assert.That(session.Model.LayerHeaders, Has.Count.EqualTo(layerCount));
    }

    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(100)]
    public async Task Range_entirely_in_ruler_does_not_select_clips(double scrollY)
    {
        using TimelineSession session = await TimelineSession.CreateAsync();
        if (scrollY > 0) session.Scroll(scrollY);
        session.Model.SelectElement(session.Model.Elements.First());
        session.Drag(session.Ruler.TranslatePoint(new Point(15, 5), session.Window)!.Value,
            session.Ruler.TranslatePoint(new Point(225, 25), session.Window)!.Value, $"outside-{scrollY}", []);
    }

    private sealed class TimelineSession : IDisposable
    {
        private readonly TimelineTabView _view;
        private readonly TimelineOverlay _overlay;
        private static readonly RawInputModifiers CommandModifier = KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;

        private TimelineSession(TimelineTabViewModel model)
        {
            Model = model;
            _view = new TimelineTabView { DataContext = model };
            Window = new Window { Content = _view, Width = 960, Height = 420 };
            Window.Show();
            HeadlessTestHelpers.Render();
            Content = _view.FindControl<ScrollViewer>("ContentScroll")!;
            Timeline = _view.FindControl<Panel>("TimelinePanel")!;
            Ruler = _view.FindControl<TimelineScale>("Scale")!;
            _overlay = _view.FindControl<TimelineOverlay>("overlay")!;
        }

        public TimelineTabViewModel Model { get; }
        public Window Window { get; }
        public ScrollViewer Content { get; }
        public Panel Timeline { get; }
        public TimelineScale Ruler { get; }

        public static async Task<TimelineSession> CreateAsync()
        {
            await TestReset.ResetShellAsync();
            string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"timeline-selection-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "timeline-selection", root))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            scene.Duration = TimeSpan.FromSeconds(5);
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            await adder.AddAsync([
                Clip(0.2, 0), Clip(0.2, 2), Clip(0.2, 3), Clip(0.2, 6), Clip(2, 0)
            ], CancellationToken.None);
            TimelineTabViewModel model = editor.FindToolTab<TimelineTabViewModel>()!;
            model.Options.Value = model.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero, MaxLayerCount = 20 };
            return new TimelineSession(model);

            static ElementDescription Clip(double start, int layer) => new(TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(1.2), layer, new ElementSource.EngineObject(() => new RectShape()));
        }

        public Point ToWindow(Point point) => Timeline.TranslatePoint(point, Window)!.Value;

        public void Scroll(double y)
        {
            Model.Options.Value = Model.Options.Value with { Offset = new System.Numerics.Vector2(75, (float)y) };
            HeadlessTestHelpers.Render();
            Assert.That(Content.Offset, Is.EqualTo(new Vector(75, y)));
        }

        public void Drag(Point from, Point to, string name, int[] expectedLayers)
        {
            Window.MouseMove(from, CommandModifier);
            Window.MouseDown(from, MouseButton.Left, CommandModifier);
            Assert.That(_view._mouseFlag, Is.EqualTo(TimelineHelper.MouseFlags.RangeSelectionPressed));
            try
            {
                Window.MouseMove(to, CommandModifier | RawInputModifiers.LeftMouseButton);
                HeadlessTestHelpers.Render();
                Capture(name);
                Assert.That(Model.SelectedElements.Select(e => e.Model.ZIndex), Is.EquivalentTo(expectedLayers));
                Assert.That(Model.SelectedElements.All(e => e.IsSelected.Value), Is.True);
            }
            finally
            {
                Window.MouseUp(to, MouseButton.Left, CommandModifier);
                HeadlessTestHelpers.Render();
            }

            Assert.Multiple(() =>
            {
                Assert.That(_view._mouseFlag, Is.EqualTo(TimelineHelper.MouseFlags.Free));
                Assert.That(_overlay.SelectionRange, Is.EqualTo(default(Rect)));
                Assert.That(Model.SelectedElements.Select(e => e.Model.ZIndex), Is.EquivalentTo(expectedLayers));
            });
        }

        private void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_TIMELINE_SELECTION_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = Window.CaptureRenderedFrame();
            Assert.That(image, Is.Not.Null);
            image!.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }

        public void Dispose()
        {
            _view.DataContext = null;
            Window.Close();
        }
    }
}

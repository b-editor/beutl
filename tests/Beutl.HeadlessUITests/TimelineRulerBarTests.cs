using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineRulerBarTests
{
    [AvaloniaTest]
    [TestCase(640, false)]
    [TestCase(960, false)]
    [TestCase(640, true)]
    [TestCase(960, true)]
    public async Task Compact_ruler_aligns_with_content_and_preserves_pointer_operations(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"timeline-ruler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "timeline-ruler", root))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(1.2), 0,
            new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        TimelineTabViewModel model = editor.FindToolTab<TimelineTabViewModel>()!;
        model.Options.Value = model.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        scene.Duration = TimeSpan.FromSeconds(2);
        var marker = new SceneMarker(TimeSpan.FromSeconds(0.8), name: "Cue");
        scene.Markers.Add(marker);
        var view = new TimelineTabView { DataContext = model };
        var window = new Window
        {
            Content = view, Width = width, Height = 420,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Border bar = view.FindControl<Border>("RulerBar")!;
            TimelineScale ruler = view.FindControl<TimelineScale>("Scale")!;
            ScrollViewer content = view.FindControl<ScrollViewer>("ContentScroll")!;
            Panel timeline = view.FindControl<Panel>("TimelinePanel")!;
            CheckLayout();
            Capture("initial");

            // Both the 32px ruler and the new upper margin seek along the same time axis.
            Click(ruler, new Point(75, 24));
            Assert.That(model.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
            Click(bar, new Point(90, 4));
            Assert.That(model.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(0.6)));

            Drag(ruler, new Point(120, 8), new Point(180, 8), TimelineHelper.MouseFlags.MarkerPressed);
            Assert.That(marker.Time, Is.EqualTo(TimeSpan.FromSeconds(1.2)));

            // Playback range handles keep their original local hit targets after the 6px shift.
            Drag(ruler, new Point(298, 8), new Point(270, 8), TimelineHelper.MouseFlags.EndingBarMarkerPressed);
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.8)));
            Drag(ruler, new Point(2, 8), new Point(30, 8), TimelineHelper.MouseFlags.StartingBarMarkerPressed);
            Assert.That(scene.Start, Is.EqualTo(TimeSpan.FromSeconds(0.2)));
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(1.6)));

            model.Options.Value = model.Options.Value with { Offset = new System.Numerics.Vector2(75, 0) };
            HeadlessTestHelpers.Render();
            Assert.That(content.Offset.X, Is.EqualTo(75));
            CheckLayout();
            Click(bar, new Point(75, 4));
            Assert.That(model.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Capture("scrolled");

            Point zoomPoint = bar.TranslatePoint(new Point(100, 4), window)!.Value;
            RawInputModifiers modifiers = KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
                ? RawInputModifiers.Meta : RawInputModifiers.Control;
            window.MouseWheel(zoomPoint, new Vector(0, 1), modifiers);
            HeadlessTestHelpers.Render();
            Assert.That(model.Options.Value.Scale, Is.EqualTo(1.2f).Within(0.0001));
            CheckLayout();

            void CheckLayout()
            {
                Assert.Multiple(() =>
                {
                    Assert.That(bar.Bounds.Height, Is.EqualTo(38));
                    Assert.That(ruler.Bounds.Height, Is.EqualTo(32));
                    Assert.That(ruler.TranslatePoint(default, bar)!.Value.Y, Is.EqualTo(6));
                    Assert.That(ruler.TranslatePoint(default, bar)!.Value.X, Is.Zero);
                    Assert.That(ruler.TranslatePoint(new Point(0, ruler.Bounds.Height), view)!.Value.Y,
                        Is.EqualTo(content.TranslatePoint(default, view)!.Value.Y));
                    Assert.That(ruler.TranslatePoint(new Point(100, 0), view)!.Value.X,
                        Is.EqualTo(timeline.TranslatePoint(new Point(100 + content.Offset.X, 0), view)!.Value.X).Within(0.001));
                });
            }

            void Click(Control control, Point position)
            {
                Point point = control.TranslatePoint(position, window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                HeadlessTestHelpers.Render();
            }

            void Drag(Control control, Point from, Point to, TimelineHelper.MouseFlags expectedMode)
            {
                Point start = control.TranslatePoint(from, window)!.Value;
                Point end = control.TranslatePoint(to, window)!.Value;
                window.MouseMove(start);
                window.MouseDown(start, MouseButton.Left);
                Assert.That(view._mouseFlag, Is.EqualTo(expectedMode));
                window.MouseMove(end);
                window.MouseUp(end, MouseButton.Left);
                HeadlessTestHelpers.Render();
                Assert.That(view._mouseFlag, Is.EqualTo(TimelineHelper.MouseFlags.Free));
            }
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }

        void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_TIMELINE_RULER_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}

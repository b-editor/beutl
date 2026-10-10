using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;

using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using AvaPoint = Avalonia.Point;
using BtlPoint = Beutl.Graphics.Point;
using BtlRect = Beutl.Graphics.Rect;
using BtlSize = Beutl.Graphics.Size;
using TextBlock = Beutl.Graphics.Shapes.TextBlock;

namespace Beutl.HeadlessUITests;

// The preview's blue transform box sits on what the selected drawable draws, which is also what the white
// boundary outlines, rather than on its layout box.
[NonParallelizable]
[TestFixture]
public class TransformHandlesOverlayTests
{
    public enum Content
    {
        // A drop shadow extends what the ellipse draws to the bottom right of its layout box.
        ShadowedEllipse,
        // A blur extends what the rectangle draws on every side, and the rectangle is turned.
        TurnedBlurredRect,
        // Glyphs fill less than the text's line box.
        Text,
    }

    [AvaloniaTest]
    [TestCase(Content.ShadowedEllipse)]
    [TestCase(Content.TurnedBlurredRect)]
    [TestCase(Content.Text)]
    public async Task Transform_box_spans_what_the_drawable_draws(Content content)
    {
        GpuTestGate.EnsureAvailable();
        (EditViewModel editor, PlayerView view, Window window, Drawable drawable) = await OpenPreview(content);
        try
        {
            TransformHandlesOverlay overlay = view.transformHandlesOverlay;
            BtlRect boundary = await GetBoundary(editor, drawable);

            Assert.That(overlay.Drawable, Is.SameAs(drawable));
            AvaPoint[] corners = Corners(overlay);
            Assert.Multiple(() =>
            {
                // The box's axis-aligned extent is the boundary, in the preview image's coordinates.
                Assert.That(corners.Min(p => p.X), Is.EqualTo(boundary.Left * overlay.FrameScale).Within(0.05));
                Assert.That(corners.Min(p => p.Y), Is.EqualTo(boundary.Top * overlay.FrameScale).Within(0.05));
                Assert.That(corners.Max(p => p.X), Is.EqualTo(boundary.Right * overlay.FrameScale).Within(0.05));
                Assert.That(corners.Max(p => p.Y), Is.EqualTo(boundary.Bottom * overlay.FrameScale).Within(0.05));
                if (content == Content.TurnedBlurredRect)
                {
                    Avalonia.Vector top = corners[1] - corners[0];
                    Assert.That(Math.Atan2(top.Y, top.X) * 180 / Math.PI, Is.EqualTo(-30).Within(0.01),
                        "The box turns with the drawable instead of standing upright like the boundary.");
                }
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(Content.ShadowedEllipse, 2, 40.0, 24.0)]
    [TestCase(Content.TurnedBlurredRect, 0, -30.0, -20.0)]
    public async Task Dragging_a_corner_takes_it_to_the_pointer_and_keeps_the_opposite_corner(
        Content content, int grabbed, double dx, double dy)
    {
        GpuTestGate.EnsureAvailable();
        (EditViewModel editor, PlayerView view, Window window, _) = await OpenPreview(content);
        try
        {
            TransformHandlesOverlay overlay = view.transformHandlesOverlay;
            AvaPoint[] before = Corners(overlay);
            int opposite = (grabbed + 2) % 4;
            AvaPoint from = before[grabbed];
            AvaPoint to = from + new Avalonia.Vector(dx, dy);
            Assert.That(overlay.HitTest(from), Is.EqualTo(grabbed == 0
                ? TransformHandlesOverlay.HandleKind.TopLeft
                : TransformHandlesOverlay.HandleKind.BottomRight));

            window.MouseDown(ToWindow(view, window, from), MouseButton.Left);
            window.MouseMove(ToWindow(view, window, to), RawInputModifiers.LeftMouseButton);
            window.MouseUp(ToWindow(view, window, to), MouseButton.Left);
            await RenderPreview(editor);

            AvaPoint[] after = Corners(overlay);
            Assert.Multiple(() =>
            {
                Assert.That(after[grabbed].X, Is.EqualTo(to.X).Within(0.5), "grabbed corner X");
                Assert.That(after[grabbed].Y, Is.EqualTo(to.Y).Within(0.5), "grabbed corner Y");
                Assert.That(after[opposite].X, Is.EqualTo(before[opposite].X).Within(0.5), "opposite corner X");
                Assert.That(after[opposite].Y, Is.EqualTo(before[opposite].Y).Within(0.5), "opposite corner Y");
            });
        }
        finally
        {
            window.Close();
        }
    }

    // The turned rectangle's box starts at (-24, -24) in its own space, so the opposite edge is not where an
    // anchor taken from the origin would put it.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Dragging_an_edge_takes_it_to_the_pointer_and_keeps_the_opposite_edge(bool shift)
    {
        GpuTestGate.EnsureAvailable();
        (EditViewModel editor, PlayerView view, Window window, _) = await OpenPreview(Content.TurnedBlurredRect);
        try
        {
            TransformHandlesOverlay overlay = view.transformHandlesOverlay;
            AvaPoint[] before = Corners(overlay);
            // Pull the right edge 30 px outward along the turned box's width.
            Avalonia.Vector across = before[1] - before[0];
            AvaPoint from = Midpoint(before[1], before[2]);
            AvaPoint to = from + (across / across.Length * 30);
            Assert.That(overlay.HitTest(from), Is.EqualTo(TransformHandlesOverlay.HandleKind.Right));

            RawInputModifiers modifiers = shift ? RawInputModifiers.Shift : RawInputModifiers.None;
            window.MouseDown(ToWindow(view, window, from), MouseButton.Left, modifiers);
            window.MouseMove(ToWindow(view, window, to), RawInputModifiers.LeftMouseButton | modifiers);
            window.MouseUp(ToWindow(view, window, to), MouseButton.Left, modifiers);
            await RenderPreview(editor);

            AvaPoint[] after = Corners(overlay);
            AvaPoint grabbed = Midpoint(after[1], after[2]);
            AvaPoint opposite = Midpoint(after[3], after[0]);
            AvaPoint oppositeBefore = Midpoint(before[3], before[0]);
            double widthRatio = AvaPoint.Distance(after[0], after[1]) / across.Length;
            double heightRatio = AvaPoint.Distance(after[0], after[3]) / AvaPoint.Distance(before[0], before[3]);
            Assert.Multiple(() =>
            {
                Assert.That(grabbed.X, Is.EqualTo(to.X).Within(0.5), "grabbed edge X");
                Assert.That(grabbed.Y, Is.EqualTo(to.Y).Within(0.5), "grabbed edge Y");
                Assert.That(opposite.X, Is.EqualTo(oppositeBefore.X).Within(0.5), "opposite edge X");
                Assert.That(opposite.Y, Is.EqualTo(oppositeBefore.Y).Within(0.5), "opposite edge Y");
                // Shift scales the height by the width's ratio; without it only the width changes.
                Assert.That(heightRatio, Is.EqualTo(shift ? widthRatio : 1).Within(0.005), "height ratio");
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static Drawable Create(Content content)
    {
        switch (content)
        {
            case Content.ShadowedEllipse:
                var ellipse = new EllipseShape();
                ellipse.Width.CurrentValue = 120;
                ellipse.Height.CurrentValue = 120;
                var shadow = new DropShadow();
                shadow.Position.CurrentValue = new BtlPoint(40, 30);
                shadow.Sigma.CurrentValue = new BtlSize(6, 6);
                shadow.Color.CurrentValue = Colors.Magenta;
                ((FilterEffectGroup)ellipse.FilterEffect.CurrentValue!).Children.Add(shadow);
                return ellipse;

            case Content.TurnedBlurredRect:
                var rect = new RectShape();
                rect.Width.CurrentValue = 100;
                rect.Height.CurrentValue = 80;
                var blur = new Blur();
                blur.Sigma.CurrentValue = new BtlSize(8, 8);
                ((FilterEffectGroup)rect.FilterEffect.CurrentValue!).Children.Add(blur);
                var transform = (TransformGroup)rect.Transform.CurrentValue!;
                transform.Children.Add(new TranslateTransform(-60, 40));
                transform.Children.Add(new RotationTransform(-30));
                return rect;

            default:
                var text = new TextBlock();
                text.Text.CurrentValue = "Beutl";
                return text;
        }
    }

    private static async Task<(EditViewModel Editor, PlayerView View, Window Window, Drawable Drawable)> OpenPreview(
        Content content)
    {
        await TestReset.ResetShellAsync();
        string name = $"transform-handles-{Guid.NewGuid():N}";
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, location))!;
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        Drawable drawable = Create(content);
        await editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            TimeSpan.Zero, TimeSpan.FromSeconds(4), 0, new ElementSource.EngineObject(() => drawable))],
            CancellationToken.None);
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = editor.Scene.Children.Single();

        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        HeadlessTestHelpers.Render();
        editor.Player.IsMoveMode.Value = true;
        await RenderPreview(editor);
        Assert.That(view.image.Bounds.Width, Is.GreaterThan(0));
        return (editor, view, window, drawable);
    }

    // Waits for a fresh preview frame, then for the overlay update the frame queues on the UI thread.
    private static async Task RenderPreview(EditViewModel editor)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = editor.Player.AfterRendered
            .Take(1)
            .Subscribe(_ => rendered.TrySetResult());
        editor.Player.QueuePreviewRender();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Render();
    }

    private static Task<BtlRect> GetBoundary(EditViewModel editor, Drawable drawable)
        => RenderThread.Dispatcher.InvokeAsync(() => editor.Renderer.Value.GetBoundary(drawable)
            ?? throw new AssertionException("The drawable is not in the rendered frame."));

    // The box's corners in the preview image's coordinates, in the order the overlay draws them.
    private static AvaPoint[] Corners(TransformHandlesOverlay overlay)
    {
        BtlRect bounds = overlay.LocalBounds;
        return [.. new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft }.Select(local =>
        {
            BtlPoint p = overlay.UserMatrix.Transform(local);
            return new AvaPoint(p.X * overlay.FrameScale, p.Y * overlay.FrameScale);
        })];
    }

    private static AvaPoint Midpoint(AvaPoint a, AvaPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static AvaPoint ToWindow(PlayerView view, Window window, AvaPoint imagePoint)
        => view.image.TranslatePoint(imagePoint, window)!.Value;
}

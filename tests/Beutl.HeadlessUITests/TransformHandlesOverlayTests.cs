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
        // The transform is a lone TranslateTransform rather than a group, which the first drag wraps.
        LoneTranslate,
        // The group scales after it translates, with a uniform Scale of its own, as projects written by hand or
        // by an agent can lay it out: [Scale, Translate] rather than the handles' own [Translate, Rotation, Scale].
        ScaledAfterTranslate,
        // A box a couple of pixels tall, whose corner handles overlap.
        Thin,
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
    [TestCase(Content.ScaledAfterTranslate, 2, 40.0, 24.0)]
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

    // Pressing a corner of a box too thin to keep its handles apart takes the corner pressed, not the first one
    // listed: a bottom handle taken for a top one flips the box.
    [AvaloniaTest]
    public async Task Pressing_a_handle_of_a_thin_box_takes_the_nearest_handle()
    {
        GpuTestGate.EnsureAvailable();
        (_, PlayerView view, Window window, _) = await OpenPreview(Content.Thin);
        try
        {
            TransformHandlesOverlay overlay = view.transformHandlesOverlay;
            AvaPoint[] corners = Corners(overlay);
            Assert.That(AvaPoint.Distance(corners[1], corners[2]), Is.LessThan(6),
                "The top and bottom handles have to overlap for this test to mean anything.");
            Assert.Multiple(() =>
            {
                Assert.That(overlay.HitTest(corners[0]), Is.EqualTo(TransformHandlesOverlay.HandleKind.TopLeft));
                Assert.That(overlay.HitTest(corners[1]), Is.EqualTo(TransformHandlesOverlay.HandleKind.TopRight));
                Assert.That(overlay.HitTest(corners[2]), Is.EqualTo(TransformHandlesOverlay.HandleKind.BottomRight));
                Assert.That(overlay.HitTest(corners[3]), Is.EqualTo(TransformHandlesOverlay.HandleKind.BottomLeft));
            });
        }
        finally
        {
            window.Close();
        }
    }

    // The first drag wraps a lone transform in a group inside the document. The drag scales the drawable, and
    // undo puts the lone transform back as the drawable's own.
    [AvaloniaTest]
    public async Task Dragging_a_corner_of_a_drawable_with_a_lone_transform_wraps_it_and_undo_restores_it()
    {
        GpuTestGate.EnsureAvailable();
        (EditViewModel editor, PlayerView view, Window window, Drawable drawable) = await OpenPreview(Content.LoneTranslate);
        try
        {
            Transform lone = drawable.Transform.CurrentValue!;
            TransformHandlesOverlay overlay = view.transformHandlesOverlay;
            AvaPoint[] before = Corners(overlay);
            AvaPoint to = before[2] + new Avalonia.Vector(30, 20);

            window.MouseDown(ToWindow(view, window, before[2]), MouseButton.Left);
            window.MouseMove(ToWindow(view, window, to), RawInputModifiers.LeftMouseButton);
            window.MouseUp(ToWindow(view, window, to), MouseButton.Left);
            await RenderPreview(editor);

            AvaPoint[] after = Corners(overlay);
            Assert.Multiple(() =>
            {
                Assert.That(drawable.Transform.CurrentValue, Is.TypeOf<TransformGroup>());
                Assert.That(((TransformGroup)drawable.Transform.CurrentValue!).Children, Has.Member(lone));
                Assert.That(lone.HierarchicalParent, Is.SameAs(drawable.Transform.CurrentValue));
                Assert.That(after[2].X, Is.EqualTo(to.X).Within(0.5), "grabbed corner X");
                Assert.That(after[2].Y, Is.EqualTo(to.Y).Within(0.5), "grabbed corner Y");
                Assert.That(after[0].X, Is.EqualTo(before[0].X).Within(0.5), "opposite corner X");
                Assert.That(after[0].Y, Is.EqualTo(before[0].Y).Within(0.5), "opposite corner Y");
            });

            editor.HistoryManager.Undo();
            Assert.Multiple(() =>
            {
                Assert.That(drawable.Transform.CurrentValue, Is.SameAs(lone));
                Assert.That(lone.HierarchicalParent, Is.SameAs(drawable));
            });
        }
        finally
        {
            window.Close();
        }
    }

    // A view detached and attached again, such as one moved to another dock, keeps the box on the drawable when
    // the preview image changes size afterwards.
    [AvaloniaTest]
    public async Task Transform_box_stays_on_the_drawable_after_the_view_is_attached_again()
    {
        GpuTestGate.EnsureAvailable();
        (EditViewModel editor, PlayerView view, Window window, Drawable drawable) = await OpenPreview(Content.ShadowedEllipse);
        try
        {
            window.Content = null;
            HeadlessTestHelpers.Render();
            window.Content = view;
            HeadlessTestHelpers.Render();
            window.Width = 800;
            window.Height = 600;
            HeadlessTestHelpers.Render();
            await RenderPreview(editor);

            TransformHandlesOverlay overlay = view.transformHandlesOverlay;
            BtlRect boundary = await GetBoundary(editor, drawable);
            AvaPoint expected = view.image.TranslatePoint(
                new AvaPoint(boundary.Left * overlay.FrameScale, boundary.Top * overlay.FrameScale), window)!.Value;
            AvaPoint drawn = overlay.TranslatePoint(Corners(overlay)[0], window)!.Value;
            // Layout rounding can put the overlay a pixel away from the image it is sized to. A view that stopped
            // following the image is off by half the change in the image's size instead, 117 by 88 px here.
            Assert.Multiple(() =>
            {
                Assert.That(drawn.X, Is.EqualTo(expected.X).Within(1.5), "box left in the window");
                Assert.That(drawn.Y, Is.EqualTo(expected.Y).Within(1.5), "box top in the window");
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

            case Content.ScaledAfterTranslate:
                var scaled = new RectShape();
                scaled.Width.CurrentValue = 160;
                scaled.Height.CurrentValue = 100;
                var layout = (TransformGroup)scaled.Transform.CurrentValue!;
                layout.Children.Add(new ScaleTransform(100, 100, 106));
                layout.Children.Add(new TranslateTransform(0, -60));
                return scaled;

            case Content.Thin:
                var thin = new RectShape();
                thin.Width.CurrentValue = 300;
                thin.Height.CurrentValue = 2;
                return thin;

            case Content.LoneTranslate:
                var moved = new RectShape();
                moved.Width.CurrentValue = 200;
                moved.Height.CurrentValue = 100;
                moved.Transform.CurrentValue = new TranslateTransform(0, 130);
                return moved;

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

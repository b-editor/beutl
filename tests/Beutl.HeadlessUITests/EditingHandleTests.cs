using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using AvaPoint = Avalonia.Point;
using Point = Beutl.Graphics.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public class EditingHandleTests
{
    [AvaloniaTest]
    [TestCase(false, 900, false)]
    [TestCase(true, 420, false)]
    [TestCase(false, 900, true)]
    [TestCase(true, 420, true)]
    public async Task ExtensionHandleTracksPreviewTransformAndCommitsOneUndoableEdit(bool light, int width, bool multiple)
    {
        (EditViewModel editor, PlayerView view, Window window, EditingHandleTestEffect effect) = await Open(width, light, multiple);
        try
        {
            Assert.That(view.transformHandlesOverlay.Drawable, Is.SameAs(effect.FindHierarchicalParent<Drawable>()));
            Point before = effect.Position.Value;
            AvaPoint from = Position(view, before);
            AvaPoint to = from + new Avalonia.Vector(32, 19);
            var overlay = view.transformHandlesOverlay;
            Assert.That(overlay.HitTestEditingHandle(from)?.Provider, Is.SameAs(effect));
            AvaPoint targetFrame = overlay.Viewport!.ToFrame(to);
            Point expected = overlay.UserMatrix.Invert().Transform(new Point((float)targetFrame.X, (float)targetFrame.Y));
            window.MouseDown(ToWindow(view, window, from), MouseButton.Left);
            window.MouseMove(ToWindow(view, window, to), RawInputModifiers.LeftMouseButton);
            window.MouseUp(ToWindow(view, window, to), MouseButton.Left);
            await RenderPreview(editor);
            Assert.That(effect.Position.Value.X, Is.EqualTo(expected.X).Within(0.01));
            Assert.That(effect.Position.Value.Y, Is.EqualTo(expected.Y).Within(0.01));
            Point after = effect.Position.Value;
            editor.HistoryManager.Undo();
            Assert.That(effect.Position.Value, Is.EqualTo(before));
            editor.HistoryManager.Redo();
            Assert.That(effect.Position.Value, Is.EqualTo(after));
            await RenderPreview(editor);
            if (Environment.GetEnvironmentVariable("BEUTL_EDITING_HANDLE_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"handles-{(light ? "light" : "dark")}-{width}-{(multiple ? "multiple" : "single")}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task BoundHandleMovesTheKeyFrameAtTheCurrentTimeAsOneUndoableEdit()
    {
        (EditViewModel editor, PlayerView view, Window window, EditingHandleTestEffect effect) =
            await Open(900, false, bound: true);
        try
        {
            var keyFrames = effect.Position.GetKeyFrameAnimation()!.KeyFrames;
            var current = (KeyFrame<Point>)keyFrames[0];
            var later = (KeyFrame<Point>)keyFrames[1];
            Point before = current.Value.Value;
            Point laterBefore = later.Value.Value;
            Point valueBefore = effect.Position.Value;
            AvaPoint from = Position(view, before);
            AvaPoint to = from + new Avalonia.Vector(32, 19);
            var overlay = view.transformHandlesOverlay;
            AvaPoint targetFrame = overlay.Viewport!.ToFrame(to);
            Point expected = overlay.UserMatrix.Invert().Transform(new Point((float)targetFrame.X, (float)targetFrame.Y));
            window.MouseDown(ToWindow(view, window, from), MouseButton.Left);
            window.MouseMove(ToWindow(view, window, to), RawInputModifiers.LeftMouseButton);
            window.MouseUp(ToWindow(view, window, to), MouseButton.Left);
            Assert.That(current.Value.Value.X, Is.EqualTo(expected.X).Within(0.01));
            Assert.That(current.Value.Value.Y, Is.EqualTo(expected.Y).Within(0.01));
            Assert.That(later.Value.Value, Is.EqualTo(laterBefore), "only the keyframe at the current time moves");
            Assert.That(effect.Position.Value, Is.EqualTo(valueBefore), "an animated property is edited through its keyframes");
            editor.HistoryManager.Undo();
            Assert.That(current.Value.Value, Is.EqualTo(before));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(1)]
    public async Task Bound_component_handle_moves_independent_tracks_and_supports_undo(double seconds)
    {
        (EditViewModel editor, PlayerView view, Window window, EditingHandleTestEffect effect) =
            await Open(900, false, bound: true);
        try
        {
            var components = (IComponentAnimation<Point>)ComponentAnimations.Separate(effect.Position);
            var x = components.Components[0].GetKeyFrameAnimation()!;
            var y = components.Components[1].GetKeyFrameAnimation()!;
            x.KeyFrames[1].KeyTime.Value = MediaTime.FromSeconds(2);
            var time = MediaTime.FromSeconds(seconds);
            editor.GetRequiredService<IEditorClock>().Seek(time);
            await RenderPreview(editor);
            editor.HistoryManager.Commit();
            Point before = components.EvaluateAt(time);
            Point valueBefore = effect.Position.Value;
            AvaPoint from = Position(view, before);
            AvaPoint to = from + new Avalonia.Vector(32, 19);
            var overlay = view.transformHandlesOverlay;
            Assert.That(overlay.HitTestEditingHandle(from)?.Provider, Is.SameAs(effect));
            AvaPoint targetFrame = overlay.Viewport!.ToFrame(to);
            Point expected = overlay.UserMatrix.Invert().Transform(new Point((float)targetFrame.X, (float)targetFrame.Y));
            window.MouseDown(ToWindow(view, window, from), MouseButton.Left);
            window.MouseMove(ToWindow(view, window, from + new Avalonia.Vector(16, 10)), RawInputModifiers.LeftMouseButton);
            window.MouseMove(ToWindow(view, window, to), RawInputModifiers.LeftMouseButton);
            window.MouseUp(ToWindow(view, window, to), MouseButton.Left);
            Point after = components.EvaluateAt(time);
            Assert.That(after.X, Is.EqualTo(expected.X).Within(0.01));
            Assert.That(after.Y, Is.EqualTo(expected.Y).Within(0.01));
            Assert.That(effect.Position.Value, Is.EqualTo(valueBefore));
            Assert.That(x.KeyFrames[1].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(y.KeyFrames[1].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That((float)x.KeyFrames[1].Value!, Is.EqualTo(seconds == 0 ? 70 : 70 + after.X - before.X).Within(0.01));
            Assert.That((float)y.KeyFrames[1].Value!, Is.EqualTo(seconds == 0 ? 50 : 50 + after.Y - before.Y).Within(0.01));
            editor.HistoryManager.Undo();
            Assert.That(components.EvaluateAt(time), Is.EqualTo(before));
            editor.HistoryManager.Redo();
            Assert.That(components.EvaluateAt(time), Is.EqualTo(after));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task DragEndUsesTheHostsHistorySessionAndTheNextDragStillWorks(int end)
    {
        (EditViewModel editor, PlayerView view, Window window, EditingHandleTestEffect effect) = await Open(900, false);
        try
        {
            Point before = effect.Position.Value;
            AvaPoint from = Position(view, before);
            AvaPoint to = from + new Avalonia.Vector(30, 15);
            IPointer? pointer = null;
            view.framePanel.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            window.MouseDown(ToWindow(view, window, from), MouseButton.Left);
            window.MouseMove(ToWindow(view, window, to), RawInputModifiers.LeftMouseButton);
            Assert.That(effect.Position.Value, Is.Not.EqualTo(before));
            if (end == 0) window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
            else if (end == 1) Assert.That(editor.HistoryManager.TryCancelGesture(), Is.True);
            else
            {
                pointer!.Capture(null);
                Assert.That(effect.Position.Value, Is.Not.EqualTo(before), "Capture loss confirms the current gesture.");
                editor.HistoryManager.Undo();
            }
            Assert.That(effect.Position.Value, Is.EqualTo(before));
            window.MouseUp(ToWindow(view, window, to), MouseButton.Left);
            HeadlessTestHelpers.Render();
            await RenderPreview(editor);
            from = Position(view, before);
            to = from + new Avalonia.Vector(15, 10);
            window.MouseDown(ToWindow(view, window, from), MouseButton.Left);
            window.MouseMove(ToWindow(view, window, to), RawInputModifiers.LeftMouseButton);
            window.MouseUp(ToWindow(view, window, to), MouseButton.Left);
            Assert.That(effect.Position.Value, Is.Not.EqualTo(before));
            editor.HistoryManager.Undo();
            Assert.That(effect.Position.Value, Is.EqualTo(before));
        }
        finally { window.Close(); }
    }

    private static async Task<(EditViewModel, PlayerView, Window, EditingHandleTestEffect)> Open(
        int width, bool light, bool multiple = false, bool bound = false)
    {
        TestGpu.RequireAnyDevice();
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("editing-handles");
        var effect = new EditingHandleTestEffect { Bound = bound };
        if (bound)
        {
            effect.Position.SetKeyFrameAnimation(new KeyFrameAnimation<Point>
            {
                KeyFrames =
                {
                    new KeyFrame<Point> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = new Point(35, 25) } },
                    new KeyFrame<Point> { KeyTime = { Value = MediaTime.FromSeconds(4) }, Value = { Value = new Point(70, 50) } },
                },
            });
        }
        var shape = new RectShape { Width = { Value = 180 }, Height = { Value = 120 } };
        ((FilterEffectGroup)shape.FilterEffect.Value!).Children.Add(effect);
        ((TransformGroup)shape.Transform.Value!).Children.Add(new RotationTransform(25));
        await editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            MediaTime.Zero, MediaTime.FromSeconds(4), 0, new ElementSource.EngineObject(() => shape))], CancellationToken.None);
        if (multiple)
        {
            Element element = shape.FindHierarchicalParent<Element>()!;
            var first = new RectShape { Width = { Value = 90 }, Height = { Value = 70 } };
            ((TransformGroup)first.Transform.Value!).Children.Add(new TranslateTransform(-220, -100));
            element.AddObject(first);
            element.Objects.Move(element.Objects.IndexOf(first), element.Objects.IndexOf(shape));
        }
        editor.GetRequiredService<IEditorSelection>().Select(effect);
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 560,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        window.Show();
        HeadlessTestHelpers.Render();
        editor.Player.IsMoveMode.Value = true;
        await RenderPreview(editor);
        return (editor, view, window, effect);
    }

    private static async Task RenderPreview(EditViewModel editor)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = editor.Player.AfterRendered.Take(1).Subscribe(_ => rendered.TrySetResult());
        editor.Player.QueuePreviewRender();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Render();
    }

    private static AvaPoint Position(PlayerView view, Point local)
    {
        Point frame = view.transformHandlesOverlay.UserMatrix.Transform(local);
        return view.transformHandlesOverlay.Viewport!.ToPreview(new AvaPoint(frame.X, frame.Y));
    }

    private static AvaPoint ToWindow(PlayerView view, Window window, AvaPoint point)
        => view.image.TranslatePoint(point, window)!.Value;
}

internal sealed partial class EditingHandleTestEffect : FilterEffect, IEditingHandleProvider
{
    public EditingHandleTestEffect() => ScanProperties<EditingHandleTestEffect>();

    public IExpressionProperty<Point> Position { get; } = Property.CreateAnimatable(new Point(35, 25));

    // A bound handle leaves the write to the host instead of MoveEditingHandle.
    public bool Bound { get; set; }

    public IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time)
        => [new("point", Position.Evaluate(new EvaluationContext(time)), "Point")
        {
            Property = Bound ? Position : null,
        }];

    public void MoveEditingHandle(string id, Point position, MediaTime time) => Position.Value = position;
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource) { }
}

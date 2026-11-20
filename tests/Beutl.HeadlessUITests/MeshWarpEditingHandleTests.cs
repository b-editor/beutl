using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AvaPoint = Avalonia.Point;
using Point = Beutl.Graphics.Point;
using Rgb = (int R, int G, int B);

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class MeshWarpEditingHandleTests
{
    private const int Width = 320;
    private const int Height = 240;

    [AvaloniaTest]
    public async Task CrosshairShowsTheGridAndTheMeshEditorResetsItInOneEdit()
    {
        var effect = new MeshWarp();
        MeshWarpGrid before = effect.Mesh.Value.WithPoint(2, 2, new Point(0.61f, 0.42f))
            .WithPoint(0, 2, new Point(0.5f, 0.10f));
        effect.Mesh.Value = before;

        using var preview = await Open(effect, showInspector: true);
        FilterEffectEditorViewModel group = Flatten(preview.Inspector!.Items.Single().Properties, includeGroups: false)
            .OfType<FilterEffectEditorViewModel>().Single();
        group.IsExpanded.Value = true;
        await Task.Delay(350);
        HeadlessTestHelpers.Render(3);
        var row = preview.Window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
            .Single(row => ReferenceEquals(((FilterEffectEditorViewModel)row.DataContext!).Value.Value, effect));
        row.BringIntoView();
        HeadlessTestHelpers.Render(3);

        Click(preview.Window, row.FindControl<Button>("selectEffectButton")!);
        Assert.That(preview.Editor.GetRequiredService<IEditorSelection>().SelectedObject.Value, Is.SameAs(effect));
        preview.Window.MouseMove(new AvaPoint(5, 5));
        await preview.Render();

        IReadOnlyList<EditingHandle> handles = effect.GetEditingHandles(MediaTime.Zero);
        IReadOnlyList<EditingGuideLine> guides = effect.GetEditingGuides(MediaTime.Zero);
        Assert.That(handles, Has.Count.EqualTo(25));
        Assert.That(guides, Has.Count.EqualTo(40));
        foreach (EditingHandle handle in handles)
        {
            var hit = preview.Player.transformHandlesOverlay.HitTestEditingHandle(preview.ToPreview(handle.Position));
            Assert.That(hit?.Handle.Id, Is.EqualTo(handle.Id));
        }

        ((FilterEffectEditorViewModel)row.DataContext!).IsExpanded.Value = true;
        await Task.Delay(350);
        HeadlessTestHelpers.Render(3);
        var meshEditor = preview.Window.GetVisualDescendants().OfType<MeshWarpGridEditor>().Single();
        var vm = (MeshWarpGridEditorViewModel)meshEditor.DataContext!;
        Assert.That(vm.Summary.Value, Is.EqualTo("4 × 4"));
        Assert.That(meshEditor.GetVisualDescendants().OfType<PropertyEditorMenu>().Count(), Is.EqualTo(1));

        using WriteableBitmap frame = preview.Window.CaptureRenderedFrame()!;
        Point midpoint = (handles[6].Position + handles[7].Position) * 0.5f;
        Rgb guideColor = StrongestBlue(frame, preview.ToWindow(midpoint));
        Rgb cornerColor = Pixel(frame, preview.ToWindow(handles[0].Position));
        Rgb interiorColor = Pixel(frame, preview.ToWindow(handles[12].Position));
        Assert.Multiple(() =>
        {
            Assert.That(guideColor.B - guideColor.R, Is.GreaterThan(60));
            Assert.That(cornerColor.R - cornerColor.B, Is.GreaterThan(100));
            Assert.That(interiorColor.B - interiorColor.R, Is.GreaterThan(100));
        });

        if (Environment.GetEnvironmentVariable("BEUTL_MESH_WARP_CAPTURE") is { Length: > 0 } capture)
        {
            Directory.CreateDirectory(capture);
            frame.Save(Path.Combine(capture, "mesh-warp-editor.png"), PngBitmapEncoderOptions.Default);
        }

        int undoCount = preview.Editor.HistoryManager.UndoCount;
        Button reset = meshEditor.FindControl<Button>("resetMeshButton")!;
        reset.BringIntoView();
        HeadlessTestHelpers.Render(3);
        Click(preview.Window, reset);
        Assert.That(effect.Mesh.Value.IsIdentity(), Is.True);
        Assert.That(preview.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
        preview.Editor.HistoryManager.Undo();
        Assert.That(effect.Mesh.Value, Is.EqualTo(before));

        vm.PrepareToEditAnimation();
        var animation = (KeyFrameAnimation<MeshWarpGrid>)effect.Mesh.GetKeyFrameAnimation()!;
        Assert.That(vm.InlineProperty?.Animation, Is.SameAs(animation));
        Assert.That(animation.KeyFrames.Single().Value, Is.EqualTo(before));

        var time = MediaTime.FromSeconds(1);
        preview.Editor.GetRequiredService<IEditorClock>().Seek(time);
        await preview.Render();
        undoCount = preview.Editor.HistoryManager.UndoCount;
        Click(preview.Window, reset);
        Assert.That(animation.KeyFrames.Select(key => key.KeyTime.Value), Is.EqualTo(new[] { MediaTime.Zero, time }));
        Assert.That(animation.KeyFrames[1].Value, Is.EqualTo(MeshWarpGrid.Default));
        Assert.That(effect.Mesh.Value, Is.EqualTo(before));
        Assert.That(preview.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
        preview.Editor.HistoryManager.Undo();
        Assert.That(animation.KeyFrames.Single().Value, Is.EqualTo(before));
    }

    [AvaloniaTest]
    public async Task UnanimatedHandleDragSupportsUndoRedoAndEscape()
    {
        var effect = new MeshWarp { Rows = { Value = 2 }, Columns = { Value = 2 } };
        using var preview = await Open(effect);
        MeshWarpGrid before = effect.Mesh.Value;
        int undoCount = preview.Editor.HistoryManager.UndoCount;
        Point from = new(Width / 2, Height / 2);
        Point to = new(Width * 0.62f, Height * 0.43f);
        preview.Press(from);
        preview.Move((from + to) * 0.5f);
        preview.Move(to);
        preview.Release(to);

        MeshWarpGrid after = effect.Mesh.Value;
        AssertPoint(after.Points[4], new Point(0.62f, 0.43f));
        Assert.That(preview.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
        preview.Editor.HistoryManager.Undo();
        Assert.That(effect.Mesh.Value, Is.EqualTo(before));
        preview.Editor.HistoryManager.Redo();
        Assert.That(effect.Mesh.Value, Is.EqualTo(after));

        await preview.Render();
        preview.Press(to);
        preview.Move(from);
        AssertPoint(effect.Mesh.Value.Points[4], new Point(0.5f, 0.5f));
        preview.Escape();
        preview.Release(from);
        Assert.That(effect.Mesh.Value, Is.EqualTo(after));
        Assert.That(preview.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task AnimatedDragEditsTheCurrentFrameAndOneUndoRestoresTheTrack(bool insert)
    {
        var effect = new MeshWarp { Rows = { Value = 2 }, Columns = { Value = 2 }, Strength = { Value = 0 } };
        MeshWarpGrid baseGrid = effect.Mesh.Value;
        MeshWarpGrid start = baseGrid.WithPoint(0, new Point(-0.12f, 0)).WithPoint(4, new Point(0.42f, 0.46f));
        MeshWarpGrid end = baseGrid.WithPoint(0, new Point(-0.44f, 0)).WithPoint(4, new Point(0.54f, 0.54f));
        MediaTime firstTime = MediaTime.FromTimeSpan(TimeSpan.FromTicks(1));
        var first = new KeyFrame<MeshWarpGrid> { KeyTime = { Value = firstTime }, Value = { Value = start } };
        var last = new KeyFrame<MeshWarpGrid> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = end } };
        var animation = new KeyFrameAnimation<MeshWarpGrid> { KeyFrames = { first, last } };
        effect.Mesh.SetKeyFrameAnimation(animation);

        MediaTime elementStart = MediaTime.FromSeconds(1);
        MediaTime sceneTime = insert ? elementStart + MediaTime.FromFrames(23, new Rational(24)) : elementStart;
        using var preview = await Open(effect, start: elementStart, time: sceneTime);
        Assert.That(animation.ToLocal(sceneTime), Is.EqualTo(sceneTime - elementStart));
        MeshWarpGrid sampled = effect.Mesh.Evaluate(new EvaluationContext(sceneTime));
        Point from = effect.GetEditingHandles(sceneTime)[4].Position;
        Point to = new(Width * 0.66f, Height * 0.40f);
        int undoCount = preview.Editor.HistoryManager.UndoCount;
        preview.Press(from);
        preview.Move((from + to) * 0.5f);
        preview.Move(to);
        preview.Release(to);

        var written = (KeyFrame<MeshWarpGrid>)animation.KeyFrames[insert ? 1 : 0];
        Assert.Multiple(() =>
        {
            Assert.That(animation.KeyFrames.Count, Is.EqualTo(insert ? 3 : 2));
            Assert.That(written.KeyTime.Value, Is.EqualTo(insert ? sceneTime - elementStart : firstTime));
            AssertPoint(written.Value.Value!.Points[4], new Point(0.66f, 0.4f));
            Assert.That(written.Value.Value.Points[0], Is.EqualTo(sampled.Points[0]));
            Assert.That(last.Value.Value, Is.EqualTo(end));
            Assert.That(effect.Mesh.Value, Is.EqualTo(baseGrid));
            Assert.That(preview.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
        });

        preview.Editor.HistoryManager.Undo();
        Assert.That(animation.KeyFrames, Is.EqualTo(new IKeyFrame[] { first, last }));
        Assert.That(first.Value.Value, Is.EqualTo(start));
        Assert.That(last.Value.Value, Is.EqualTo(end));

        await preview.Render();
        preview.Press(from);
        preview.Move(to);
        Assert.That(animation.KeyFrames.Count, Is.EqualTo(insert ? 3 : 2));
        preview.Escape();
        preview.Release(to);
        Assert.That(animation.KeyFrames, Is.EqualTo(new IKeyFrame[] { first, last }));
        Assert.That(first.Value.Value, Is.EqualTo(start));
        Assert.That(preview.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
    }

    [AvaloniaTest]
    public async Task OutsideCornerTracksThePreWarpInputDuringAndAfterARenderUnderRotation()
    {
        var effect = new MeshWarp { Rows = { Value = 2 }, Columns = { Value = 2 } };
        var prefix = new TransformEffect
        {
            ApplyToTarget = { Value = false },
            Transform = { Value = new TranslateTransform(22, 15) },
        };
        using var preview = await Open(effect, rotate: true, beforeWarp: prefix);
        Point from = effect.GetEditingHandles(MediaTime.Zero)[0].Position;
        AssertPoint(from, new Point(22, 15));
        Point first = from - new Point(40, 20);
        Point second = from - new Point(80, 36);
        preview.Press(from);
        preview.Move(first);
        AssertPoint(effect.Mesh.Value.Points[0], new Point(-40f / Width, -20f / Height));

        await preview.Render();
        Assert.That(preview.Player.transformHandlesOverlay.LocalBounds.X, Is.LessThan(from.X - 20));
        AssertPoint(effect.GetEditingHandles(MediaTime.Zero)[0].Position, first);
        preview.Move(second);
        preview.Release(second);
        AssertPoint(effect.Mesh.Value.Points[0], new Point(-80f / Width, -36f / Height));

        await preview.Render();
        AssertPoint(effect.GetEditingHandles(MediaTime.Zero)[0].Position, second);
        preview.Press(second);
        preview.Move(first);
        preview.Release(first);
        AssertPoint(effect.Mesh.Value.Points[0], new Point(-40f / Width, -20f / Height));
    }

    [AvaloniaTest]
    public async Task SymbolicUpstreamEffectDoesNotJumpHandlesOnFirstDrag()
    {
        var effect = new MeshWarp { Rows = { Value = 2 }, Columns = { Value = 2 } };
        // ApplyToTarget=true (the default) produces symbolic bounds (Rect.Invalid) during recording.
        var prefix = new TransformEffect
        {
            ApplyToTarget = { Value = true },
            Transform = { Value = new TranslateTransform(22, 15) },
        };
        using var preview = await Open(effect, beforeWarp: prefix);
        Beutl.Graphics.Rect localBounds = preview.Player.transformHandlesOverlay.LocalBounds;
        Point from = effect.GetEditingHandles(MediaTime.Zero, localBounds)[0].Position;
        AssertPoint(from, new Point(22, 15));

        Point to = from - new Point(30, 12);
        preview.Press(from);
        preview.Move(to);
        preview.Release(to);
        AssertPoint(effect.Mesh.Value.Points[0], new Point(-30f / Width, -12f / Height));

        await preview.Render();
        AssertPoint(effect.GetEditingHandles(MediaTime.Zero, preview.Player.transformHandlesOverlay.LocalBounds)[0].Position, to);
    }

    [AvaloniaTest]
    public async Task SeekingBackToEarlierFrameKeepsItsOwnCapturedInputBounds()
    {
        TestGpu.RequireAnyDevice();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("mesh-warp-cached-time", frameRate: 24);
        var effect = new MeshWarp { Rows = { Value = 1 }, Columns = { Value = 1 } };
        var shape = new RectShape { Height = { Value = 120 } };
        shape.Width.SetKeyFrameAnimation(new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 160 } },
                new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 320 } },
            },
        });
        ((FilterEffectGroup)shape.FilterEffect.Value!).Children.Add(effect);
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(4));

        editor.GetRequiredService<IEditorSelection>().Select(effect);
        var player = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = player, Width = 900, Height = 640 };
        using var preview = new Preview(editor, player, window, Inspector: null);
        window.Show();
        HeadlessTestHelpers.Render();
        editor.Player.IsMoveMode.Value = true;

        MediaTime t0 = MediaTime.Zero;
        MediaTime t1 = MediaTime.FromSeconds(1);

        editor.GetRequiredService<IEditorClock>().Seek(t0);
        await preview.Render();
        AssertPoint(effect.GetEditingHandles(t0)[1].Position, new Point(160, 0));

        editor.GetRequiredService<IEditorClock>().Seek(t1);
        await preview.Render();
        AssertPoint(effect.GetEditingHandles(t1)[1].Position, new Point(320, 0));

        // Even without triggering a fresh preview render (as on a cached frame), handles at t0 keep t0's bounds.
        AssertPoint(effect.GetEditingHandles(t0)[1].Position, new Point(160, 0));
    }

    private static async Task<Preview> Open(MeshWarp effect, bool showInspector = false, bool rotate = false,
        FilterEffect? beforeWarp = null, MediaTime start = default, MediaTime time = default)
    {
        TestGpu.RequireAnyDevice();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("mesh-warp", frameRate: 24);
        var shape = new RectShape { Width = { Value = Width }, Height = { Value = Height } };
        var effects = (FilterEffectGroup)shape.FilterEffect.Value!;
        if (beforeWarp is not null) effects.Children.Add(beforeWarp);
        effects.Children.Add(effect);
        if (rotate)
        {
            var transforms = (TransformGroup)shape.Transform.Value!;
            transforms.Children.Add(new ScaleTransform(100, 90));
            transforms.Children.Add(new RotationTransform(23));
        }

        await TestShell.AddElementAsync(editor, () => shape, start, MediaTime.FromSeconds(4));
        Element element = scene.Children.Single();
        editor.GetRequiredService<IEditorSelection>().Select(showInspector ? element : effect);
        editor.GetRequiredService<IEditorClock>().Seek(time);
        var player = new PlayerView { DataContext = editor.Player };
        ElementPropertyTabViewModel? inspector = null;
        Control content = player;
        if (showInspector)
        {
            inspector = new ElementPropertyTabViewModel(editor);
            inspector.Element.Value = element;
            Grid.SetColumn(player, 1);
            content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("360,*"),
                Children = { new ElementPropertyTabView { DataContext = inspector }, player }
            };
        }

        var window = new Window { Content = content, Width = showInspector ? 1260 : 900, Height = 640 };
        var preview = new Preview(editor, player, window, inspector);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            editor.Player.IsMoveMode.Value = true;
            await preview.Render();
            return preview;
        }
        catch
        {
            preview.Dispose();
            throw;
        }
    }

    private static void Click(Window window, Control control)
    {
        AvaPoint point = control.TranslatePoint(new AvaPoint(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static Rgb StrongestBlue(WriteableBitmap frame, AvaPoint point)
    {
        Rgb strongest = default;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                Rgb pixel = Pixel(frame, new AvaPoint(point.X + x, point.Y + y));
                if (pixel.B - pixel.R > strongest.B - strongest.R) strongest = pixel;
            }
        }

        return strongest;
    }

    private static Rgb Pixel(WriteableBitmap frame, AvaPoint point)
        => HeadlessTestHelpers.ReadPixel(frame, new PixelPoint((int)point.X, (int)point.Y));

    private static void AssertPoint(Point actual, Point expected)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.01));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.01));
    }

    private sealed record Preview(EditViewModel Editor, PlayerView Player, Window Window, ElementPropertyTabViewModel? Inspector) : IDisposable
    {
        public async Task Render()
        {
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = Editor.Player.AfterRendered.Take(1).Subscribe(_ => rendered.TrySetResult());
            Editor.Player.QueuePreviewRender();
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Render(3);
        }

        public AvaPoint ToPreview(Point local)
        {
            TransformHandlesOverlay overlay = Player.transformHandlesOverlay;
            Point frame = overlay.UserMatrix.Transform(local);
            return overlay.Viewport!.ToPreview(new AvaPoint(frame.X, frame.Y));
        }

        public AvaPoint ToWindow(Point local) => Player.image.TranslatePoint(ToPreview(local), Window)!.Value;

        public void Press(Point point) => Window.MouseDown(ToWindow(point), MouseButton.Left);

        public void Move(Point point) => Window.MouseMove(ToWindow(point), RawInputModifiers.LeftMouseButton);

        public void Release(Point point) => Window.MouseUp(ToWindow(point), MouseButton.Left);

        public void Escape() => Window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);

        public void Dispose()
        {
            Window.Close();
            Inspector?.Dispose();
        }
    }
}

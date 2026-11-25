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
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AvaPoint = Avalonia.Point;
using Point = Beutl.Graphics.Point;
using RelativePoint = Beutl.Graphics.RelativePoint;
using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class CornerPinEditingHandleTests
{
    private const int Width = 320;
    private const int Height = 240;

    [AvaloniaTest]
    public async Task CrosshairSelectsTheEffectAndDrawsFourHitTestableCornersAndGuides()
    {
        var effect = new CornerPin
        {
            TopLeft = { Value = Relative(0.25f, 0.2f) },
            TopRight = { Value = Relative(0.88f, 0.08f) },
        };
        using Preview preview = await Open(effect, showInspector: true);
        FilterEffectEditorViewModel group = Flatten(preview.Inspector!.Items.Single().Properties, includeGroups: false)
            .OfType<FilterEffectEditorViewModel>().Single();
        group.IsExpanded.Value = true;
        FilterEffectListItemEditor? row = null;
        await HeadlessTestHelpers.WaitUntilAsync(() => (row = preview.Window.GetVisualDescendants()
            .OfType<FilterEffectListItemEditor>().SingleOrDefault(row => row.DataContext is FilterEffectEditorViewModel vm
                && ReferenceEquals(vm.Value.Value, effect))) is not null);
        var item = row!;
        item.BringIntoView();
        HeadlessTestHelpers.Render(3);
        HeadlessTestHelpers.Click(item.FindControl<Button>("selectEffectButton")!);
        Assert.That(preview.Editor.GetRequiredService<IEditorSelection>().SelectedObject.Value, Is.SameAs(effect));
        preview.Window.MouseMove(new AvaPoint(5, 5));
        await preview.Render();

        IReadOnlyList<EditingHandle> handles = effect.GetEditingHandles(MediaTime.Zero);
        IReadOnlyList<EditingGuideLine> guides = effect.GetEditingGuides(MediaTime.Zero);
        Assert.That(handles, Has.Count.EqualTo(4));
        Assert.That(guides, Has.Count.EqualTo(4));
        using WriteableBitmap frame = preview.Window.CaptureRenderedFrame()!;
        using (Assert.EnterMultipleScope())
        {
            foreach (EditingHandle handle in handles)
            {
                Assert.That(preview.Hit(handle.Position), Is.EqualTo(handle.Id));
                Assert.That(BlueContrast(frame, preview.ToWindow(handle.Position)), Is.GreaterThan(60));
            }
            foreach (EditingGuideLine line in guides)
            {
                Point midpoint = (line.Start + line.End) * 0.5f;
                Assert.That(BlueContrast(frame, preview.ToWindow(midpoint)), Is.GreaterThan(40));
                Assert.That(preview.Hit(midpoint), Is.Null);
            }
        }
        HeadlessCapture.SaveIfRequested(preview.Window, nameof(CornerPinEditingHandleTests), "perspective");
    }

    [AvaloniaTest]
    public async Task UnanimatedDragSupportsUndoRedoAndEscape()
    {
        var effect = new CornerPin();
        using Preview preview = await Open(effect);
        int undoCount = preview.Editor.HistoryManager.UndoCount;
        var moved = new Point(64, 24);
        preview.Press(default);
        preview.Move(moved * 0.5f);
        preview.Move(moved);
        preview.Release(moved);
        RelativePoint edited = effect.TopLeft.Value;
        AssertPoint(edited.Point, new Point(0.2f, 0.1f));
        Assert.That(preview.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));

        preview.Editor.HistoryManager.Undo();
        Assert.That(effect.TopLeft.Value, Is.EqualTo(RelativePoint.TopLeft));
        preview.Editor.HistoryManager.Redo();
        Assert.That(effect.TopLeft.Value, Is.EqualTo(edited));
        await preview.Render();

        preview.Press(moved);
        preview.Move(default);
        AssertPoint(effect.TopLeft.Value.Point, default);
        preview.Escape();
        preview.Release(default);
        Assert.That(effect.TopLeft.Value, Is.EqualTo(edited));
        Assert.That(preview.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task AnimatedDragEditsTheSnappedFrameAndUndoesBothCoordinates(bool separate, bool insert)
    {
        var effect = new CornerPin();
        MediaTime firstTime = MediaTime.FromTimeSpan(TimeSpan.FromTicks(1));
        MediaTime lastTime = MediaTime.FromSeconds(2);
        var first = new KeyFrame<RelativePoint> { KeyTime = { Value = firstTime }, Value = { Value = Relative(0.1f, 0.08f) } };
        var last = new KeyFrame<RelativePoint> { KeyTime = { Value = lastTime }, Value = { Value = Relative(0.3f, 0.2f) } };
        effect.TopLeft.SetKeyFrameAnimation(new KeyFrameAnimation<RelativePoint> { KeyFrames = { first, last } });
        IProperty[] properties = separate ? ComponentAnimations.Separate(effect.TopLeft).Components.ToArray() : [effect.TopLeft];
        var tracks = properties.Select(property => property.GetKeyFrameAnimation()!).ToArray();
        var before = tracks.Select(track => track.KeyFrames.ToArray()).ToArray();
        var beforeValues = before.Select(keys => keys.Select(key => key.Value).ToArray()).ToArray();

        MediaTime start = MediaTime.FromSeconds(1);
        MediaTime time = insert ? start + MediaTime.FromFrames(23, new Rational(24)) : start;
        using Preview preview = await Open(effect, start: start, time: time);
        Point from = effect.GetEditingHandles(time)[0].Position;
        var to = new Point(112, 36);
        int undoCount = preview.Editor.HistoryManager.UndoCount;
        preview.Press(from);
        preview.Move((from + to) * 0.5f);
        preview.Move(to);
        preview.Release(to);

        MediaTime[] expectedTimes = insert ? [firstTime, time - start, lastTime] : [firstTime, lastTime];
        foreach (IKeyFrameAnimation track in tracks)
            Assert.That(track.KeyFrames.Select(key => key.KeyTime.Value), Is.EqualTo(expectedTimes));
        AssertPoint(effect.TopLeft.Evaluate(new EvaluationContext(time)).Point, new Point(0.35f, 0.15f));
        Assert.That(effect.TopLeft.Value, Is.EqualTo(RelativePoint.TopLeft));
        Assert.That(preview.Editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));

        preview.Editor.HistoryManager.Undo();
        for (int i = 0; i < tracks.Length; i++)
        {
            Assert.That(tracks[i].KeyFrames, Is.EqualTo(before[i]));
            Assert.That(tracks[i].KeyFrames.Select(key => key.Value), Is.EqualTo(beforeValues[i]));
        }
    }

    [AvaloniaTest]
    [TestCase(RelativeUnit.Relative)]
    [TestCase(RelativeUnit.Absolute)]
    public async Task RotatedDrawableKeepsTheCornerUnderThePointerAcrossRenders(RelativeUnit unit)
    {
        var effect = new CornerPin { TopLeft = { Value = new RelativePoint(0, 0, unit) } };
        var prefix = new TransformEffect
        {
            ApplyToTarget = { Value = false },
            Transform = { Value = new TranslateTransform(22, 15) },
        };
        using Preview preview = await Open(effect, rotate: true, before: prefix);
        Point from = effect.GetEditingHandles(MediaTime.Zero)[0].Position;
        AssertPoint(from, new Point(22, 15));
        Point first = from - new Point(40, 20);
        Point second = from - new Point(80, 36);
        preview.Press(from);
        preview.Move(first);
        await preview.Render();
        AssertPoint(effect.GetEditingHandles(MediaTime.Zero)[0].Position, first);
        preview.Move(second);
        preview.Release(second);
        await preview.Render();

        Assert.That(effect.TopLeft.Value.Unit, Is.EqualTo(unit));
        AssertPoint(effect.TopLeft.Value.Point, unit == RelativeUnit.Absolute ? new Point(-80, -36) : new Point(-0.25f, -0.15f));
        AssertPoint(effect.GetEditingHandles(MediaTime.Zero)[0].Position, second);
        Assert.That(preview.Hit(second), Is.EqualTo("top-left"));
    }

    [AvaloniaTest]
    public async Task NonConvexDragLeavesTheOverlayEditableAndDraggingBackRestoresThePicture()
    {
        var effect = new CornerPin();
        using Preview preview = await Open(effect);
        var concave = new Point(190, 160);
        var convex = new Point(60, 35);
        var probe = new Point(Width / 2f, Height / 2f);

        preview.Press(default);
        preview.Move(concave);
        preview.Release(concave);
        await preview.Render();
        AssertPoint(effect.GetEditingHandles(MediaTime.Zero)[0].Position, concave);
        Assert.That(effect.GetEditingGuides(MediaTime.Zero), Has.Count.EqualTo(4));
        Assert.That(preview.Hit(concave), Is.EqualTo("top-left"));
        Assert.That((await preview.ShownAlpha(probe)).Single(), Is.Zero);
        HeadlessCapture.SaveIfRequested(preview.Window, nameof(CornerPinEditingHandleTests), "non-convex");

        preview.Press(concave);
        preview.Move(convex);
        preview.Release(convex);
        await preview.Render();
        Assert.That((await preview.ShownAlpha(probe)).Single(), Is.GreaterThan(200));
        AssertPoint(effect.GetEditingHandles(MediaTime.Zero)[0].Position, convex);
        Assert.That(preview.Hit(convex), Is.EqualTo("top-left"));
    }

    [AvaloniaTest]
    public async Task EditingStageHidesTheDownstreamTransformAndKeepsThePictureAtTheHandles()
    {
        var effect = new CornerPin { TopLeft = { Value = Relative(0.125f, 0.1f) } };
        var suffix = new TransformEffect { Transform = { Value = new TranslateTransform(80, 0) } };
        using Preview preview = await Open(effect, after: suffix);
        IEditorSelection selection = preview.Editor.GetRequiredService<IEditorSelection>();
        var message = preview.Player.FindControl<Avalonia.Controls.TextBlock>("puppetPlacementMessage")!;
        Point[] probe = [new(30, Height / 2f), new(Width + 40, Height / 2f)];
        byte[] editing = await preview.ShownAlpha(probe);
        Assert.That(preview.Editor.Player.ShownEditingStage.Value, Is.SameAs(effect));
        Assert.That(message.Text, Is.EqualTo(GraphicsStrings.CornerPin_EditingPreviewHint));
        Assert.That(editing[0], Is.GreaterThan(200));
        Assert.That(editing[1], Is.Zero);

        Point corner = effect.GetEditingHandles(MediaTime.Zero)[0].Position;
        byte[] aroundCorner = await preview.ShownAlpha(corner + new Point(10, 10), corner - new Point(8, 8));
        Assert.That(aroundCorner[0], Is.GreaterThan(200));
        Assert.That(aroundCorner[1], Is.Zero);
        Assert.That(preview.Hit(corner), Is.EqualTo("top-left"));

        selection.Select(preview.Editor.Scene.Children.Single());
        await preview.Render();
        byte[] full = await preview.ShownAlpha(probe);
        Assert.That(preview.Editor.Player.ShownEditingStage.Value, Is.Null);
        Assert.That(full[0], Is.Zero);
        Assert.That(full[1], Is.GreaterThan(200));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task SymbolicUpstreamBoundsAreCapturedForIdentityAndWarpedFrames(bool warped)
    {
        var effect = new CornerPin();
        if (warped) effect.TopLeft.Value = Relative(0.125f, 0.1f);
        var prefix = new TransformEffect { Transform = { Value = new TranslateTransform(22, 15) } };
        using Preview preview = await Open(effect, before: prefix);
        var unrelated = new Beutl.Graphics.Rect(500, 500, 10, 10);
        Point from = effect.GetEditingHandles(MediaTime.Zero, unrelated)[0].Position;
        AssertPoint(from, warped ? new Point(62, 39) : new Point(22, 15));

        Point to = from - new Point(30, 12);
        preview.Press(from);
        preview.Move(to);
        preview.Release(to);
        await preview.Render();
        AssertPoint(effect.GetEditingHandles(MediaTime.Zero, unrelated)[0].Position, to);
        AssertPoint(effect.TopLeft.Value.Point, new Point((to.X - 22) / Width, (to.Y - 15) / Height));
    }

    private static async Task<Preview> Open(CornerPin effect, bool showInspector = false, bool rotate = false,
        FilterEffect? before = null, FilterEffect? after = null, MediaTime start = default, MediaTime time = default)
    {
        TestGpu.RequireAnyDevice();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("corner-pin", frameRate: 24);
        var shape = new RectShape { Width = { Value = Width }, Height = { Value = Height } };
        var effects = (FilterEffectGroup)shape.FilterEffect.Value!;
        if (before is not null) effects.Children.Add(before);
        effects.Children.Add(effect);
        if (after is not null) effects.Children.Add(after);
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
                Children = { new ElementPropertyTabView { DataContext = inspector }, player },
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

    private static int BlueContrast(WriteableBitmap frame, AvaPoint point)
    {
        int strongest = 0;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                var (r, _, b) = HeadlessTestHelpers.ReadPixel(frame, new PixelPoint((int)point.X + x, (int)point.Y + y));
                strongest = Math.Max(strongest, b - r);
            }
        }
        return strongest;
    }

    private static RelativePoint Relative(float x, float y) => new(x, y, RelativeUnit.Relative);

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

        private Point ToFrame(Point local) => Player.transformHandlesOverlay.UserMatrix.Transform(local);

        private AvaPoint ToPreview(Point local)
        {
            Point frame = ToFrame(local);
            return Player.transformHandlesOverlay.Viewport!.ToPreview(new AvaPoint(frame.X, frame.Y));
        }

        public AvaPoint ToWindow(Point local) => Player.image.TranslatePoint(ToPreview(local), Window)!.Value;

        public string? Hit(Point local) => Player.transformHandlesOverlay.HitTestEditingHandle(ToPreview(local))?.Handle.Id;

        public async Task<byte[]> ShownAlpha(params Point[] points)
        {
            var view = (Beutl.Controls.BitmapView)Player.image;
            Ref<Beutl.Media.Bitmap>? frame = null;
            await HeadlessTestHelpers.WaitUntilAsync(() => ReferenceEquals(view.Source, Editor.Player.EditorPreviewImage.Value)
                && (frame = view.Source?.TryClone()) is not null, message: "The latest preview frame should be shown.");
            using (frame)
            using (Beutl.Media.Bitmap alpha = frame!.Value.Convert(Beutl.Media.BitmapColorType.Alpha8))
            {
                var size = Editor.Scene.FrameSize.Value;
                return points.Select(point =>
                {
                    Point p = ToFrame(point);
                    int x = (int)(p.X * alpha.Width / size.Width);
                    int y = (int)(p.Y * alpha.Height / size.Height);
                    return alpha.GetPixelSpan()[y * alpha.RowBytes + x];
                }).ToArray();
            }
        }

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

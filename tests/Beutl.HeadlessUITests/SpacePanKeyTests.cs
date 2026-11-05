using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Api.Services;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// Each surface sits in a real editor view, so a key takes the route it takes in the app.
[TestFixture]
public class SpacePanKeyTests
{
    public enum Surface { GraphEditor, PathEditorTab, PathEditorOverlay }

    [AvaloniaTest]
    public async Task A_tap_plays_and_pauses_on_release([Values] Surface surface)
    {
        await using var scope = await Scope.CreateAsync(surface);
        PlayerView player = scope.Window.GetVisualDescendants().OfType<PlayerView>().Single();

        Assert.That(player.framePanel.Focus(), Is.True);
        scope.Press();
        Assert.That(scope.IsPlaying, Is.True, "Space plays from elsewhere in the editor.");
        scope.Release();
        scope.Press();
        scope.Release();
        await scope.WaitUntilPausedAsync();

        Assert.That(scope.View.Focus(), Is.True);
        // A held key repeats, and the hold must still toggle just once.
        scope.Press();
        scope.Press();
        Assert.That(scope.IsPlaying, Is.False, "A hold can still turn into a pan.");
        scope.Release();
        Assert.That(scope.IsPlaying, Is.True);

        // The preview overlay hides during playback, and the next tap still has to reach the editor.
        scope.Press();
        scope.Release();
        await scope.WaitUntilPausedAsync();

        // The surface's own keys work again without a click, which could lose its selection.
        HeadlessTestHelpers.Settle();
        Assert.That(scope.View.IsFocused, Is.True);
    }

    [AvaloniaTest]
    public async Task Dragging_with_space_held_pans_without_touching_playback([Values] Surface surface)
    {
        await using var scope = await Scope.CreateAsync(surface);
        Assert.That(scope.View.Focus(), Is.True);
        double before = scope.PanPosition();
        Point pointer = scope.CenterOf(scope.PanSurface);

        scope.Press();
        scope.Window.MouseDown(pointer, MouseButton.Left);
        scope.Window.MouseMove(pointer + new Vector(-30, 0), RawInputModifiers.LeftMouseButton);
        scope.Window.MouseUp(pointer + new Vector(-30, 0), MouseButton.Left);
        scope.Release();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(scope.PanPosition() - before), Is.EqualTo(30).Within(0.01));
            Assert.That(scope.IsPlaying, Is.False);
        });

        // The pan is over, so the next tap is a tap again.
        scope.Press();
        scope.Release();
        Assert.That(scope.IsPlaying, Is.True);
    }

    [AvaloniaTest]
    public async Task Space_pressed_during_a_drag_is_not_a_tap([Values] Surface surface)
    {
        await using var scope = await Scope.CreateAsync(surface);
        Assert.That(scope.View.Focus(), Is.True);
        Point pointer = scope.CenterOf(scope.PanSurface);

        scope.Window.MouseDown(pointer, MouseButton.Left);
        scope.Press();
        scope.Release();
        scope.Window.MouseUp(pointer, MouseButton.Left);
        HeadlessTestHelpers.Settle();

        Assert.That(scope.IsPlaying, Is.False);
    }

    [AvaloniaTest]
    [TestCase(Surface.PathEditorTab)]
    [TestCase(Surface.PathEditorOverlay)]
    public async Task Space_pressed_during_a_point_drag_is_not_a_tap(Surface surface)
    {
        await using var scope = await Scope.CreateAsync(surface);
        Assert.That(scope.View.Focus(), Is.True);
        Thumb anchor = scope.View.FindControl<Canvas>("canvas")!.Children.OfType<Thumb>().First(thumb => !thumb.Classes.Contains("control"));
        Point point = scope.CenterOf(anchor);

        scope.Window.MouseDown(point, MouseButton.Left);
        scope.Press();
        scope.Release();
        scope.Window.MouseUp(point, MouseButton.Left);
        HeadlessTestHelpers.Settle();

        Assert.That(scope.IsPlaying, Is.False);
    }

    [AvaloniaTest]
    public async Task Space_held_across_a_click_is_not_a_tap([Values] Surface surface)
    {
        await using var scope = await Scope.CreateAsync(surface);
        Assert.That(scope.View.Focus(), Is.True);
        Point point = scope.CenterOf(scope.ClickTarget);

        scope.Press();
        scope.Window.MouseDown(point, MouseButton.Left);
        scope.Window.MouseUp(point, MouseButton.Left);
        scope.Release();
        HeadlessTestHelpers.Settle();

        Assert.That(scope.IsPlaying, Is.False);
    }

    [AvaloniaTest]
    public async Task A_tap_runs_what_the_key_map_binds_to_it([Values] Surface surface)
    {
        var keyMap = new JsonObject
        {
            [PlatformKey()] = new JsonObject { ["editor.playPause"] = new JsonArray("Shift+Space") }
        };
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(keyMap, persist: false));
        manager.Register(SceneEditorExtension.Instance);
        await using var scope = await Scope.CreateAsync(surface, manager);
        Assert.That(scope.View.Focus(), Is.True);

        scope.Press();
        scope.Release();
        HeadlessTestHelpers.Settle();
        Assert.That(scope.IsPlaying, Is.False, "Plain Space no longer plays.");

        // What counts is what the key was pressed with, not what is still held when it is released.
        scope.Press(RawInputModifiers.Shift);
        scope.Release();
        Assert.That(scope.IsPlaying, Is.True);
    }

    // The key monitor on the window pairs them, and would show a press with no release as a key still held.
    [AvaloniaTest]
    public async Task A_tap_reaches_the_window_with_every_press_released([Values] Surface surface)
    {
        await using var scope = await Scope.CreateAsync(surface);
        Assert.That(scope.View.Focus(), Is.True);
        var keys = new List<string>();
        scope.Window.AddHandler(InputElement.KeyDownEvent, (_, _) => keys.Add("press"), RoutingStrategies.Tunnel);
        scope.Window.AddHandler(InputElement.KeyUpEvent, (_, _) => keys.Add("release"), RoutingStrategies.Tunnel);

        scope.Press();
        scope.Release();

        Assert.That(keys, Is.EqualTo(new[] { "press", "release", "press", "release" }));
    }

    private static string PlatformKey()
        => OperatingSystem.IsWindows() ? OSPlatform.Windows.ToString()
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX.ToString()
            : OSPlatform.Linux.ToString();

    private sealed class Scope : IAsyncDisposable
    {
        public required EditViewModel Editor { get; init; }
        public required Window Window { get; init; }
        // Takes the focus and the key.
        public required Control View { get; init; }
        // A press here pans while Space is held.
        public required Control PanSurface { get; init; }
        // A press here with Space held is not a tap, though it need not pan.
        public required Control ClickTarget { get; init; }
        public required Func<double> PanPosition { get; init; }
        public required Action Cleanup { get; init; }

        public bool IsPlaying => Editor.Player.IsPlaying.Value;

        public static Task<Scope> CreateAsync(Surface surface, ContextCommandManager? manager = null)
            => surface == Surface.GraphEditor ? CreateGraphAsync(manager) : CreatePathAsync(surface, manager);

        private static async Task<Scope> CreateGraphAsync(ContextCommandManager? manager)
        {
            (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("space-pan-graph");
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(0.5) }, Value = { Value = 100 } });
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1.5) }, Value = { Value = 500 } });
            var shape = new RectShape();
            shape.Width.SetKeyFrameAnimation(animation);
            await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(2));

            var window = new Window { Width = 1200, Height = 800 };
            window.Content = CreateEditView(editor, manager);
            window.Show();
            HeadlessTestHelpers.Render();

            var tab = new GraphEditorTabViewModel(editor) { Element = { Value = scene.Children.Single() } };
            Assert.That(editor.DockHost.OpenToolTab(tab, editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)), Is.True);
            tab.Select(animation);
            HeadlessTestHelpers.Render(3);
            GraphEditorViewModel model = tab.SelectedAnimation.Value!;
            model.Options.Value = model.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
            HeadlessTestHelpers.Render(3);

            GraphEditorView view = window.GetVisualDescendants().OfType<GraphEditorView>()
                .Single(graph => ReferenceEquals(graph.DataContext, model));
            ScrollViewer scroll = view.FindControl<ScrollViewer>("scroll")!;
            return new Scope
            {
                Editor = editor,
                Window = window,
                View = view,
                PanSurface = scroll,
                ClickTarget = view.FindControl<Border>("RulerBar")!,
                PanPosition = () => scroll.Offset.X,
                Cleanup = window.Close
            };
        }

        private static async Task<Scope> CreatePathAsync(Surface surface, ContextCommandManager? manager)
        {
            var fixture = await PathEditorInteractionTests.Fixture.Create();
            EditViewModel editor = fixture.Editor;
            fixture.Window.Width = 1200;
            fixture.Window.Height = 800;
            fixture.Window.Content = CreateEditView(editor, manager);
            HeadlessTestHelpers.Render(3);

            PathEditorViewBase view;
            Func<double> panPosition;
            if (surface == Surface.PathEditorTab)
            {
                var tab = new PathEditorTabViewModel(editor);
                tab.FigureContext.Value = fixture.Model.FigureContext.Value;
                Assert.That(editor.DockHost.OpenToolTab(tab, editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)), Is.True);
                HeadlessTestHelpers.Render(3);
                view = fixture.Window.GetVisualDescendants().OfType<PathEditorTabView>()
                    .Single(path => ReferenceEquals(path.DataContext, tab));
                panPosition = () => view.Matrix.M31;
            }
            else
            {
                editor.Player.PathEditor.FigureContext.Value = fixture.Model.FigureContext.Value;
                HeadlessTestHelpers.Render(3);
                view = fixture.Window.GetVisualDescendants().OfType<PathEditorView>().Single();
                panPosition = () => editor.Player.FrameMatrix.Value.M31;
            }

            Canvas canvas = view.FindControl<Canvas>("canvas")!;
            return new Scope
            {
                Editor = editor,
                Window = fixture.Window,
                View = view,
                PanSurface = canvas,
                ClickTarget = canvas,
                PanPosition = panPosition,
                Cleanup = () =>
                {
                    editor.Player.PathEditor.FigureContext.Value = null;
                    fixture.Dispose();
                }
            };
        }

        // EditorHostView attaches the editor's commands this way.
        private static EditView CreateEditView(EditViewModel editor, ContextCommandManager? manager)
        {
            var editView = new EditView { DataContext = editor };
            (manager ?? TestShell.MainViewModel.ContextCommandManager).Attach(editView, SceneEditorExtension.Instance);
            return editView;
        }

        public void Press(RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.KeyPress(Key.Space, modifiers, PhysicalKey.Space, " ");

        public void Release() => Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");

        // Where a click lands on the control. Avalonia hit tests against the last rendered frame, which can
        // predate the window or the overlay, so wait for the frame that shows the control.
        public Point CenterOf(Control control)
        {
            bool Hits(Point point) => (Window.InputHitTest(point) as Visual)?.GetSelfAndVisualAncestors().Contains(control) == true;

            Point center = HeadlessTestHelpers.Center(control, Window);
            for (int attempt = 0; attempt < 20 && !Hits(center); attempt++)
            {
                HeadlessTestHelpers.Render();
                center = HeadlessTestHelpers.Center(control, Window);
            }
            Assert.That(Hits(center), Is.True, () => $"{Window.InputHitTest(center)} is at the center of {control}.");
            return center;
        }

        // The command stays disabled until the pause it ran has finished.
        public Task WaitUntilPausedAsync() => HeadlessTestHelpers.WaitUntilAsync(() => !IsPlaying && Editor.Player.PlayPause.CanExecute());

        public async ValueTask DisposeAsync()
        {
            await Editor.Player.Pause();
            Cleanup();
        }
    }
}

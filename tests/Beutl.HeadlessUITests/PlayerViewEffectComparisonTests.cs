using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Composition;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Primitives;
using Beutl.Language;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using BtlSize = Beutl.Graphics.Size;
using Point = Avalonia.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class PlayerViewEffectComparisonTests
{
    private (bool Hdr, bool Guides, bool OnionSkin, bool Checkerboard) _previewSettings;

    [SetUp]
    public void SavePreviewSettings()
    {
        var config = TestShell.Configuration.EditorConfig;
        _previewSettings = (config.UseHdrPreview.Value, config.IsPreviewGuideEnabled.Value,
            config.IsOnionSkinEnabled.Value, config.IsPreviewCheckerboardEnabled.Value);
    }

    [TearDown]
    public void RestorePreviewSettings()
    {
        var config = TestShell.Configuration.EditorConfig;
        (config.UseHdrPreview.Value, config.IsPreviewGuideEnabled.Value, config.IsOnionSkinEnabled.Value,
            config.IsPreviewCheckerboardEnabled.Value) = _previewSettings;
    }

    [AvaloniaTest]
    public async Task Pick_a_nested_effect_change_its_values_drag_the_divider_and_end_the_comparison()
    {
        using var host = await ComparisonHost.OpenAsync(capture: HeadlessCapture.IsRequested);
        var (window, editor, player, view, blur, group, hue) = host;
        int undoCount = editor.HistoryManager.UndoCount;

        await ClickAsync(window, view.effectComparisonToggle);
        Assert.That(view.effectComparisonToggle.IsChecked, Is.True);
        Assert.That(view.effectComparisonPrompt.IsEffectivelyVisible, Is.True);
        Assert.That(view.effectComparisonOverlay.IsEffectivelyVisible, Is.False);
        Button blurHeader = FindHeader(window, blur);
        window.MouseMove(HeadlessTestHelpers.Center(blurHeader, window));
        await HeadlessTestHelpers.WaitUntilAsync(() => blurHeader.IsPointerOver);
        Capture(window, "01-selection-mode");

        await ClickAsync(window, blurHeader);
        await WaitForComparison(view);
        Assert.Multiple(() =>
        {
            Assert.That(player.ComparisonTarget?.Effect, Is.SameAs(blur));
            Assert.That(view.effectComparisonPrompt.IsEffectivelyVisible, Is.False);
            Assert.That(view.beforeEffectComparisonText.Text, Is.EqualTo(string.Format(Strings.EffectComparisonBefore, GraphicsStrings.Blur)));
            Assert.That(view.afterEffectComparisonLabel.Content, Is.EqualTo(Strings.EffectComparisonAfter));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
            Assert.That(blur.IsEnabled.Value, Is.True);
        });
        Capture(window, "02-split");

        Bitmap split = player.EffectComparisonImage.Value!.Value;
        byte[] initialPixels = split.GetPixelSpan().ToArray();
        Point from = HeadlessTestHelpers.Center(view.effectComparisonDivider, window);
        Point to = from + new Avalonia.Vector(view.image.Bounds.Width * 0.22, 0);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        Assert.That(player.EffectComparisonDivider, Is.EqualTo(0.72).Within(0.02));
        Assert.That(player.EffectComparisonImage.Value!.Value.GetPixelSpan().SequenceEqual(initialPixels), Is.False);
        Capture(window, "03-divider-dragged");
        if (HeadlessCapture.IsRequested)
            window.SetRenderScaling(1);

        blur.Sigma.Value = BtlSize.Empty;
        await HeadlessTestHelpers.WaitUntilAsync(() => ComparisonMatchesNormal(player, expected: true));
        blur.Sigma.Value = new BtlSize(11, 11);
        await HeadlessTestHelpers.WaitUntilAsync(() => ComparisonMatchesNormal(player, expected: false));

        await ClickAsync(window, view.beforeEffectComparisonLabel);
        Assert.That(view.effectComparisonPrompt.IsEffectivelyVisible, Is.True);
        await ClickAsync(window, FindHeader(window, hue));
        await WaitForComparison(view);
        Assert.That(player.ComparisonTarget?.Effect, Is.SameAs(hue));
        Assert.That(player.EffectComparisonDivider, Is.EqualTo(0.72).Within(0.02));
        await ClickAsync(window, view.effectComparisonToggle);
        AssertEnded(view, player);

        view.framePanel.Focus();
        window.KeyPress(Key.C, RawInputModifiers.Alt, PhysicalKey.C, "c");
        window.KeyRelease(Key.C, RawInputModifiers.Alt, PhysicalKey.C, "c");
        HeadlessTestHelpers.Render();
        Assert.That(view.effectComparisonPrompt.IsEffectivelyVisible, Is.True);
        PressEscape(window);
        AssertEnded(view, player);

        await ClickAsync(window, view.effectComparisonToggle);
        await ClickAsync(window, FindHeader(window, blur));
        await WaitForComparison(view);
        Assert.That(player.EffectComparisonDivider, Is.EqualTo(0.5));
        var row = FindRow(window, blur);
        row.FindControl<ToggleButton>("reorderHandle")!.Focus();
        PressEscape(window);
        AssertEnded(view, player);

        await ClickAsync(window, view.effectComparisonToggle);
        await ClickAsync(window, FindHeader(window, blur));
        await WaitForComparison(view);
        group.Children.Remove(blur);
        HeadlessTestHelpers.Render();
        AssertEnded(view, player);
    }

    [AvaloniaTest]
    public async Task Puppet_pin_placement_and_effect_comparison_replace_each_other()
    {
        using var host = await ComparisonHost.OpenAsync(capture: HeadlessCapture.IsRequested);
        var (window, _, player, view, _, group, _) = host;
        var puppet = new Puppet();
        group.Children.Add(puppet);

        player.ToggleEffectComparison();
        Assert.That(((IEffectComparisonSelection)player).TryChooseComparisonEffect(puppet), Is.True);
        await WaitForComparison(view);

        using var cancel = new CancellationTokenSource();
        var placement = ((IPreviewPuppetPinEditor)player).PlacePinsAsync(puppet, _ => false, cancel.Token);
        try
        {
            await HeadlessTestHelpers.WaitUntilAsync(() => player.PointPick.Value is not null || placement.IsCompleted);
            HeadlessTestHelpers.Render();
            Assert.That(player.PointPick.Value, Is.Not.Null, "Puppet placement must have started.");
            AssertEnded(view, player);
            Capture(window, "04-puppet-placement");

            player.ToggleEffectComparison();
            Assert.That(player.IsSelectingEffectForComparison.Value, Is.True);
            Assert.That(player.PuppetPlacementEffect.Value, Is.Null);
            Assert.That(await placement.WaitAsync(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(player.PointPick.Value, Is.Null);
        }
        finally
        {
            cancel.Cancel();
            await placement.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaTest]
    public async Task Camera_tool_picks_a_3D_object_first_shown_during_comparison()
    {
        using var host = await ComparisonHost.OpenAsync(capture: HeadlessCapture.IsRequested);
        var (window, editor, player, view, blur, _, _) = host;
        var cube = new Cube3D();
        var scene = new Scene3D
        {
            RenderWidth = { Value = 640 },
            RenderHeight = { Value = 360 },
            BackgroundColor = { Value = Colors.DarkCyan },
            Objects = { cube },
        };
        await TestShell.AddElementAsync(editor, () => scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(4), layer: 1);

        ChooseBlur(player, blur);
        await WaitForComparison(view);
        using var initial = player.EffectComparisonImage.Value!.Clone();
        byte[] corner = CornerPixel(initial.Value).ToArray();
        player.ApplyTimecodeSeek(MediaTime.FromSeconds(2));
        await HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            using var split = player.EffectComparisonImage.Value?.TryClone();
            return split is not null && !CornerPixel(split.Value).SequenceEqual(corner);
        }, message: "The comparison did not show the 3D scene.");

        // Switch tools after the 3D frame has already been drawn. Click the cube just to the right of the divider.
        player.SelectPreviewTool(PreviewTool.Camera3D);
        Point inImage = view.ViewportTransform!.ToPreview(new Point(345, 180));
        Point point = view.image.TranslatePoint(inImage, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.That(scene.GizmoTarget.Value, Is.EqualTo(cube.Id.Value));
        Assert.That(view.effectComparisonOverlay.IsEffectivelyVisible, Is.True);
        HeadlessTestHelpers.Render();
        Capture(window, "05-camera-pick");

        static Span<byte> CornerPixel(Bitmap bitmap)
            => bitmap.GetRow(bitmap.Height / 10).Slice(bitmap.Width / 10 * bitmap.BytesPerPixel, bitmap.BytesPerPixel);
    }

    [AvaloniaTest]
    public async Task Playback_switches_buffered_frames_to_and_from_comparison()
    {
        using var host = await ComparisonHost.OpenAsync();
        var (_, editor, player, _, blur, _, _) = host;
        RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));

        player.IsPlaying.Value = true;
        using var buffer = new BufferedPlayer(editor, player.IsPlaying, new Rational(30), CancellationToken.None)
        {
            ReadAheadBytes = 4 * ReadAheadBudget.SnapshotBytes(editor.Renderer.Value.DeviceSize),
        };
        try
        {
            buffer.Start(new PlaybackRange(0, 30));
            await HeadlessTestHelpers.WaitUntilAsync(() => buffer.QueuedFrames >= 2, pump: false);
            using BufferedPlayer.Frame normal = await TakeAsync(buffer);
            Assert.That(normal.Comparison, Is.Null);

            player.ToggleEffectComparison();
            Assert.That(((IEffectComparisonSelection)player).TryChooseComparisonEffect(blur), Is.True);
            using BufferedPlayer.Frame split = await TakeAsync(buffer);
            Assert.That(split.Time, Is.EqualTo(normal.Time + 1));
            Assert.That(split.Comparison?.Target.Effect, Is.SameAs(blur));
            Assert.That(split.Comparison!.Before.Value.GetPixelSpan().SequenceEqual(split.Bitmap.Value.GetPixelSpan()), Is.False);

            blur.Sigma.Value = BtlSize.Empty;
            HeadlessTestHelpers.Settle();
            int lastFrame = split.Time;
            bool editShown = false;
            // Up to two coherent pairs may already have been queued or be drawing when the edit arrives.
            for (int i = 0; i < 3 && !editShown; i++)
            {
                using BufferedPlayer.Frame edited = await TakeAsync(buffer);
                Assert.That(edited.Time, Is.EqualTo(++lastFrame));
                editShown = edited.Comparison!.Before.Value.GetPixelSpan().SequenceEqual(edited.Bitmap.Value.GetPixelSpan());
            }

            Assert.That(editShown, Is.True);

            player.ToggleEffectComparison();
            using BufferedPlayer.Frame restored = await TakeAsync(buffer);
            Assert.That(restored.Time, Is.EqualTo(lastFrame + 1));
            Assert.That(restored.Comparison, Is.Null);
        }
        finally
        {
            player.IsPlaying.Value = false;
            await HeadlessTestHelpers.WaitUntilAsync(() => buffer.ProducerStopped, pump: false);
        }
    }

    [AvaloniaTest]
    public async Task Paused_comparison_presents_coherent_pairs_during_repeated_edits()
    {
        using var host = await ComparisonHost.OpenAsync();
        var (_, editor, player, view, blur, _, _) = host;
        var (fill, probe) = AddComparisonProbe(editor);
        player.IsMoveMode.Value = false;
        ChooseBlur(player, blur);
        await WaitForComparison(view);

        var gate = probe.BlockNextPair();
        try
        {
            Edit(blur, fill, 1);
            for (int revision = 1; revision < s_editColors.Length - 1; revision++)
            {
                await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                using var previous = gate;
                gate = probe.BlockNextPair();
                Edit(blur, fill, revision + 1);
                previous.Dispose();

                // The pending edit has started drawing. Its predecessor must already be visible.
                await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                using var split = player.EffectComparisonImage.Value!.Clone();
                AssertRevision(split.Value, split.Value, revision);
                Assert.That(ComparisonMatchesNormal(player, expected: false), Is.True);
            }
        }
        finally
        {
            gate.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Comparison_playback_advances_during_edits_to_every_pair()
    {
        using var host = await ComparisonHost.OpenAsync();
        var (_, editor, player, _, blur, _, _) = host;
        var (fill, probe) = AddComparisonProbe(editor);
        // Keep the paused editor playhead outside the edited elements. The producer plays those
        // elements from frame zero without starting a native audio device.
        editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(18));
        HeadlessTestHelpers.Settle();
        RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));

        player.IsPlaying.Value = true;
        ChooseBlur(player, blur);
        Edit(blur, fill, 0);
        using var buffer = new BufferedPlayer(editor, player.IsPlaying, new Rational(30), CancellationToken.None);
        var gate = probe.BlockNextPair();
        try
        {
            buffer.Start(new PlaybackRange(0, 30));
            for (int revision = 0; revision < s_editColors.Length - 1; revision++)
            {
                await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                using var previous = gate;
                gate = probe.BlockNextPair();
                Edit(blur, fill, revision + 1);
                HeadlessTestHelpers.Settle();
                previous.Dispose();

                using BufferedPlayer.Frame frame = await TakeAsync(buffer);
                Assert.That(frame.Time, Is.EqualTo(revision));
                Assert.That(frame.Comparison?.Target.Effect, Is.SameAs(blur));
                AssertRevision(frame.Comparison!.Before.Value, frame.Bitmap.Value, revision);
            }
        }
        finally
        {
            player.IsPlaying.Value = false;
            gate.Dispose();
            await HeadlessTestHelpers.WaitUntilAsync(() => buffer.ProducerStopped, pump: false);
        }
    }

    private static readonly Color[] s_editColors = [Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow, Colors.Magenta];

    private static (SolidColorBrush Fill, ComparisonRenderProbe Probe) AddComparisonProbe(EditViewModel editor)
    {
        var fill = new SolidColorBrush(Colors.White);
        var probe = new ComparisonRenderProbe();
        var element = new Element { Length = { Value = MediaTime.FromSeconds(15) }, ZIndex = { Value = 1 } };
        element.AddObject(new RectShape
        {
            Width = { Value = 640 },
            Height = { Value = 32 },
            AlignmentX = { Value = AlignmentX.Left },
            AlignmentY = { Value = AlignmentY.Top },
            Fill = { Value = fill },
        });
        element.AddObject(probe);
        editor.Scene.Children.Add(element);
        return (fill, probe);
    }

    private static void ChooseBlur(PlayerViewModel player, Blur blur)
    {
        player.ToggleEffectComparison();
        Assert.That(((IEffectComparisonSelection)player).TryChooseComparisonEffect(blur), Is.True);
    }

    private static void Edit(Blur blur, SolidColorBrush fill, int revision)
    {
        blur.Sigma.Value = new BtlSize(5 + revision, 5 + revision);
        fill.Color.Value = s_editColors[revision];
    }

    private static void AssertRevision(Bitmap before, Bitmap after, int revision)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Sample(before, 1), Is.EqualTo(s_editColors[revision]), $"Bypassed side of edit {revision}.");
            Assert.That(Sample(after, 3), Is.EqualTo(s_editColors[revision]), $"Normal side of edit {revision}.");
        });

        static Color Sample(Bitmap bitmap, int quarter)
        {
            const int bytesPerPixel = 4;
            using Bitmap bgra = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul);
            int x = bgra.Width * quarter / 4;
            int y = bgra.Height / 24;
            var pixel = bgra.GetRow(y).Slice(x * bytesPerPixel, bytesPerPixel);
            return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }
    }

    private static async Task<BufferedPlayer.Frame> TakeAsync(BufferedPlayer buffer)
    {
        await HeadlessTestHelpers.WaitUntilAsync(() => buffer.QueuedFrames > 0 || buffer.ProducerStopped, pump: false,
            message: "Playback did not queue a frame.");
        Assert.That(buffer.Failure, Is.Null);
        Assert.That(buffer.TryDequeue(out BufferedPlayer.Frame frame), Is.True);
        return frame;
    }

    private static FilterEffectListItemEditor FindRow(Window window, FilterEffect effect)
        => window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
            .Single(row => row.DataContext is FilterEffectEditorViewModel vm && ReferenceEquals(vm.Value.Value, effect));

    private static Button FindHeader(Window window, FilterEffect effect)
        => FindRow(window, effect).FindControl<Button>("comparisonHeader")!;

    private static bool ComparisonMatchesNormal(PlayerViewModel player, bool expected)
    {
        using var comparison = player.EffectComparisonImage.Value?.TryClone();
        using var normal = player.PreviewImage.Value?.TryClone();
        return comparison is not null && normal is not null
            && comparison.Value.GetPixelSpan().SequenceEqual(normal.Value.GetPixelSpan()) == expected;
    }

    private static Task WaitForComparison(PlayerView view)
        => HeadlessTestHelpers.WaitUntilAsync(() => view.effectComparisonOverlay.IsEffectivelyVisible);

    private static async Task ClickAsync(Window window, Control control)
    {
        await HeadlessTestHelpers.WaitUntilAsync(() => control.IsEffectivelyVisible && control.Bounds.Width > 0);
        control.BringIntoView();
        Point point = default;
        await HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            point = HeadlessTestHelpers.Center(control, window);
            return window.InputHitTest(point) is Visual hit && (hit == control || control.IsVisualAncestorOf(hit));
        }, message: $"Could not reach {control.Name}.");
        window.MouseMove(point);
        HeadlessTestHelpers.Click(control);
    }

    private static void PressEscape(Window window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        HeadlessTestHelpers.Render(3);
    }

    private static void AssertEnded(PlayerView view, PlayerViewModel player)
    {
        Assert.Multiple(() =>
        {
            Assert.That(view.effectComparisonToggle.IsChecked, Is.False);
            Assert.That(view.effectComparisonPrompt.IsEffectivelyVisible, Is.False);
            Assert.That(view.effectComparisonOverlay.IsEffectivelyVisible, Is.False);
            Assert.That(player.EffectComparisonImage.Value, Is.Null);
            Assert.That(player.ComparisonTarget, Is.Null);
        });
    }

    private static void Capture(Window window, string name)
    {
        HeadlessCapture.SaveIfRequested(window, nameof(PlayerViewEffectComparisonTests), name);
    }

    private sealed record ComparisonHost(
        Window Window,
        EditViewModel Editor,
        PlayerViewModel Player,
        PlayerView View,
        Blur Blur,
        FilterEffectGroup Nested,
        HueRotate Hue) : IDisposable
    {
        public static async Task<ComparisonHost> OpenAsync(bool capture = false)
        {
            TestGpu.RequireAnyDevice();
            (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
                "effect-comparison", 640, 360, duration: MediaTime.FromSeconds(20));
            var config = TestShell.Configuration.EditorConfig;
            config.UseHdrPreview.Value = false;
            config.IsOnionSkinEnabled.Value = false;
            config.IsPreviewGuideEnabled.Value = false;
            config.IsPreviewCheckerboardEnabled.Value = false;

            var blur = new Blur { Sigma = { Value = new BtlSize(11, 11) } };
            var hue = new HueRotate { Angle = { Value = 12 } };
            var nested = new FilterEffectGroup { Children = { blur } };
            var filters = new FilterEffectGroup { Children = { nested, hue } };
            var bars = new DrawableGroup { FilterEffect = { Value = filters } };
            Color[] palette = [Colors.Turquoise, Colors.CornflowerBlue, Colors.Orchid, Colors.Coral];
            for (int i = 0; i < 8; i++)
            {
                int x = 41 + (i * 71);
                int y = 82 + ((i % 4) * 16);
                bars.Children.Add(Rectangle(54, 192, x, y, palette[i % palette.Length]));
                bars.Children.Add(Rectangle(54, 10, x, y - 19, Colors.White));
            }

            await TestShell.AddElementAsync(editor, () => bars, MediaTime.Zero, MediaTime.FromSeconds(15));
            editor.GetRequiredService<IEditorSelection>().Select(scene.Children.Single());
            var edit = new EditView { DataContext = editor };
            TestShell.MainViewModel.ContextCommandManager.Attach(edit, SceneEditorExtension.Instance);
            var window = new Window { Content = edit, Width = 1300, Height = 780, RequestedThemeVariant = ThemeVariant.Light };
            try
            {
                window.Show();
                if (capture)
                    window.SetRenderScaling(2);
                HeadlessTestHelpers.Render(3);
                var view = window.GetVisualDescendants().OfType<PlayerView>().Single();
                var filterEditor = window.GetVisualDescendants().OfType<FilterEffectEditor>()
                    .Single(control => control.DataContext is FilterEffectEditorViewModel vm && ReferenceEquals(vm.Value.Value, filters));
                ((FilterEffectEditorViewModel)filterEditor.DataContext!).IsExpanded.Value = true;
                HeadlessTestHelpers.Render(3);
                ((FilterEffectEditorViewModel)FindRow(window, nested).DataContext!).IsExpanded.Value = true;
                await HeadlessTestHelpers.WaitUntilAsync(() =>
                    view.image.Bounds.Width > 0 && window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
                        .Any(row => row.IsEffectivelyVisible && row.DataContext is FilterEffectEditorViewModel vm
                            && ReferenceEquals(vm.Value.Value, blur)));
                var scroll = window.GetVisualDescendants().OfType<ElementPropertyTabView>().Single()
                    .FindControl<ScrollViewer>("scrollViewer")!;
                double blurY = FindRow(window, blur).TranslatePoint(default, scroll)!.Value.Y;
                scroll.Offset = new Avalonia.Vector(0, scroll.Offset.Y + blurY - (scroll.Bounds.Height * 0.65));
                HeadlessTestHelpers.Render(3);
                return new(window, editor, editor.Player, view, blur, nested, hue);
            }
            catch
            {
                window.Close();
                throw;
            }
        }

        public void Dispose() => Window.Close();

        private static RectShape Rectangle(int width, int height, int x, int y, Color color)
            => new()
            {
                Width = { Value = width },
                Height = { Value = height },
                AlignmentX = { Value = AlignmentX.Left },
                AlignmentY = { Value = AlignmentY.Top },
                Transform = { Value = new TranslateTransform(x, y) },
                Fill = { Value = new SolidColorBrush(color) },
            };
    }
}

// Pauses the bypass reconciliation after the marker has been reconciled. The probe is serialized
// with the scene, so its Id identifies both the live drawable and each captured copy.
internal sealed partial class ComparisonRenderProbe : Drawable
{
    private static PairGate? s_gate;

    internal PairGate BlockNextPair()
    {
        var gate = new PairGate(Id.Value);
        Assert.That(Interlocked.CompareExchange(ref s_gate, gate, null), Is.Null);
        return gate;
    }

    internal sealed class PairGate(Guid probeId) : IDisposable
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid ProbeId { get; } = probeId;
        public Task Entered => _entered.Task;

        public void WaitForEdit()
        {
            _entered.TrySetResult();
            _release.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Interlocked.CompareExchange(ref s_gate, null, this);
            _release.TrySetResult();
        }
    }

    public new partial class Resource
    {
        partial void PostReconcile(ComparisonRenderProbe obj, CompositionContext context)
        {
            if (context.Conditions.BypassedFilterEffect is not null && Volatile.Read(ref s_gate) is { } gate
                && gate.ProbeId == obj.Id.Value && ReferenceEquals(Interlocked.CompareExchange(ref s_gate, null, gate), gate))
                gate.WaitForEdit();
        }

        protected override BtlSize MeasureCore(BtlSize availableSize) => BtlSize.Empty;

        protected override void OnDraw(GraphicsContext2D context)
        {
        }
    }
}

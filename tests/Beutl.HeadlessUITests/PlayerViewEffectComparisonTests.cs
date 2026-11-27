using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
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
            using BufferedPlayer.Frame edited = await TakeAsync(buffer);
            Assert.That(edited.Time, Is.EqualTo(split.Time + 1));
            Assert.That(edited.Comparison!.Before.Value.GetPixelSpan().SequenceEqual(edited.Bitmap.Value.GetPixelSpan()), Is.True);

            player.ToggleEffectComparison();
            using BufferedPlayer.Frame restored = await TakeAsync(buffer);
            Assert.That(restored.Time, Is.EqualTo(edited.Time + 1));
            Assert.That(restored.Comparison, Is.Null);
        }
        finally
        {
            player.IsPlaying.Value = false;
            await HeadlessTestHelpers.WaitUntilAsync(() => buffer.ProducerStopped, pump: false);
        }

        static async Task<BufferedPlayer.Frame> TakeAsync(BufferedPlayer buffer)
        {
            await HeadlessTestHelpers.WaitUntilAsync(() => buffer.QueuedFrames > 0 || buffer.ProducerStopped, pump: false);
            Assert.That(buffer.Failure, Is.Null);
            Assert.That(buffer.TryDequeue(out BufferedPlayer.Frame frame), Is.True);
            return frame;
        }
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

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public class PreviewWorkingScaleIndicatorTests
{
    private static readonly MediaTime s_denseFrame = MediaTime.FromSeconds(2);

    [AvaloniaTest]
    [TestCase(RenderScale.Full)]
    [TestCase(RenderScale.Half)]
    public async Task FixedQualityShowsMarkerOnClampedFramesIncludingCacheHits(RenderScale quality)
    {
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("preview-density", 320, 180,
            duration: MediaTime.FromSeconds(4));
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window
        {
            Content = view,
            Width = 720,
            Height = 480,
            RequestedThemeVariant = ThemeVariant.Dark,
        };
        bool previousOverlayPopups = TestAppBuilder.HeadlessOptions.OverlayPopups;
        try
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = true;
            window.Show();
            await SelectQualityAsync(view, editor, quality);
            Assert.That(editor.Renderer.Value.OutputScale, Is.EqualTo(quality == RenderScale.Full ? 1f : 0.5f));
            await TestShell.AddElementAsync(editor, () => CreateMosaic(320, 180, 100),
                MediaTime.Zero, s_denseFrame);
            await TestShell.AddElementAsync(editor, () => CreateMosaic(1280, 720, 25),
                s_denseFrame, MediaTime.FromSeconds(2));
            var indicator = view.FindControl<Border>("previewWorkingScaleIndicator")!;
            FrameCacheManager cache = editor.FrameCacheManager.Value;
            cache.IsEnabled = true;
            cache.Clear();
            editor.Player.QueuePreviewRender();

            await ExpectMarkerAsync(editor.Player, indicator, visible: false, () => !cache.CalculateBlocks(0, 1).IsEmpty);
            editor.Player.ApplyTimecodeSeek(s_denseFrame);
            await ExpectMarkerAsync(editor.Player, indicator, visible: true);
            int denseFrame = (int)s_denseFrame.ToFrame(editor.Player.FrameRate.Value, MidpointRounding.AwayFromZero);
            Assert.That(cache.TryGetWithDiagnostics(denseFrame, out var bitmap, out var diagnostics), Is.True);
            bitmap?.Dispose();
            Assert.That(diagnostics.IsWorkingScaleClamped, Is.True);

            if (quality == RenderScale.Full)
            {
                HeadlessTestHelpers.Render();
                HeadlessCapture.SaveIfRequested(window, nameof(PreviewWorkingScaleIndicatorTests), "marker");

                window.MouseMove(HeadlessTestHelpers.Center(indicator, window));
                await HeadlessTestHelpers.WaitUntilAsync(() => ToolTip.GetIsOpen(indicator));
                var tip = (ToolTip)window.OpenedPopups.Single().Child!;
                Assert.That(((TextBlock)tip.Content!).Text, Is.EqualTo(MessageStrings.PreviewWorkingScaleClamped));
                HeadlessTestHelpers.Render(3);
                HeadlessCapture.SaveIfRequested(window, nameof(PreviewWorkingScaleIndicatorTests), "tooltip");
            }

            editor.Player.ApplyTimecodeSeek(MediaTime.Zero);
            await ExpectMarkerAsync(editor.Player, indicator, visible: false);
            editor.Player.ApplyTimecodeSeek(s_denseFrame);
            await ExpectMarkerAsync(editor.Player, indicator, visible: true);
        }
        finally
        {
            window.Close();
            TestAppBuilder.HeadlessOptions.OverlayPopups = previousOverlayPopups;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task FitToPreviewerPanelResizeCrossesTheWorkingScaleCeiling()
    {
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("fit-preview-density", 640, 360,
            duration: MediaTime.FromSeconds(4));
        var view = new PlayerView { DataContext = editor.Player };
        var panel = new Border { Child = view, Width = 820, HorizontalAlignment = HorizontalAlignment.Left };
        var window = new Window { Content = panel, Width = 900, Height = 560 };
        try
        {
            window.Show();
            await SelectQualityAsync(view, editor, RenderScale.FitToPreviewer);
            // The image has a supply density of 1.6 after its scale transform, crossing the ceiling at 80% fit.
            await TestShell.AddElementAsync(editor, () => CreateMosaic(1024, 576, 62.5f),
                MediaTime.Zero, MediaTime.FromSeconds(4));
            var indicator = view.FindControl<Border>("previewWorkingScaleIndicator")!;
            FrameCacheManager cache = editor.FrameCacheManager.Value;
            cache.IsEnabled = true;
            cache.Clear();
            editor.Player.QueuePreviewRender();
            await ExpectMarkerAsync(editor.Player, indicator, visible: false,
                () => editor.Renderer.Value.OutputScale > 0.8f && !cache.CalculateBlocks(0, 1).IsEmpty);

            panel.Width = 420;
            HeadlessTestHelpers.Render(3);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Renderer.Value.OutputScale < 0.8f,
                message: $"The narrow panel did not lower the Fit scale (panel {panel.Bounds.Width}, "
                    + $"preview {editor.PreviewSurfaceSize.Value}, scale {editor.Renderer.Value.OutputScale}).");
            await ExpectMarkerAsync(editor.Player, indicator, visible: true);

            panel.Width = 820;
            HeadlessTestHelpers.Render(3);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Renderer.Value.OutputScale > 0.8f,
                message: "The wider panel did not raise the Fit scale.");
            await ExpectMarkerAsync(editor.Player, indicator, visible: false);
        }
        finally
        {
            window.Close();
            await TestReset.ResetShellAsync();
        }
    }

    private static Task SelectQualityAsync(PlayerView view, EditViewModel editor, RenderScale quality)
    {
        HeadlessTestHelpers.Render();
        view.resolutionComboBox.SelectedItem = quality;
        return HeadlessTestHelpers.WaitUntilAsync(() => editor.PreviewScale.Value == quality);
    }

    private static async Task ExpectMarkerAsync(PlayerViewModel player, Border indicator, bool visible,
        Func<bool>? frameReady = null)
    {
        await HeadlessTestHelpers.WaitUntilAsync(() => player.PreviewRenderError.Value is not null
            || (indicator.IsVisible == visible && (frameReady?.Invoke() ?? true)),
            message: $"The preview marker did not become {(visible ? "visible" : "hidden")}.");
        Assert.That(player.PreviewRenderError.Value, Is.Null);
    }

    private static DrawableGroup CreateMosaic(int width, int height, float scale)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var gradient = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
            [SKColor.Parse("#0A2463"), SKColor.Parse("#7556D8"), SKColor.Parse("#17C3B2")],
            null, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = gradient };
        canvas.DrawPaint(paint);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        var source = new ImageSource();
        source.ReadFrom(UriHelper.CreateBase64DataUri("image/png", png.ToArray()));
        var group = new DrawableGroup();
        group.Children.Add(new SourceImage
        {
            Source = { Value = source },
            Transform = { Value = new ScaleTransform(scale, scale) },
        });
        group.FilterEffect.Value = new MosaicEffect { TileSize = { Value = new Size(16, 16) } };
        return group;
    }
}

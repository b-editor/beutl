using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Primitives;
using Beutl.Language;
using Beutl.Media;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Moq;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public class Scene3DRenderWarningTests
{
    private const int RenderWidth = 4096;
    private const int RenderHeight = 3072;
    private const int DeviceLimit = 2048;

    [AvaloniaTest]
    [TestCase(false, "vulkan-unavailable")]
    [TestCase(true, "over-device-limit")]
    public async Task PreviewShowsTheReasonAndRecovers_AndFrameExportRejectsMissing3D(
        bool supports3D, string captureName)
    {
        await TestReset.ResetShellAsync();
        var device = new Mock<IGraphicsContext>();
        device.SetupGet(static context => context.Supports3DRendering).Returns(true);
        device.SetupGet(static context => context.MaxAttachmentDimension).Returns(DeviceLimit);
        // The real renderer runs on CPU surfaces, with a reported 3D budget for the oversize case.
        var (previousGraphics, previousAllocationContext) = RenderThread.Dispatcher.Invoke(() =>
        {
            var graphics = new InstalledGraphics(supports3D ? device.Object : null, null, null, !supports3D);
            return (GraphicsContextFactory.ExchangeInstalledGraphics(graphics),
                RenderTarget.ExchangeAllocationContext(static () => null));
        });

        Window? window = null;
        try
        {
            var (_, _, editor) = await TestShell.OpenNewSceneAsync(captureName, 320, 240,
                duration: MediaTime.FromSeconds(4), reset: false);
            PlayerViewModel player = editor.Player;
            editor.FrameCacheManager.Value.IsEnabled = true;
            var view = new PlayerView { DataContext = player };
            window = new Window { Content = view, Width = 640, Height = 480 };
            window.Show();

            await TestShell.AddElementAsync(editor, static () => new RectShape
            {
                Width = { Value = 180 },
                Height = { Value = 120 },
                Fill = { Value = new SolidColorBrush(Colors.CornflowerBlue) },
            }, MediaTime.Zero, MediaTime.FromSeconds(4));
            var scene = new Scene3D { RenderWidth = { Value = RenderWidth }, RenderHeight = { Value = RenderHeight } };
            scene.Objects.Add(new Cube3D());
            await TestShell.AddElementAsync(editor, () => scene, MediaTime.Zero, MediaTime.FromSeconds(2), layer: 1);

            string expected = supports3D
                ? string.Format(MessageStrings.Scene3DExtentLimitExceeded, RenderWidth, RenderHeight, DeviceLimit)
                : MessageStrings.Scene3DBackendUnavailable;
            Border warning = view.FindControl<Border>("previewRenderWarning")!;
            TextBlock message = view.FindControl<TextBlock>("previewRenderWarningMessage")!;
            editor.FrameCacheManager.Value.Clear();
            player.QueuePreviewRender();
            RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));

            Assert.That(player.PreviewRenderError.Value, Is.Null);
            Assert.That(player.PreviewRenderWarning.Value, Is.EqualTo(expected));
            Assert.That(message.Text, Is.EqualTo(expected));
            Assert.That(warning.IsVisible, Is.True);
            Assert.That(warning.IsHitTestVisible, Is.False);
            Assert.That(AutomationProperties.GetLiveSetting(message), Is.EqualTo(AutomationLiveSetting.Polite));
            Assert.That(editor.FrameCacheManager.Value.TryGet(0, out var bitmap, out string? cachedWarning), Is.True);
            bitmap?.Dispose();
            Assert.That(cachedWarning, Is.EqualTo(expected));

            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(warning, nameof(Scene3DRenderWarningTests), captureName);

            InvalidOperationException? exportFailure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using var frame = await player.DrawFrameAtFullScale();
            });
            Assert.That(exportFailure!.Message, Is.EqualTo(expected));

            player.ApplyTimecodeSeek(MediaTime.FromSeconds(3));
            await HeadlessTestHelpers.WaitUntilAsync(() => !warning.IsVisible, TimeSpan.FromSeconds(10));
            player.ApplyTimecodeSeek(MediaTime.Zero);
            await HeadlessTestHelpers.WaitUntilAsync(() => message.Text == expected && warning.IsVisible,
                TimeSpan.FromSeconds(10));
        }
        finally
        {
            window?.Close();
            try
            {
                await TestReset.ResetShellAsync();
            }
            finally
            {
                RenderThread.Dispatcher.Invoke(() =>
                {
                    RenderTarget.ExchangeAllocationContext(previousAllocationContext);
                    GraphicsContextFactory.ExchangeInstalledGraphics(previousGraphics);
                });
            }
        }
    }
}

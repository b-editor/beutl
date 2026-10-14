using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;

using Beutl.Configuration;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Components.PreviewSettingsTab.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// Playback renders the whole run in one render-thread job, so anything the UI thread waits for on the render thread
// waits until playback stops or reaches the end of the scene. Changing the node cache settings used to do that.
[NonParallelizable]
[TestFixture]
public class NodeCacheSettingsPlaybackTests
{
    [AvaloniaTest]
    public async Task ChangingTheNodeCache_DuringPlayback_DoesNotWaitForTheRenderThread()
    {
        TestGpu.RequireAnyDevice();
        // Much longer than the producer's read-ahead and the bound below, so the producer holds the render thread
        // for the whole test.
        EditViewModel editor = await OpenEditor(TimeSpan.FromMinutes(2));
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool enabled = config.IsNodeCacheEnabled;

        try
        {
            editor.Player.Play();
            await HeadlessTestHelpers.WaitUntilAsync(
                () => clock.CurrentTime.Value >= TimeSpan.FromSeconds(0.2), TimeSpan.FromMinutes(2), pump: false);

            // Queued behind the producer, so it only runs once playback lets go of the render thread.
            Task renderThreadReached = RenderThread.Dispatcher.InvokeAsync(static () => { });
            var stopwatch = Stopwatch.StartNew();
            config.IsNodeCacheEnabled = !enabled;
            stopwatch.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(renderThreadReached.IsCompleted, Is.False,
                    "the change must return while playback still holds the render thread");
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMinutes(1)));
                Assert.That(editor.Player.IsPlaying.Value, Is.True);
            });

            // The renderer takes the new options once playback releases the render thread.
            await editor.Player.Pause();
            await HeadlessTestHelpers.WaitUntilAsync(
                () => editor.Renderer.Value.CacheOptions.IsEnabled == !enabled, TimeSpan.FromMinutes(2), pump: false);
        }
        finally
        {
            await editor.Player.Pause();
            config.IsNodeCacheEnabled = enabled;
        }
    }

    [AvaloniaTest]
    public async Task PreviewSettings_DisableTheNodeCacheControls_WhilePlaying()
    {
        EditViewModel editor = await OpenEditor(TimeSpan.FromSeconds(10));
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool enabled = config.IsNodeCacheEnabled;
        // With the cache on, the pixel limits are enabled until playback starts.
        config.IsNodeCacheEnabled = true;
        var viewModel = new PreviewSettingsTabViewModel(editor);
        var view = new PreviewSettingsTabView { DataContext = viewModel };
        var window = new Window
        {
            Content = view,
            Width = 360,
            Height = 960,
            RequestedThemeVariant = ThemeVariant.Dark,
        };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(view.NodeCacheToggle.IsEffectivelyEnabled, Is.True);
                Assert.That(view.NodeCacheLimits.IsEffectivelyEnabled, Is.True);
            });

            // Mark the player as playing without starting playback, as the render quality option is tested.
            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(view.NodeCacheToggle.IsEffectivelyEnabled, Is.False);
                Assert.That(view.NodeCacheLimits.IsEffectivelyEnabled, Is.False);
            });

            editor.Player.IsPlaying.Value = false;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(view.NodeCacheToggle.IsEffectivelyEnabled, Is.True);
                Assert.That(view.NodeCacheLimits.IsEffectivelyEnabled, Is.True);
            });
        }
        finally
        {
            editor.Player.IsPlaying.Value = false;
            window.Close();
            viewModel.Dispose();
            config.IsNodeCacheEnabled = enabled;
        }
    }

    private static async Task<EditViewModel> OpenEditor(TimeSpan duration)
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("node-cache-playback", 320, 240, duration: duration);

        // Let the preview render queued by opening the scene finish first.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        RenderThread.Dispatcher.Invoke(static () => { }, ct: timeout.Token);
        HeadlessTestHelpers.Settle();
        return editor;
    }
}

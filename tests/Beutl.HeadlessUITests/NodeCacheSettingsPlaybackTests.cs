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

// A render-thread job can run long, as a heavy playback frame does, so changing the node cache settings must not wait
// for the render thread. It used to wait for the whole playback, which rendered in one job.
[NonParallelizable]
[TestFixture]
public class NodeCacheSettingsPlaybackTests
{
    [AvaloniaTest]
    public async Task ChangingTheNodeCache_DuringPlayback_DoesNotWaitForTheRenderThread()
    {
        TestGpu.RequireAnyDevice();
        // Long enough that playback still runs when the setting changes.
        EditViewModel editor = await OpenEditor(MediaTime.FromSeconds(120));
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool enabled = config.IsNodeCacheEnabled;
        using var release = new CancellationTokenSource();
        Task? blocked = null;

        try
        {
            editor.Player.Play();
            await HeadlessTestHelpers.WaitUntilAsync(
                () => clock.CurrentTime.Value >= MediaTime.FromSeconds(0.2), TimeSpan.FromSeconds(120), pump: false);

            // A job that holds the render thread between two playback frames, until the change has returned.
            blocked = await BlockRenderThreadAsync(release);
            var stopwatch = Stopwatch.StartNew();
            config.IsNodeCacheEnabled = !enabled;
            stopwatch.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(blocked.IsCompleted, Is.False,
                    "the change must return while the render thread is busy");
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMinutes(1)));
                Assert.That(editor.Player.IsPlaying.Value, Is.True);
            });

            // The renderer takes the new options once the render thread is free.
            release.Cancel();
            await blocked.WaitAsync(TimeSpan.FromSeconds(10));
            await editor.Player.Pause();
            await HeadlessTestHelpers.WaitUntilAsync(
                () => editor.Renderer.Value.CacheOptions.IsEnabled == !enabled, TimeSpan.FromSeconds(120), pump: false);
        }
        finally
        {
            release.Cancel();
            if (blocked is not null)
                await blocked.WaitAsync(TimeSpan.FromSeconds(10));
            await editor.Player.Pause();
            config.IsNodeCacheEnabled = enabled;
        }
    }

    private static async Task<Task> BlockRenderThreadAsync(CancellationTokenSource release)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task job = RenderThread.Dispatcher.InvokeAsync(() =>
        {
            entered.TrySetResult();
            release.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(60));
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return job;
    }

    [AvaloniaTest]
    public async Task PreviewSettings_DisableTheNodeCacheControls_WhilePlaying()
    {
        EditViewModel editor = await OpenEditor(MediaTime.FromSeconds(10));
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

    private static async Task<EditViewModel> OpenEditor(MediaTime duration)
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("node-cache-playback", 320, 240, duration: duration);

        // Let the preview render queued by opening the scene finish first.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        RenderThread.Dispatcher.Invoke(static () => { }, ct: timeout.Token);
        HeadlessTestHelpers.Settle();
        return editor;
    }
}

using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using Beutl.Configuration;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Components.PreviewSettingsTab.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;
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
            await WaitUntilAsync(() => clock.CurrentTime.Value >= TimeSpan.FromSeconds(0.2), TimeSpan.FromMinutes(2));

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
            await WaitUntilAsync(
                () => editor.Renderer.Value.CacheOptions.IsEnabled == !enabled, TimeSpan.FromMinutes(2));
        }
        finally
        {
            await editor.Player.Pause();
            config.IsNodeCacheEnabled = enabled;
        }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task PreviewSettings_DisableTheNodeCacheControls_WhilePlaying(bool light)
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
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
        };
        string theme = light ? "light" : "dark";

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(view.NodeCacheToggle.IsEffectivelyEnabled, Is.True);
                Assert.That(view.NodeCacheLimits.IsEffectivelyEnabled, Is.True);
            });
            Capture(window, $"stopped-{theme}");

            // Mark the player as playing without starting playback, as the render quality option is tested.
            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(view.NodeCacheToggle.IsEffectivelyEnabled, Is.False);
                Assert.That(view.NodeCacheLimits.IsEffectivelyEnabled, Is.False);
            });
            Capture(window, $"playing-{theme}");

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
        await TestReset.ResetShellAsync();
        string name = $"node-cache-playback-{Guid.NewGuid():N}";
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(320, 240, 30, 44100, name, directory))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();
        scene.Duration = duration;
        TestShell.Editor.ActivateTabItem(scene);

        // Let the preview render queued by opening the scene finish first.
        HeadlessTestHelpers.Settle();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        RenderThread.Dispatcher.Invoke(static () => { }, ct: timeout.Token);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_NODE_CACHE_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        // Let the brush transitions of the changed controls finish.
        HeadlessTestHelpers.Render(30);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
                Assert.Fail($"Condition was not met within {timeout}.");

            await Task.Delay(10);
        }
    }
}

using System.Diagnostics;
using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// Regression coverage for abandoned playback tasks.
[TestFixture]
public class PlayerViewModelDisposalTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static Element AddRectangle(EditViewModel editor)
    {
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(4),
            Layer: 0,
            EngineObjectFactory: () => new RectShape()));
        HeadlessTestHelpers.Settle();
        return editor.Scene.Children[^1];
    }

    [AvaloniaTest]
    public async Task Publishing_audio_after_dispose_is_safe_for_abandoned_playback_tasks()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("player-dispose");
        PlayerViewModel player = editor.Player;

        // Use the normal editor-close path.
        await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!);
        HeadlessTestHelpers.Settle();

        // A delayed audio task may still call this after disposal.
        using var pcm = new Pcm<Stereo32BitFloat>(44100, 64);
        Assert.DoesNotThrow(() => player.PublishAudioSnapshot(pcm, TimeSpan.Zero));

        // A fresh subscriber should observe completion after disposal.
        bool completed = false;
        ((IPreviewPlayer)player).AudioFramePushed.Subscribe(_ => { }, () => completed = true);
        Assert.That(completed, Is.True,
            "AudioFramePushed must be completed, not disposed, after DisposeAsync");
    }

    // GPU-gated: the playback loop renders preview frames through SceneRenderer.
    [AvaloniaTest]
    public async Task Disposing_the_editor_while_the_playback_timer_is_active_stops_cleanly()
    {
        GpuTestGate.EnsureAvailable();
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("player-timer-dispose");
        PlayerViewModel player = editor.Player;
        AddRectangle(editor);
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();

        var frameApplied = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPreviewInvalidated(object? sender, EventArgs e) => frameApplied.TrySetResult(true);
        player.PreviewInvalidated += OnPreviewInvalidated;

        // Capture backend errors for diagnostics.
        var notifications = new CaptureNotificationHandler();
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        NotificationService.Handler = notifications;

        try
        {
            player.Play();

            // Wait until playback has advanced before disposing the editor.
            await frameApplied.Task.WaitAsync(TimeSpan.FromSeconds(10));
            bool clockAdvanced = await WaitUntilAsync(
                () => clock.CurrentTime.Value > TimeSpan.Zero, TimeSpan.FromSeconds(5));

            Assert.That(clockAdvanced, Is.True,
                $"the playback timer must advance the editor clock; {Diagnostics()}");
            Assert.That(player.IsPlaying.Value, Is.True,
                $"playback must still be running before the mid-playback disposal; {Diagnostics()}");

            // Dispose while the playback timer is active.
            await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(player.IsPlaying.Value, Is.False);
                Assert.That(player.PreviewImage.Value, Is.Null,
                    "teardown must leave no preview frame behind");
            });
        }
        finally
        {
            NotificationService.Handler = previousHandler;
            player.PreviewInvalidated -= OnPreviewInvalidated;
        }

        string Diagnostics() =>
            $"renderError={player.PreviewRenderError.Value}, clock={clock.CurrentTime.Value}, " +
            $"notifications=[{string.Join(" | ", notifications.All.Select(static n => n.Message))}]";
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        long deadline = Stopwatch.GetTimestamp() + timeout.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
        while (!condition())
        {
            if (Stopwatch.GetTimestamp() >= deadline) return false;
            await Task.Delay(10);
        }

        return true;
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        public System.Collections.Concurrent.ConcurrentQueue<Notification> All { get; } = new();

        public void Show(Notification notification) => All.Enqueue(notification);
    }
}

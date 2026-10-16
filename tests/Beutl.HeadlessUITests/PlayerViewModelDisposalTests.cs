using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.Audio;
using Beutl.Graphics.Shapes;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// Regression coverage for abandoned playback tasks.
[TestFixture]
public class PlayerViewModelDisposalTests
{
    [AvaloniaTest]
    public async Task Publishing_audio_after_dispose_is_safe_for_abandoned_playback_tasks()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("player-dispose");
        PlayerViewModel player = editor.Player;

        await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!);
        HeadlessTestHelpers.Settle();

        // A delayed audio task may still call this after disposal.
        using var audio = new AudioBuffer(44100, 2, 64);
        Assert.DoesNotThrow(() => player.PublishAudioSnapshot(audio, TimeSpan.Zero));

        bool completed = false;
        ((IPreviewPlayer)player).AudioFramePushed.Subscribe(_ => { }, () => completed = true);
        Assert.That(completed, Is.True,
            "AudioFramePushed must be completed, not disposed, after DisposeAsync");
    }

    // GPU-gated: the playback loop renders preview frames through SceneRenderer.
    [AvaloniaTest]
    public async Task Disposing_the_editor_while_the_playback_timer_is_active_stops_cleanly()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("player-timer-dispose");
        PlayerViewModel player = editor.Player;
        await TestShell.AddElementAsync(editor, () => new RectShape(), TimeSpan.Zero, TimeSpan.FromSeconds(4));
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();

        var frameApplied = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPreviewInvalidated(object? sender, EventArgs e) => frameApplied.TrySetResult(true);
        player.PreviewInvalidated += OnPreviewInvalidated;

        // Capture backend errors for diagnostics.
        using var notifications = new NotificationCapture();

        try
        {
            player.Play();

            await frameApplied.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                await HeadlessTestHelpers.WaitUntilAsync(
                    () => clock.CurrentTime.Value > TimeSpan.Zero, TimeSpan.FromSeconds(5), pump: false);
            }
            catch (TimeoutException)
            {
                Assert.Fail($"the playback timer must advance the editor clock; {Diagnostics()}");
            }

            Assert.That(player.IsPlaying.Value, Is.True,
                $"playback must still be running before the mid-playback disposal; {Diagnostics()}");

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
            player.PreviewInvalidated -= OnPreviewInvalidated;
        }

        string Diagnostics() =>
            $"renderError={player.PreviewRenderError.Value}, clock={clock.CurrentTime.Value}, " +
            $"notifications=[{string.Join(" | ", notifications.All.Select(static n => n.Message))}]";
    }
}

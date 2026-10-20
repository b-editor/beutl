using Avalonia.Headless.NUnit;

using Beutl.Graphics.Rendering;
using Beutl.Models;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Playback renders one frame per render-thread job, so a job the UI queues runs between two frames instead of
// waiting for playback to end, and the producer holds no job while the read-ahead is full.
[NonParallelizable]
[TestFixture]
public class BufferedPlayerFrameJobTests
{
    private const int Rate = 30;
    private const int BudgetFrames = 10;

    [AvaloniaTest]
    public async Task A_render_thread_job_runs_between_playback_frames()
    {
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await OpenEditor();
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using BufferedPlayer player = CreatePlayer(editor, isPlaying);

        try
        {
            player.Start(RangeOf(editor));
            int queued = RenderThread.Dispatcher.Invoke(() => player.QueuedFrames, TimeSpan.FromSeconds(5));
            Assert.That(queued, Is.LessThan(BudgetFrames), "The job must not wait for the read-ahead to fill.");

            await HeadlessTestHelpers.WaitUntilAsync(
                () => player.QueuedFrames == BudgetFrames, TimeSpan.FromSeconds(20), pump: false);
            Assert.DoesNotThrow(
                () => RenderThread.Dispatcher.Invoke(static () => { }, TimeSpan.FromSeconds(5)),
                "A full read-ahead must leave the render thread free.");
            Assert.That(player.ProducerStopped, Is.False);
        }
        finally
        {
            isPlaying.Value = false;
            await HeadlessTestHelpers.WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(10), pump: false);
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_producer_waiting_for_room_stops_with_playback(bool dispose)
    {
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await OpenEditor();
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using BufferedPlayer player = CreatePlayer(editor, isPlaying);
        player.Start(RangeOf(editor));
        await HeadlessTestHelpers.WaitUntilAsync(
            () => player.QueuedFrames == BudgetFrames, TimeSpan.FromSeconds(20), pump: false);

        if (dispose)
            player.Dispose();
        else
            isPlaying.Value = false;

        await HeadlessTestHelpers.WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(10), pump: false);
        Assert.That(player.QueuedFrames, Is.EqualTo(dispose ? 0 : BudgetFrames));
    }

    private static BufferedPlayer CreatePlayer(EditViewModel editor, ReactivePropertySlim<bool> isPlaying)
        => new(editor, isPlaying, Rate, CancellationToken.None)
        {
            ReadAheadBytes = ReadAheadBudget.SnapshotBytes(editor.Renderer.Value.DeviceSize) * BudgetFrames,
        };

    private static PlaybackRange RangeOf(EditViewModel editor)
        => PlaybackRange.From(TimeSpan.Zero, editor.Scene.Start, editor.Scene.Duration, Rate);

    private static async Task<EditViewModel> OpenEditor()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("frame-jobs", 320, 240, frameRate: Rate);

        // Let the preview render queued by opening the scene finish first.
        RenderThreadDrain.Drain(TimeSpan.FromMinutes(2));
        return editor;
    }
}

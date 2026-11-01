using System.Diagnostics;

using Avalonia.Headless.NUnit;

using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Primitives;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Playback renders ahead into a queue of full-size RgbaF16 snapshots that the frame cache budget does not count, so
// the lead is bounded by memory instead of a fixed 120 frames. A budget of a few small frames keeps the test cheap.
[NonParallelizable]
[TestFixture]
public class BufferedPlayerReadAheadTests
{
    private const int BudgetFrames = 10;

    [AvaloniaTest]
    public async Task Producer_StopsRenderingAhead_OnceTheQueuedFramesFillTheBudget()
    {
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await OpenEditor();
        long frameBytes = ReadAheadBudget.SnapshotBytes(editor.Renderer.Value.DeviceSize);
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(
            editor, isPlaying, editor.Player.GetFrameRate(), CancellationToken.None)
        {
            ReadAheadBytes = frameBytes * BudgetFrames,
        };

        try
        {
            player.Start(PlaybackRange.From(
                MediaTime.Zero, editor.Scene.Start.Value, editor.Scene.Duration.Value, editor.Player.GetFrameRate()));
            await WaitUntilAsync(() => player.QueuedFrames >= BudgetFrames, TimeSpan.FromSeconds(20));

            // Nothing consumes the queue, so a producer that ignored the budget would keep rendering.
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Assert.That(player.QueuedFrames, Is.EqualTo(BudgetFrames));

            // A consumed frame makes room for one more, and no further.
            Assert.That(player.TryDequeue(out BufferedPlayer.Frame frame), Is.True);
            frame.Bitmap.Dispose();
            await WaitUntilAsync(() => player.QueuedFrames == BudgetFrames, TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Assert.That(player.QueuedFrames, Is.EqualTo(BudgetFrames));
        }
        finally
        {
            isPlaying.Value = false;
            await WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(10));
        }
    }

    // A 2D frame lends its pixels to the clean preview that the presentation window and the mobile preview show,
    // so the clean image takes no room of its own. Room stays for a next frame that might need a second snapshot.
    [AvaloniaTest]
    public async Task A_clean_preview_that_shares_the_frame_takes_no_room_of_its_own()
    {
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await OpenEditor();
        using IDisposable cleanPreview = editor.Player.AcquireCleanPreview();

        await AssertProducerStopsAt(editor, BudgetFrames - 1);
    }

    // A frame with a 3D scene draws its clean image again without gizmos, so it holds two snapshots.
    [AvaloniaTest]
    public async Task A_clean_preview_drawn_apart_from_the_frame_takes_a_snapshot_of_its_own()
    {
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await OpenEditor();
        var scene3D = new Scene3D
        {
            RenderWidth = { Value = 320 },
            RenderHeight = { Value = 240 },
        };
        scene3D.Objects.Add(new Cube3D());
        await TestShell.AddElementAsync(editor, () => scene3D, MediaTime.Zero, MediaTime.FromSeconds(10));
        using IDisposable cleanPreview = editor.Player.AcquireCleanPreview();

        await AssertProducerStopsAt(editor, BudgetFrames / 2);
    }

    // With nothing taking frames, the producer queues the expected number of frames and no more.
    private static async Task AssertProducerStopsAt(EditViewModel editor, int expected)
    {
        long frameBytes = ReadAheadBudget.SnapshotBytes(editor.Renderer.Value.DeviceSize);
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(
            editor, isPlaying, editor.Player.GetFrameRate(), CancellationToken.None)
        {
            ReadAheadBytes = frameBytes * BudgetFrames,
        };

        try
        {
            player.Start(PlaybackRange.From(
                MediaTime.Zero, editor.Scene.Start.Value, editor.Scene.Duration.Value, editor.Player.GetFrameRate()));
            await WaitUntilAsync(() => player.QueuedFrames >= expected, TimeSpan.FromSeconds(20));

            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Assert.That(player.QueuedFrames, Is.EqualTo(expected));
        }
        finally
        {
            isPlaying.Value = false;
            await WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(10));
        }
    }

    private static async Task<EditViewModel> OpenEditor()
    {
        await TestReset.ResetShellAsync();
        string name = $"read-ahead-{Guid.NewGuid():N}";
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(320, 240, new Rational(30), 44100, name, directory))!;
        HeadlessTestHelpers.Settle();
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();

        // Let the preview render queued by opening the scene finish before the producer takes the render thread.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        RenderThread.Dispatcher.Invoke(static () => { }, ct: timeout.Token);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
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

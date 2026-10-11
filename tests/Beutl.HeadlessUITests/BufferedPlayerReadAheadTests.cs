using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

using Avalonia.Headless.NUnit;

using Beutl.Graphics.Rendering;
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
        GpuTestGate.EnsureAvailable();
        EditViewModel editor = await OpenEditor();
        long frameBytes = ReadAheadBudget.SnapshotBytes(editor.Renderer.Value.DeviceSize);
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(
            editor, editor.Scene, isPlaying, editor.Player.GetFrameRate(), CancellationToken.None)
        {
            ReadAheadBytes = frameBytes * BudgetFrames,
        };

        try
        {
            player.Start();
            await WaitUntilAsync(() => QueuedFrames(player) >= BudgetFrames, TimeSpan.FromSeconds(20));

            // Nothing consumes the queue, so a producer that ignored the budget would keep rendering.
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Assert.That(QueuedFrames(player), Is.EqualTo(BudgetFrames));

            // A consumed frame makes room for one more, and no further.
            Assert.That(player.TryDequeue(out IPlayer.Frame frame), Is.True);
            frame.Bitmap.Dispose();
            await WaitUntilAsync(() => QueuedFrames(player) == BudgetFrames, TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Assert.That(QueuedFrames(player), Is.EqualTo(BudgetFrames));
        }
        finally
        {
            isPlaying.Value = false;
            await WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(10));
        }
    }

    private static int QueuedFrames(BufferedPlayer player)
        => ((ConcurrentQueue<IPlayer.Frame>)typeof(BufferedPlayer)
            .GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(player)!).Count;

    private static async Task<EditViewModel> OpenEditor()
    {
        await TestReset.ResetShellAsync();
        string name = $"read-ahead-{Guid.NewGuid():N}";
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(320, 240, 30, 44100, name, directory))!;
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

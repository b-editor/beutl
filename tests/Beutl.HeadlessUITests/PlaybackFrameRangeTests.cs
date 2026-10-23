using System.Collections.Concurrent;

using Avalonia.Headless.NUnit;

using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// The audio starts at the playhead, so the video must start at the frame the paused preview shows there, not at the
// frame before it, and the producer must render every frame up to the end of the scene.
[NonParallelizable]
[TestFixture]
public class PlaybackFrameRangeTests
{
    private const int Rate = 30;

    [AvaloniaTest]
    public async Task Playback_RendersNoFrameBeforeThePlayhead()
    {
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await OpenEditor();
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        // Where playback leaves the playhead at frame 10: 3,333,333 ticks, which truncates to frame 9.
        MediaTime playhead = MediaTime.FromFrames(10, new Rational(Rate));
        clock.Seek(playhead);
        RenderThreadDrain.Drain(TimeSpan.FromSeconds(120));

        // The producer reports each frame it queues on the render thread.
        var queued = new ConcurrentQueue<MediaTime>();
        using IDisposable subscription = editor.BufferStatus.EndTime.Subscribe(time =>
        {
            if (RenderThread.Dispatcher.CheckAccess())
                queued.Enqueue(time);
        });

        try
        {
            editor.Player.Play();
            await HeadlessTestHelpers.WaitUntilAsync(() => queued.Count >= 3, TimeSpan.FromSeconds(120), pump: false);
        }
        finally
        {
            await editor.Player.Pause();
        }

        Assert.That(queued, Has.All.GreaterThanOrEqualTo(playhead));
    }

    [AvaloniaTest]
    public async Task Producer_RendersEveryFrameOfTheRange_ToTheEndOfAMovedScene()
    {
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await OpenEditor();
        // As dragging the scene start leaves it: the start and the end snapped to frames 1 and 4. Truncating the
        // start and adding the length rounded up gave frames 0 to 3.
        Scene scene = editor.Scene;
        MediaTime end = MediaTime.FromSeconds(4d / Rate).RoundToFrame(new Rational(Rate), MidpointRounding.AwayFromZero);
        scene.Start.Value = MediaTime.FromSeconds(1d / Rate).RoundToFrame(new Rational(Rate), MidpointRounding.AwayFromZero);
        scene.Duration.Value = end - scene.Start.Value;
        RenderThreadDrain.Drain(TimeSpan.FromSeconds(120));

        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(editor, isPlaying, new Rational(Rate), CancellationToken.None);

        player.Start(PlaybackRange.From(scene.Start.Value, scene.Start.Value, scene.Duration.Value, new Rational(Rate)));
        await HeadlessTestHelpers.WaitUntilAsync(() => player.QueuedFrames >= 3, TimeSpan.FromSeconds(120), pump: false);
        // The producer then waits at the end of the range for an edit that drops frames, rendering nothing past it.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        isPlaying.Value = false;
        await HeadlessTestHelpers.WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(120), pump: false);

        var frames = new List<int>();
        while (player.TryDequeue(out BufferedPlayer.Frame frame))
        {
            frames.Add(frame.Time);
            frame.Bitmap.Dispose();
        }

        Assert.Multiple(() =>
        {
            Assert.That(player.Failure, Is.Null);
            Assert.That(frames, Is.EqualTo(new[] { 1, 2, 3 }));
        });
    }

    private static async Task<EditViewModel> OpenEditor()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("playback-frame-range", 320, 240, frameRate: Rate);

        // Let the preview render queued by opening the scene finish first.
        RenderThreadDrain.Drain(TimeSpan.FromSeconds(120));
        return editor;
    }
}

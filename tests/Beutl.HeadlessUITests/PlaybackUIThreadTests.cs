using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;

using Avalonia.Headless.NUnit;
using Avalonia.Threading;

using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// Playback runs from a frame timer on a thread-pool thread, but editors subscribed to the playhead read collections
// that the UI thread edits. So the playhead, the preview and IsPlaying must change on the UI thread, nothing posted
// for a run may land after it stopped or rewound, and a subscriber that throws must stop playback, not the process.
[NonParallelizable]
[TestFixture]
public class PlaybackUIThreadTests
{
    [AvaloniaTest]
    public async Task Playback_NotifiesThePlayheadPreviewAndIsPlaying_OnTheUIThread()
    {
        GpuTestGate.EnsureAvailable();
        EditViewModel editor = await OpenEditor(TimeSpan.FromSeconds(0.5));
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        var offThread = new ConcurrentQueue<string>();
        int clockChanges = 0;
        int previewChanges = 0;
        int playingChanges = 0;
        // Once playback stops, the paused preview renders the playhead frame on the render thread; that is not
        // playback, so only the run itself counts.
        bool running = true;
        using IDisposable clockSubscription = clock.CurrentTime.Skip(1).Subscribe(_ =>
        {
            if (Volatile.Read(ref running))
                Record(ref clockChanges, offThread, "CurrentTime");
        });
        using IDisposable previewSubscription = editor.Player.PreviewImage.Skip(1).Subscribe(_ =>
        {
            if (Volatile.Read(ref running))
                Record(ref previewChanges, offThread, "PreviewImage");
        });
        using IDisposable playingSubscription = editor.Player.IsPlaying.Skip(1).Subscribe(playing =>
        {
            Record(ref playingChanges, offThread, "IsPlaying");
            if (!playing)
                Volatile.Write(ref running, false);
        });

        // The scene is half a second long, so playback stops at its end by itself.
        editor.Player.Play();
        await WaitUntilAsync(() => playingChanges >= 2 && !editor.Player.IsPlaybackActive, TimeSpan.FromSeconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(clockChanges, Is.GreaterThan(1), "playback must move the playhead");
            Assert.That(previewChanges, Is.GreaterThan(1), "playback must show frames");
            Assert.That(offThread, Is.Empty, "notifications that arrived off the UI thread");
        });
    }

    // At the end of a looping scene the playhead goes back to the start and playback goes on. A frame or a stop
    // that the run posted before the rewind must not land after it.
    [AvaloniaTest]
    public async Task LoopPlayback_RewindsToTheStart_AndKeepsPlaying()
    {
        GpuTestGate.EnsureAvailable();
        EditViewModel editor = await OpenEditor(TimeSpan.FromSeconds(0.3));
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        var times = new ConcurrentQueue<TimeSpan>();
        var offThread = new ConcurrentQueue<string>();
        int clockChanges = 0;
        using IDisposable subscription = clock.CurrentTime.Skip(1).Subscribe(time =>
        {
            Record(ref clockChanges, offThread, "CurrentTime");
            times.Enqueue(time);
        });
        editor.Player.IsLoopEnabled.Value = true;

        try
        {
            editor.Player.Play();
            // A stop that landed after a restart would end playback before the second rewind.
            await WaitUntilAsync(() => Rewinds(times) >= 2, TimeSpan.FromSeconds(20));
        }
        finally
        {
            await editor.Player.Pause();
            editor.Player.IsLoopEnabled.Value = false;
        }

        TimeSpan[] seen = [.. times];
        Assert.Multiple(() =>
        {
            Assert.That(offThread, Is.Empty, "notifications that arrived off the UI thread");
            for (int i = 1; i < seen.Length; i++)
            {
                if (seen[i] < seen[i - 1])
                {
                    Assert.That(seen[i], Is.EqualTo(editor.Scene.Start),
                        $"the playhead went back from {seen[i - 1]} to {seen[i]}, not to the start of the scene");
                }
            }
        });
    }

    // Pause disowns the run first, so what the run posted before it cannot move the playhead afterwards.
    [AvaloniaTest]
    public async Task Pause_LeavesThePlayheadWhereItStopped()
    {
        GpuTestGate.EnsureAvailable();
        EditViewModel editor = await OpenEditor(TimeSpan.FromSeconds(30));
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();

        editor.Player.Play();
        await WaitUntilAsync(() => clock.CurrentTime.Value >= TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(20));
        await editor.Player.Pause();
        TimeSpan pausedAt = clock.CurrentTime.Value;

        // Let whatever is still queued on the UI thread run.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(pausedAt));
            Assert.That(editor.Player.IsPlaying.Value, Is.False);
        });
    }

    // A playhead subscriber that threw used to escape the timer callback, which ends the process.
    [AvaloniaTest]
    public async Task SubscriberThatThrows_StopsPlayback()
    {
        GpuTestGate.EnsureAvailable();
        EditViewModel editor = await OpenEditor(TimeSpan.FromSeconds(30));
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        int thrown = 0;
        using IDisposable subscription = clock.CurrentTime.Skip(1).Subscribe(_ =>
        {
            if (Interlocked.Exchange(ref thrown, 1) == 0)
                throw new InvalidOperationException("A playhead subscriber failed.");
        });

        editor.Player.Play();
        await WaitUntilAsync(() => thrown == 1 && !editor.Player.IsPlaybackActive, TimeSpan.FromSeconds(20));

        Assert.That(editor.Player.IsPlaying.Value, Is.False);
    }

    private static void Record(ref int count, ConcurrentQueue<string> offThread, string what)
    {
        Interlocked.Increment(ref count);
        if (!Dispatcher.UIThread.CheckAccess())
            offThread.Enqueue(what);
    }

    private static int Rewinds(ConcurrentQueue<TimeSpan> times)
    {
        TimeSpan[] seen = [.. times];
        int rewinds = 0;
        for (int i = 1; i < seen.Length; i++)
        {
            if (seen[i] < seen[i - 1])
                rewinds++;
        }

        return rewinds;
    }

    private static async Task<EditViewModel> OpenEditor(TimeSpan duration)
    {
        await TestReset.ResetShellAsync();
        string name = $"playback-ui-thread-{Guid.NewGuid():N}";
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(320, 240, 30, 44100, name, directory))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();
        scene.Duration = duration;
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();

        // Let the preview render queued by opening the scene finish first.
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

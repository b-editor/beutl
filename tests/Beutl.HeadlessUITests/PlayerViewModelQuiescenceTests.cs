using System.Reflection;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PlayerViewModelQuiescenceTests
{
    [AvaloniaTest]
    public async Task History_waits_for_active_composition_after_the_playback_timeout()
    {
        EditViewModel editor = await OpenEditor();
        var sound = AddBlockingSound(editor.Scene);
        Task playback = StartComposition(editor);
        Task<bool>? mutation = null;
        bool mutated = false;
        try
        {
            await sound.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            mutation = editor.ExecuteGuardedHistoryMutationAsync(() => true, () =>
            {
                mutated = true;
                editor.Scene.Children.Clear();
                return true;
            }).AsTask();

            await Task.WhenAny(mutation, Task.Delay(TimeSpan.FromSeconds(6)));
            bool uiResponded = false;
            await Dispatcher.UIThread.InvokeAsync(() => uiResponded = true);
            Assert.Multiple(() =>
            {
                Assert.That(playback.IsCompleted, Is.False, "composition is still using the live scene");
                Assert.That(mutation.IsCompleted, Is.False, "the native playback timeout must not release scene readers");
                Assert.That(mutated, Is.False);
                Assert.That(uiResponded, Is.True, "waiting for composition must yield the UI thread");
            });
        }
        finally
        {
            sound.Release.Set();
            await playback.WaitAsync(TimeSpan.FromSeconds(5));
            if (mutation is not null)
                await mutation.WaitAsync(TimeSpan.FromSeconds(5));
            sound.Release.Dispose();
        }

        Assert.That(mutated, Is.True);
    }

    [AvaloniaTest]
    public async Task Closing_an_editor_keeps_its_composer_alive_until_active_composition_finishes()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        SceneComposer composer = editor.Composer.Value;
        var sound = AddBlockingSound(editor.Scene);
        Task playback = StartComposition(editor);
        Task? closing = null;
        try
        {
            await sound.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closing = TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!, saveChanges: false).AsTask();

            await Task.WhenAny(closing, Task.Delay(TimeSpan.FromSeconds(6)));
            Assert.Multiple(() =>
            {
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(composer.IsDisposed, Is.False, "the audio graph is still in use");
                Assert.That(player.Scene, Is.Not.Null);
            });
        }
        finally
        {
            sound.Release.Set();
            await playback.WaitAsync(TimeSpan.FromSeconds(5));
            if (closing is not null)
                await closing.WaitAsync(TimeSpan.FromSeconds(5));
            sound.Release.Dispose();
        }

        Assert.That(composer.IsDisposed, Is.True);
        Assert.That(player.Scene, Is.Null);
    }

    [AvaloniaTest]
    public async Task A_hung_native_backend_does_not_prevent_pause_when_scene_work_has_finished()
    {
        EditViewModel editor = await OpenEditor();
        var backend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetPlaybackTask(editor.Player, backend.Task);
        try
        {
            await editor.Player.Pause().WaitAsync(TimeSpan.FromSeconds(8));
            Assert.That(backend.Task.IsCompleted, Is.False, "only the backend may be abandoned");
            await editor.Player.Pause().WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            backend.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Pause_drains_render_work_and_shares_the_drain_with_overlapping_callers()
    {
        EditViewModel editor = await OpenEditor();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        Task render = RenderThread.Dispatcher.InvokeAsync(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release rendering.");
        });
        Task? pause = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pause = editor.Player.Pause();
            Assert.That(editor.Player.Pause(), Is.SameAs(pause));
            editor.Player.Play();
            editor.Player.ShuttleForward();
            Assert.Multiple(() =>
            {
                Assert.That(pause.IsCompleted, Is.False, "the playback task can finish before its render producer");
                Assert.That(editor.Player.IsPlaying.Value, Is.False, "a new session must not enter behind the barrier");
            });
            Assert.That(await ((IPreviewPlayer)editor.Player).ComposeAudioAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                Is.Null, "idle audio requests must be suspended while draining");
        }
        finally
        {
            release.Set();
            await render.WaitAsync(TimeSpan.FromSeconds(5));
            if (pause is not null)
                await pause.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTest]
    public async Task Concurrent_pause_callers_share_one_scene_drain()
    {
        EditViewModel editor = await OpenEditor();
        var renderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRender = new ManualResetEventSlim();
        Task render = RenderThread.Dispatcher.InvokeAsync(() =>
        {
            renderEntered.TrySetResult();
            if (!releaseRender.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("The test did not release rendering.");
        });
        using var callersStarted = new CountdownEvent(2);
        TaskCompletionSource<Task>[] calls =
        [
            new(TaskCreationOptions.RunContinuationsAsynchronously),
            new(TaskCreationOptions.RunContinuationsAsynchronously)
        ];
        Thread[] threads = calls.Select(call => new Thread(() =>
        {
            callersStarted.Signal();
            try
            {
                call.SetResult(editor.Player.Pause());
            }
            catch (Exception ex)
            {
                call.SetException(ex);
            }
        })
        { IsBackground = true }).ToArray();
        try
        {
            await renderEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            object requestLock = typeof(PlayerViewModel)
                .GetField("_renderRequestLock", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(editor.Player)!;
            lock (requestLock)
            {
                foreach (Thread thread in threads)
                    thread.Start();
                Assert.That(callersStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
                // Hold startup before its first await. Both callers must reach the contended
                // lock before releasing it, so the old check-then-start race is deterministic.
                Assert.That(SpinWait.SpinUntil(() => threads.All(thread =>
                    (thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0),
                    TimeSpan.FromSeconds(5)), Is.True);
            }

            Task first = await calls[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task second = await calls[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(first, Is.SameAs(second));
                Assert.That(editor.Player.Pause(), Is.SameAs(first));
                Assert.That(first.IsCompleted, Is.False, "all callers must await the blocked scene work");
            });
        }
        finally
        {
            releaseRender.Set();
            await render.WaitAsync(TimeSpan.FromSeconds(5));
            foreach (var call in calls)
            {
                Task pause = await call.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await pause.WaitAsync(TimeSpan.FromSeconds(5));
            }
            foreach (Thread thread in threads)
                Assert.That(thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_queued_audio_buffer_checks_stop_state_on_the_compose_thread(bool cancelToken)
    {
        EditViewModel editor = await OpenEditor();
        var sound = AddBlockingSound(editor.Scene);
        sound.Release.Set();
        using var cts = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task blocker = ComposeThread.Dispatcher.InvokeAsync(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the compose dispatcher.");
        });
        Task? pause = null;
        Task<(Pcm<Stereo32BitFloat>? Pcm, TimeSpan SceneEnd)>? buffer = null;
        bool bufferWasComposed = false;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            int generation = GetSessionGuard(editor.Player).Claim();
            buffer = ComposeThread.Dispatcher.InvokeAsync(() =>
                editor.Player.FillAudioData(TimeSpan.Zero, editor.Scene, generation, cts.Token));
            if (cancelToken)
                cts.Cancel();
            else
            {
                SetPlaybackTask(editor.Player, buffer);
                pause = editor.Player.Pause();
            }
        }
        finally
        {
            release.Set();
            // Keep this UI turn until the queued audio reaches its admission check. PauseCore
            // may still be queued on the UI dispatcher, but the stop request must already apply.
            if (pause is not null)
                ComposeThread.Dispatcher.Invoke(static () => { });
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));
            if (buffer is not null)
            {
                using var pcm = (await buffer.WaitAsync(TimeSpan.FromSeconds(5))).Pcm;
                bufferWasComposed = pcm is not null;
            }
            if (pause is not null)
                await pause.WaitAsync(TimeSpan.FromSeconds(5));
            sound.Release.Dispose();
        }
        Assert.That(bufferWasComposed, Is.False);
        Assert.That(sound.Entered.Task.IsCompleted, Is.False, "the retired buffer must not compose the live scene");
    }

    [AvaloniaTest]
    public async Task Background_idle_audio_requests_cannot_enter_during_a_history_mutation()
    {
        EditViewModel editor = await OpenEditor();
        var sound = AddBlockingSound(editor.Scene);
        sound.Release.Set();
        Task? audio = null;
        Task? request = null;
        try
        {
            await editor.ExecuteGuardedHistoryMutationAsync(() => true, () =>
            {
                var requested = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
                request = Task.Run(() => requested.SetResult(((IPreviewPlayer)editor.Player)
                    .ComposeAudioAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1))));
                audio = requested.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                // Flush any audio work already admitted by the background caller, while the UI
                // thread is still executing this synchronous history mutation.
                ComposeThread.Dispatcher.Invoke(static () => { });
                Assert.That(sound.Entered.Task.IsCompleted, Is.False);
                return true;
            });
            await audio!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(sound.Entered.Task.IsCompleted, Is.True);
        }
        finally
        {
            if (request is not null)
                await request.WaitAsync(TimeSpan.FromSeconds(5));
            if (audio is not null)
                await audio.WaitAsync(TimeSpan.FromSeconds(5));
            sound.Release.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task History_waiting_for_composition_does_not_mutate_an_editor_that_is_closing()
    {
        EditViewModel editor = await OpenEditor();
        var sound = AddBlockingSound(editor.Scene);
        Task playback = StartComposition(editor);
        Task<bool>? mutation = null;
        Task? closing = null;
        bool mutated = false;
        try
        {
            await sound.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            mutation = editor.ExecuteGuardedHistoryMutationAsync(() => true, () => mutated = true).AsTask();
            closing = TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!, saveChanges: false).AsTask();
        }
        finally
        {
            sound.Release.Set();
            await playback.WaitAsync(TimeSpan.FromSeconds(5));
            if (mutation is not null)
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await mutation.WaitAsync(TimeSpan.FromSeconds(5)));
            if (closing is not null)
                await closing.WaitAsync(TimeSpan.FromSeconds(5));
            sound.Release.Dispose();
        }
        Assert.That(mutated, Is.False);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_stopped_render_producer_cannot_publish_a_new_wait(bool cancelToken)
    {
        EditViewModel editor = await OpenEditor();
        using var cts = new CancellationTokenSource();
        using var playing = new ReactivePropertySlim<bool>(true);
        using var producer = new BufferedPlayer(editor, editor.Scene, playing, 30, cts.Token);
        if (cancelToken)
            cts.Cancel();
        else
            playing.Value = false;
        var gate = (BufferedPlayerWaitGate)typeof(BufferedPlayer)
            .GetField("_waitTimerGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(producer)!;
        using var wait = new CancellationTokenSource();
        Assert.That(gate.Publish(wait), Is.False, "a pause before wait publication must not leave the render barrier blocked");
    }

    private static async Task<EditViewModel> OpenEditor()
    {
        await TestReset.ResetShellAsync();
        string name = $"playback-quiescence-{Guid.NewGuid():N}";
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, directory))!;
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static BlockingPlaybackSound AddBlockingSound(Scene scene)
    {
        var sound = new BlockingPlaybackSound();
        var element = new Element { Length = TimeSpan.FromSeconds(2) };
        element.Objects.Add(sound);
        scene.Children.Add(element);
        return sound;
    }

    private static Task StartComposition(EditViewModel editor)
    {
        int generation = GetSessionGuard(editor.Player).Claim();
        Task playback = Task.Run(() =>
        {
            using var pcm = editor.Player.FillAudioData(
                TimeSpan.Zero, editor.Scene, generation, CancellationToken.None).Pcm;
        });
        SetPlaybackTask(editor.Player, playback);
        return playback;
    }

    private static PlaybackSessionGuard GetSessionGuard(PlayerViewModel player) =>
        (PlaybackSessionGuard)typeof(PlayerViewModel)
            .GetField("_sessionGuard", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player)!;

    private static void SetPlaybackTask(PlayerViewModel player, Task playback)
    {
        typeof(PlayerViewModel).GetField("_playbackTask", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(player, playback);
    }
}

public sealed partial class BlockingPlaybackSound : Sound
{
    public BlockingPlaybackSound() => ScanProperties<BlockingPlaybackSound>();

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ManualResetEventSlim Release { get; } = new();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        Entered.TrySetResult();
        if (!Release.Wait(TimeSpan.FromSeconds(20)))
            throw new TimeoutException("The test did not release audio composition.");
        context.Clear();
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}

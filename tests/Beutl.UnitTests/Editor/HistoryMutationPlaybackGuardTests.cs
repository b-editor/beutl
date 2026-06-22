using System.Reactive;
using System.Reactive.Linq;
using Beutl.Editor.Services;
using Beutl.Helpers;
using Beutl.Media;
using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class HistoryMutationPlaybackGuardTests
{
    [Test]
    public async Task RunAsync_WhenPlayerIsPlaying_StopsAndAwaitsBeforeMutation()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: true);
        bool mutatedAfterPause = false;

        bool result = await guard.RunAsync(player, () => { }, () => true, () =>
        {
            mutatedAfterPause = !player.IsPlaying.Value;
            return true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(mutatedAfterPause, Is.True);
            Assert.That(player.StopCount, Is.EqualTo(1));
            Assert.That(player.IsPlaying.Value, Is.False);
        });
    }

    [Test]
    public async Task RunAsync_WhenPlayerIsStopped_MutatesWithoutStopping()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: false);
        bool mutated = false;

        bool result = await guard.RunAsync(player, () => { }, () => true, () =>
        {
            mutated = true;
            return true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(mutated, Is.True);
            Assert.That(player.StopCount, Is.Zero);
        });
    }

    [Test]
    public async Task RunAsync_WhenPlayerIsNull_DoesNotThrow()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        bool mutated = false;

        bool result = await guard.RunAsync(null, () => { }, () => true, () =>
        {
            mutated = true;
            return true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(mutated, Is.True);
        });
    }

    [Test]
    public async Task RunAsync_WhenMutationDoesNotNeedPause_DoesNotCallPause()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: true);
        bool mutated = false;

        bool result = await guard.RunAsync(player, () => { }, () => false, () =>
        {
            mutated = true;
            return false;
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(mutated, Is.True);
            Assert.That(player.PauseCallCount, Is.Zero);
            Assert.That(player.IsPlaying.Value, Is.True);
        });
    }

    [Test]
    public async Task RunAsync_PausesBeforeDrainingPendingMutations()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: true);
        bool drainedWhileStopped = false;
        bool mutatedWhileStopped = false;

        // The drain commits pending work (e.g. a nudge) that schedules frame-cache rebuilds,
        // so it must run only after the player is paused — never against a live player.
        bool result = await guard.RunAsync(
            player,
            () => drainedWhileStopped = !player.IsPlaying.Value,
            () => true,
            () =>
            {
                mutatedWhileStopped = !player.IsPlaying.Value;
                return true;
            });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(drainedWhileStopped, Is.True, "the drain must run only after playback is paused");
            Assert.That(mutatedWhileStopped, Is.True);
            Assert.That(player.StopCount, Is.EqualTo(1), "a mutation that needs a pause must stop playback once");
            Assert.That(player.IsPlaying.Value, Is.False);
        });
    }

    [Test]
    public async Task RunAsync_WhenPlaybackRestartsDuringPause_RePausesBeforeMutation()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: true)
        {
            SimulateRestartOnce = true
        };
        bool mutatedWhileStopped = false;

        bool result = await guard.RunAsync(player, () => { }, () => true, () =>
        {
            mutatedWhileStopped = !player.IsPlaying.Value;
            return true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(mutatedWhileStopped, Is.True, "the mutation must run only after the restarted playback is paused again");
            Assert.That(player.StopCount, Is.EqualTo(2), "the guard must re-pause the playback that restarted mid-drain");
            Assert.That(player.IsPlaying.Value, Is.False);
        });
    }

    [Test]
    public async Task RunAsync_WhenPauseIsInFlight_SecondMutationWaitsAndDoesNotStopTwice()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: true)
        {
            CompletePauseManually = true
        };
        int mutationCount = 0;

        Task<bool> first = guard.RunAsync(player, () => { }, () => true, () =>
        {
            mutationCount++;
            return true;
        }).AsTask();
        await player.WaitForPauseStartedAsync();

        Task<bool> second = guard.RunAsync(player, () => { }, () => true, () =>
        {
            mutationCount++;
            return true;
        }).AsTask();
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(mutationCount, Is.Zero);
            Assert.That(player.StopCount, Is.EqualTo(1));
        });

        player.CompleteDrain();
        bool[] results = await Task.WhenAll(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.All.True);
            Assert.That(mutationCount, Is.EqualTo(2));
            Assert.That(player.StopCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RunAsync_WhenPlayerStoppedMidDrain_AwaitsDrainBeforeMutation()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: true)
        {
            CompletePauseManually = true
        };
        // An external Pause() has already cleared IsPlaying but the pipeline is still
        // draining; the guard must await that in-flight drain rather than skip it.
        player.BeginExternalDrain();
        bool mutatedBeforeDrain = false;

        Task<bool> run = guard.RunAsync(player, () => { }, () => true, () =>
        {
            mutatedBeforeDrain = !player.DrainCompleted;
            return true;
        }).AsTask();
        await Task.Yield();

        Assert.That(run.IsCompleted, Is.False);

        player.CompleteDrain();
        bool result = await run;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(mutatedBeforeDrain, Is.False);
            Assert.That(player.StopCount, Is.Zero);
        });
    }

    private sealed class PreviewPlayerStub : IPreviewPlayer, IDisposable
    {
        private readonly ReactivePropertySlim<Ref<Bitmap>?> _previewImage = new();
        private readonly ReactivePropertySlim<bool> _isPlaying;
        private readonly TaskCompletionSource _pauseStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Mirrors PlayerViewModel._playbackTask: incomplete while a drain is in
        // flight, already complete when nothing is playing.
        private readonly TaskCompletionSource _drain = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _restartedOnce;

        public PreviewPlayerStub(bool isPlaying)
        {
            _isPlaying = new ReactivePropertySlim<bool>(isPlaying);
            if (!isPlaying)
            {
                _drain.SetResult();
            }
        }

        public bool CompletePauseManually { get; init; }

        // When set, the first Pause() that stops playback re-arms IsPlaying afterwards,
        // simulating a Play() that restarted the preview during the drain. The guard's
        // re-pause loop must then stop it a second time before mutating.
        public bool SimulateRestartOnce { get; init; }

        public int PauseCallCount { get; private set; }

        public int StopCount { get; private set; }

        public bool DrainCompleted => _drain.Task.IsCompleted;

        public IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage => _previewImage;

        public IObservable<Unit> AfterRendered => Observable.Empty<Unit>();

        public IReadOnlyReactiveProperty<bool> IsPlaying => _isPlaying;

        public Task Pause()
        {
            PauseCallCount++;
            if (_isPlaying.Value)
            {
                StopCount++;
                _isPlaying.Value = false;
                _pauseStarted.TrySetResult();
                if (!CompletePauseManually)
                {
                    _drain.TrySetResult();
                }
            }

            if (SimulateRestartOnce && !_restartedOnce)
            {
                _restartedOnce = true;
                _isPlaying.Value = true;
            }

            return _drain.Task;
        }

        // Simulate an external pause that cleared IsPlaying but is still draining.
        public void BeginExternalDrain()
        {
            _isPlaying.Value = false;
        }

        public Task WaitForPauseStartedAsync()
        {
            return _pauseStarted.Task;
        }

        public void CompleteDrain()
        {
            _drain.TrySetResult();
        }

        public void Dispose()
        {
            _previewImage.Dispose();
            _isPlaying.Dispose();
        }
    }
}

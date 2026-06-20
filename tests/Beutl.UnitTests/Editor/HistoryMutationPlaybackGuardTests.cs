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
    public async Task RunAsync_WhenPlayerIsPlaying_AwaitsPauseBeforeMutation()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: true);
        bool mutatedAfterPause = false;

        bool result = await guard.RunAsync(player, () => true, () =>
        {
            mutatedAfterPause = !player.IsPlaying.Value;
            return true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(mutatedAfterPause, Is.True);
            Assert.That(player.PauseCallCount, Is.EqualTo(1));
            Assert.That(player.IsPlaying.Value, Is.False);
        });
    }

    [Test]
    public async Task RunAsync_WhenPlayerIsStopped_MutatesWithoutPause()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: false);
        bool mutated = false;

        bool result = await guard.RunAsync(player, () => true, () =>
        {
            mutated = true;
            return true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(mutated, Is.True);
            Assert.That(player.PauseCallCount, Is.Zero);
        });
    }

    [Test]
    public async Task RunAsync_WhenPlayerIsNull_DoesNotThrow()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        bool mutated = false;

        bool result = await guard.RunAsync(null, () => true, () =>
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
    public async Task RunAsync_WhenMutationDoesNotNeedPause_DoesNotPausePlayingPlayer()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: true);
        bool mutated = false;

        bool result = await guard.RunAsync(player, () => false, () =>
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
    public async Task RunAsync_WhenPauseIsInFlight_SecondMutationWaitsAndDoesNotPauseTwice()
    {
        using var guard = new HistoryMutationPlaybackGuard();
        using var player = new PreviewPlayerStub(isPlaying: true)
        {
            CompletePauseManually = true
        };
        int mutationCount = 0;

        Task<bool> first = guard.RunAsync(player, () => true, () =>
        {
            mutationCount++;
            return true;
        }).AsTask();
        await player.WaitForPauseStartedAsync();

        Task<bool> second = guard.RunAsync(player, () => true, () =>
        {
            mutationCount++;
            return true;
        }).AsTask();
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(mutationCount, Is.Zero);
            Assert.That(player.PauseCallCount, Is.EqualTo(1));
        });

        player.CompletePause();
        bool[] results = await Task.WhenAll(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.All.True);
            Assert.That(mutationCount, Is.EqualTo(2));
            Assert.That(player.PauseCallCount, Is.EqualTo(1));
        });
    }

    private sealed class PreviewPlayerStub : IPreviewPlayer, IDisposable
    {
        private readonly ReactivePropertySlim<Ref<Bitmap>?> _previewImage = new();
        private readonly ReactivePropertySlim<bool> _isPlaying;
        private readonly TaskCompletionSource _pauseStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _pauseCompletion;

        public PreviewPlayerStub(bool isPlaying)
        {
            _isPlaying = new ReactivePropertySlim<bool>(isPlaying);
        }

        public bool CompletePauseManually { get; init; }

        public int PauseCallCount { get; private set; }

        public IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage => _previewImage;

        public IObservable<Unit> AfterRendered => Observable.Empty<Unit>();

        public IReadOnlyReactiveProperty<bool> IsPlaying => _isPlaying;

        public Task Pause()
        {
            PauseCallCount++;
            _isPlaying.Value = false;
            _pauseStarted.TrySetResult();

            if (!CompletePauseManually)
            {
                return Task.CompletedTask;
            }

            _pauseCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pauseCompletion.Task;
        }

        public Task WaitForPauseStartedAsync()
        {
            return _pauseStarted.Task;
        }

        public void CompletePause()
        {
            _pauseCompletion?.SetResult();
        }

        public void Dispose()
        {
            _previewImage.Dispose();
            _isPlaying.Dispose();
        }
    }
}

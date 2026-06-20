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
    public async Task PauseIfPlayingAsync_WhenPlayerIsPlaying_AwaitsPause()
    {
        using var player = new PreviewPlayerStub(isPlaying: true);

        await HistoryMutationPlaybackGuard.PauseIfPlayingAsync(player);

        Assert.Multiple(() =>
        {
            Assert.That(player.PauseCallCount, Is.EqualTo(1));
            Assert.That(player.IsPlaying.Value, Is.False);
        });
    }

    [Test]
    public async Task PauseIfPlayingAsync_WhenPlayerIsStopped_DoesNotPause()
    {
        using var player = new PreviewPlayerStub(isPlaying: false);

        await HistoryMutationPlaybackGuard.PauseIfPlayingAsync(player);

        Assert.That(player.PauseCallCount, Is.Zero);
    }

    [Test]
    public async Task PauseIfPlayingAsync_WhenPlayerIsNull_DoesNotThrow()
    {
        await HistoryMutationPlaybackGuard.PauseIfPlayingAsync(null);
    }

    private sealed class PreviewPlayerStub : IPreviewPlayer, IDisposable
    {
        private readonly ReactivePropertySlim<Ref<Bitmap>?> _previewImage = new();
        private readonly ReactivePropertySlim<bool> _isPlaying;

        public PreviewPlayerStub(bool isPlaying)
        {
            _isPlaying = new ReactivePropertySlim<bool>(isPlaying);
        }

        public int PauseCallCount { get; private set; }

        public IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage => _previewImage;

        public IObservable<Unit> AfterRendered => Observable.Empty<Unit>();

        public IReadOnlyReactiveProperty<bool> IsPlaying => _isPlaying;

        public Task Pause()
        {
            PauseCallCount++;
            _isPlaying.Value = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _previewImage.Dispose();
            _isPlaying.Dispose();
        }
    }
}

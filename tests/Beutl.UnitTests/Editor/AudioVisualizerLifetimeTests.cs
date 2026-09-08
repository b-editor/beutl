using System.Reactive.Subjects;
using System.Reflection;
using Beutl.Editor.Components.AudioVisualizerTab;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Moq;
using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class AudioVisualizerLifetimeTests
{
    [Test]
    public async Task Dispose_cancels_and_drains_composition_without_publishing_late_audio()
    {
        using var frames = new Subject<AudioFrameSnapshot>();
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(x => x.AudioFramePushed).Returns(frames);
        player.SetupGet(x => x.IsPlaying).Returns(new ReactivePropertySlim<bool>(false));
        var clock = new Mock<IEditorClock>();
        clock.SetupGet(x => x.CurrentTime).Returns(new ReactivePropertySlim<TimeSpan>(TimeSpan.FromSeconds(1)));
        var completion = new TaskCompletionSource<AudioFrameSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken composeToken = default;
        player.Setup(x => x.ComposeAudioAsync(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<TimeSpan, TimeSpan, CancellationToken>((_, _, ct) => composeToken = ct)
            .Returns(completion.Task);
        var editor = new Mock<IEditorContext>();
        editor.Setup(x => x.GetService(typeof(IPreviewPlayer))).Returns(player.Object);
        editor.Setup(x => x.GetService(typeof(IEditorClock))).Returns(clock.Object);
        var vm = new AudioVisualizerTabViewModel(editor.Object, AudioVisualizerTabExtension.Instance);
        int snapshots = 0;
        vm.SnapshotUpdated += (_, _) => snapshots++;
        var method = typeof(AudioVisualizerTabViewModel).GetMethod("ComposeSnapshotOnIdleAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Task compose = (Task)method.Invoke(vm, null)!;
        Task disposal = vm.DisposeAsync().AsTask();
        Assert.Multiple(() =>
        {
            Assert.That(composeToken.IsCancellationRequested, Is.True);
            Assert.That(disposal.IsCompleted, Is.False);
        });
        completion.SetResult(new AudioFrameSnapshot([1, 1], 48000, 2, TimeSpan.Zero));
        await Task.WhenAll(compose, disposal).WaitAsync(TimeSpan.FromSeconds(5));
        frames.OnNext(new AudioFrameSnapshot([1, 1], 48000, 2, TimeSpan.Zero));
        Assert.That(snapshots, Is.Zero);
        Assert.That(vm.RingBuffer.TotalWritten, Is.Zero);
        await vm.DisposeAsync();
    }
}

using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class MediaThumbnailThreadingTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task OpeningMediaForThumbnails_DoesNotBlockTheUi(bool video)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-thumbnail-{Guid.NewGuid():N}.thumbnail-thread");
        File.WriteAllBytes(path, []);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        bool openedOnUi = false;
        var decoder = new Mock<IDecoderInfo>();
        decoder.Setup(d => d.IsSupported(path)).Returns(true);
        decoder.Setup(d => d.AudioExtensions()).Returns([".thumbnail-thread"]);
        decoder.Setup(d => d.VideoExtensions()).Returns([".thumbnail-thread"]);
        decoder.Setup(d => d.Open(path, It.IsAny<MediaOptions>())).Returns(() =>
        {
            openedOnUi |= Dispatcher.UIThread.CheckAccess();
            started.TrySetResult();
            if (!openedOnUi && !release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return null;
        });
        DecoderRegistry.Register(decoder.Object);
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        EngineObject source = video
            ? new SourceVideo { Source = { Value = VideoSource.Open(path) } }
            : new SourceSound { Source = { Value = SoundSource.Open(path) } };
        element.Objects.Add(source);
        var provider = (IThumbnailsProvider)source;
        Task loading = ReadAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(openedOnUi, Is.False);
            Assert.That(loading.IsCompleted, Is.False);
            release.Set();
            await loading;
        }
        finally
        {
            release.Set();
            try { await loading; }
            finally { DecoderRegistry.Unregister(decoder.Object); File.Delete(path); }
        }

        async Task ReadAsync()
        {
            if (video)
                await foreach (var (_, _, thumbnail) in provider.GetThumbnailStripAsync(100, 25, null)) thumbnail.Dispose();
            else
                await foreach (var chunk in provider.GetWaveformChunksAsync(10, 128, null)) { }
        }
    }
}

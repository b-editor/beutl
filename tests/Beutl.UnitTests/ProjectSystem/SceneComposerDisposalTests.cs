using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneComposerDisposalTests
{
    [Test]
    public void Dispose_WhenComposerCacheCleanupThrows_StillDisposesCompositorResources()
    {
        string basePath = Path.Combine(Path.GetTempPath(), $"beutl_scene_composer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        var cleanupFailure = new InvalidOperationException("audio-node cleanup failure");
        var sound = new SceneComposerThrowingDisposeSound(cleanupFailure);
        var scene = new Scene(16, 16, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "test.scene")),
        };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(basePath, "sound.layer")),
        };
        element.AddObject(sound);
        scene.Children.Add(element);
        var composer = new SceneComposer(scene, RenderIntent.Delivery);

        try
        {
            var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
            using AudioBuffer? buffer = composer.Compose(range);

            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(composer.Dispose);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(cleanupFailure));
                Assert.That(sound.NodeDisposeCalls, Is.EqualTo(1));
                Assert.That(sound.ResourceDisposeCalls, Is.EqualTo(1),
                    "SceneCompositor cleanup must run even when Composer cache cleanup fails first");
            });
        }
        finally
        {
            Assert.DoesNotThrow(composer.Dispose);
            if (Directory.Exists(basePath))
            {
                Directory.Delete(basePath, recursive: true);
            }
        }
    }
}

internal sealed partial class SceneComposerThrowingDisposeSound(Exception cleanupFailure) : Sound
{
    public int NodeDisposeCalls { get; private set; }

    public int ResourceDisposeCalls { get; private set; }

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var node = context.AddNode(new ThrowingDisposeAudioNode(this, cleanupFailure));
        context.MarkAsOutput(node);
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                GetOriginal().ResourceDisposeCalls++;
            }
        }
    }

    private sealed class ThrowingDisposeAudioNode(
        SceneComposerThrowingDisposeSound owner,
        Exception failure) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                owner.NodeDisposeCalls++;
                throw failure;
            }
        }
    }
}

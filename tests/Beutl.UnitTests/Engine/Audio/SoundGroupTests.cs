using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SoundGroupTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    // An edit composes the group again; its children keep their nodes, and with them the state of a playing stream.
    [Test]
    public void Compose_ReusesTheChildNodesOfThePreviousGraph()
    {
        string path = TestMediaHelper.CreateTestAudioFile(sampleRate: 44100, channels: 2, durationSeconds: 1.0);
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));
        var child = new SourceSound { Source = { Value = source }, TimeRange = { Value = range } };
        var group = new SoundGroup { TimeRange = { Value = range } };
        group.Children.Add(child);
        using var resource = group.ToResource(CompositionContext.Default);

        var first = new AudioContext(44100, 2);
        group.Compose(first, resource);
        AudioNode[] previous = [.. first.Nodes];

        using var second = new AudioContext(44100, 2);
        second.BeginUpdate(previous);
        group.Compose(second, resource);
        second.EndUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(previous.OfType<SourceNode>(), Is.Not.Empty);
            Assert.That(second.Nodes, Is.EquivalentTo(previous));
        });
    }

    // A child whose source has no audio composes no node, which left the group's mixer without inputs; the
    // first window threw instead of playing silence, and playback and export stopped (#3622).
    [Test]
    public void Compose_WithoutChildAudio_PlaysSilence()
    {
        string path = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 30);
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));
        var child = new SourceSound { Source = { Value = source }, TimeRange = { Value = range } };
        var group = new SoundGroup { TimeRange = { Value = range } };
        group.Children.Add(child);
        using var resource = group.ToResource(CompositionContext.Default);
        var frame = new CompositionFrame(
            ImmutableArray.Create((EngineObject.Resource)resource), range, default, new CompositionEligibility([group]));
        using var composer = new Composer { SampleRate = 44100 };

        using AudioBuffer? buffer = composer.Compose(range, frame);

        Assert.That(buffer!.GetChannelData(0).ToArray(), Is.All.EqualTo(0f));
    }
}

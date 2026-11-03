using System.Text.Json.Nodes;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneMasterVolumeTests
{
    private static readonly TimeRange s_window = new(MediaTime.Zero, MediaTime.FromSeconds(0.05));

    [TestCase(100f, 0.25f)]
    [TestCase(50f, 0.125f)]
    [TestCase(0f, 0f)]
    public void The_mix_follows_the_master_volume(float volume, float expected)
    {
        Scene scene = CreateScene();
        scene.MasterVolume.Value = volume;
        using var composer = new SceneComposer(scene, disableResourceShare: true, forceOriginalSource: true);

        using AudioBuffer buffer = composer.Compose(s_window)!;

        Assert.That(buffer.GetChannelData(0)[100], Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void A_composer_already_playing_picks_up_a_change()
    {
        Scene scene = CreateScene();
        using var composer = new SceneComposer(scene, disableResourceShare: true, forceOriginalSource: true);
        using (AudioBuffer first = composer.Compose(s_window)!)
            Assert.That(first.GetChannelData(0)[100], Is.EqualTo(0.25f).Within(0.0001f));

        scene.MasterVolume.Value = 200f;

        using AudioBuffer second = composer.Compose(s_window)!;
        Assert.That(second.GetChannelData(0)[100], Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void A_scene_embedded_in_another_brings_its_master_volume()
    {
        Scene inner = CreateScene();
        inner.MasterVolume.Value = 50f;
        var outer = new Scene(640, 480, "outer") { Duration = { Value = MediaTime.FromSeconds(5) } };
        var clip = new Element { Length = { Value = MediaTime.FromSeconds(5) } };
        clip.Objects.Add(new SceneSound { ReferencedScene = { Value = inner } });
        outer.Children.Add(clip);
        using var composer = new SceneComposer(outer, disableResourceShare: true, forceOriginalSource: true);

        using AudioBuffer buffer = composer.Compose(s_window)!;

        Assert.That(buffer.GetChannelData(0)[100], Is.EqualTo(0.125f).Within(0.0001f));
    }

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    public void A_value_below_silence_is_silence(float value)
    {
        var scene = new Scene(640, 480, "scene");

        scene.MasterVolume.Value = value;

        Assert.That(scene.MasterVolume.Value, Is.Zero);
    }

    [Test]
    public void It_is_saved_with_the_scene_and_a_scene_saved_without_it_stays_at_full_volume()
    {
        string directory = Directory.CreateTempSubdirectory("beutl-master-volume-").FullName;
        try
        {
            var uri = new Uri(Path.Combine(directory, "main.scene"));
            CoreSerializer.StoreToUri(new Scene(640, 480, "scene") { Uri = uri, MasterVolume = { Value = 30f } }, uri);
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).MasterVolume.Value, Is.EqualTo(30f));

            JsonObject json = JsonNode.Parse(File.ReadAllText(uri.LocalPath))!.AsObject();
            Assert.That(json.Remove("MasterVolume"), Is.True);
            File.WriteAllText(uri.LocalPath, json.ToJsonString());
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).MasterVolume.Value, Is.EqualTo(100f));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static Scene CreateScene()
    {
        var scene = new Scene(640, 480, "scene") { Duration = { Value = MediaTime.FromSeconds(5) } };
        var clip = new Element { Length = { Value = MediaTime.FromSeconds(5) } };
        clip.Objects.Add(new QuarterLevelSound());
        scene.Children.Add(clip);
        return scene;
    }
}

// Every sample is 0.25, which is far enough below full scale for the master limiter to leave alone.
public sealed partial class QuarterLevelSound : Sound
{
    public QuarterLevelSound() => ScanProperties<QuarterLevelSound>();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        context.MarkAsOutput(context.AddNode(new QuarterLevelNode()));
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }

    private sealed class QuarterLevelNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            var buffer = new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
            for (int ch = 0; ch < buffer.ChannelCount; ch++)
                buffer.GetChannelData(ch).Fill(0.25f);
            return buffer;
        }
    }
}

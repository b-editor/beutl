using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Serialization;
using Beutl.Audio;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public sealed class ProjectRateTests
{
    [TestCase("24", 48000, "24", 48000)]
    [TestCase("30000/1001", 48000, "30000/1001", 48000)]
    [TestCase("0", -1, "30", 44100)]
    [TestCase("-24", 0, "30", 44100)]
    [TestCase("30000/0", 48000, "30", 48000)]
    [TestCase("-30000/1001", 48000, "30", 48000)]
    public void Reads_positive_project_rates_or_uses_defaults(
        string frameRate, int sampleRate, string expectedFrameRate, int expectedSampleRate)
    {
        var project = new Project
        {
            FrameRate = { Value = Rational.Parse(frameRate, CultureInfo.InvariantCulture) },
            SampleRate = { Value = sampleRate },
        };

        Assert.Multiple(() =>
        {
            Assert.That(project.GetFrameRate(), Is.EqualTo(Rational.Parse(expectedFrameRate, CultureInfo.InvariantCulture)));
            Assert.That(project.GetSampleRate(), Is.EqualTo(expectedSampleRate));
        });
    }

    [Test]
    public void Detached_scene_uses_defaults()
    {
        Project? project = null;
        Assert.Multiple(() =>
        {
            Assert.That(project.GetFrameRate(), Is.EqualTo(new Rational(30)));
            Assert.That(project.GetSampleRate(), Is.EqualTo(44100));
        });
    }

    [Test]
    public void ProjectLayout_DefaultsToStereoAndSurvivesSerialization()
    {
        Project? detached = null;
        Assert.That(detached.GetChannelLayout(), Is.EqualTo(AudioChannelLayout.Stereo));
        Assert.That(Read(new JsonObject()).ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Stereo));
        foreach (SpeakerLayout layout in Enum.GetValues<SpeakerLayout>())
        {
            var project = new Project { ChannelLayout = { Value = layout } };
            var restored = Read(CoreSerializer.SerializeToJsonObject(project));
            Assert.That(restored.ChannelLayout.Value, Is.EqualTo(layout));
            Assert.That(restored.GetChannelLayout(), Is.EqualTo(layout.ToAudioChannelLayout()));
        }
    }

    // Earlier versions kept the rates as variables, which a read moves to the rate members once.
    [TestCase("24", "48000", "24", 48000)]
    [TestCase("30000/1001", "96000", "30000/1001", 96000)]
    [TestCase("invalid", "invalid", "30", 44100)]
    [TestCase("2147483648", "2147483648", "2147483648", 44100)]
    public void A_project_of_an_earlier_version_moves_its_rate_variables_to_the_rates(
        string frameRate, string sampleRate, string expectedFrameRate, int expectedSampleRate)
    {
        Project project = Read(new JsonObject
        {
            ["variables"] = new JsonObject { ["framerate"] = frameRate, ["samplerate"] = sampleRate, ["author"] = "beutl" },
        });
        JsonObject saved = CoreSerializer.SerializeToJsonObject(project);

        Assert.Multiple(() =>
        {
            Assert.That(project.FrameRate.Value, Is.EqualTo(Rational.Parse(expectedFrameRate, CultureInfo.InvariantCulture)));
            Assert.That(project.SampleRate.Value, Is.EqualTo(expectedSampleRate));
            Assert.That(project.Variables.Value, Is.EquivalentTo(new Dictionary<string, string> { ["author"] = "beutl" }));
            Assert.That((string?)saved["FrameRate"], Is.EqualTo(expectedFrameRate));
            Assert.That((int?)saved["SampleRate"], Is.EqualTo(expectedSampleRate));
            Assert.That(saved["variables"]!.AsObject().Select(pair => pair.Key), Is.EqualTo(new[] { "author" }));
        });
    }

    [Test]
    public void The_rates_win_over_rate_variables_left_in_a_file()
    {
        Project project = Read(new JsonObject
        {
            ["FrameRate"] = "30000/1001",
            ["SampleRate"] = 48000,
            ["variables"] = new JsonObject { ["framerate"] = "60", ["samplerate"] = "22050" },
        });

        Assert.Multiple(() =>
        {
            Assert.That(project.FrameRate.Value, Is.EqualTo(new Rational(30000, 1001)));
            Assert.That(project.SampleRate.Value, Is.EqualTo(48000));
            Assert.That(project.Variables.Value, Is.Empty);
        });
    }

    private static Project Read(JsonObject json)
    {
        json["items"] = new JsonArray();
        return (Project)CoreSerializer.DeserializeFromJsonObject(json, typeof(Project));
    }
}

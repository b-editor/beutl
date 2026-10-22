using System.Globalization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public sealed class ProjectRateTests
{
    [TestCase("24", "48000", "24", 48000)]
    [TestCase("60", "96000", "60", 96000)]
    [TestCase("30000/1001", "48000", "30000/1001", 48000)]
    [TestCase(null, null, "30", 44100)]
    [TestCase("invalid", "invalid", "30", 44100)]
    [TestCase("0", "-1", "30", 44100)]
    [TestCase("-24", "0", "30", 44100)]
    [TestCase("30000/0", "48000", "30", 48000)]
    [TestCase("-30000/1001", "48000", "30", 48000)]
    [TestCase("2147483648", "2147483648", "2147483648", 44100)]
    public void Reads_positive_project_rates_or_uses_defaults(
        string? frameRate, string? sampleRate, string expectedFrameRate, int expectedSampleRate)
    {
        var project = new Project();
        if (frameRate != null) project.Variables[ProjectVariableKeys.FrameRate] = frameRate;
        if (sampleRate != null) project.Variables[ProjectVariableKeys.SampleRate] = sampleRate;

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
}

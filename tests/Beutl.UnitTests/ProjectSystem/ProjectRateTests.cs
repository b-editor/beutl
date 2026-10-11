namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public sealed class ProjectRateTests
{
    [TestCase("24", "48000", 24, 48000)]
    [TestCase("60", "96000", 60, 96000)]
    [TestCase(null, null, 30, 44100)]
    [TestCase("invalid", "invalid", 30, 44100)]
    [TestCase("0", "-1", 30, 44100)]
    [TestCase("-24", "0", 30, 44100)]
    [TestCase("2147483648", "2147483648", 30, 44100)]
    public void Reads_positive_project_rates_or_uses_defaults(
        string? frameRate, string? sampleRate, int expectedFrameRate, int expectedSampleRate)
    {
        var project = new Project();
        if (frameRate != null) project.Variables[ProjectVariableKeys.FrameRate] = frameRate;
        if (sampleRate != null) project.Variables[ProjectVariableKeys.SampleRate] = sampleRate;

        Assert.Multiple(() =>
        {
            Assert.That(project.GetFrameRate(), Is.EqualTo(expectedFrameRate));
            Assert.That(project.GetSampleRate(), Is.EqualTo(expectedSampleRate));
        });
    }

    [Test]
    public void Detached_scene_uses_defaults()
    {
        Project? project = null;
        Assert.Multiple(() =>
        {
            Assert.That(project.GetFrameRate(), Is.EqualTo(30));
            Assert.That(project.GetSampleRate(), Is.EqualTo(44100));
        });
    }
}

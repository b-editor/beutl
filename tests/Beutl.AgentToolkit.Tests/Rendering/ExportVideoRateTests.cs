using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

[TestFixture]
public sealed class ExportVideoRateTests
{
    [TestCase(60, 48000, false)]
    [TestCase(24, 48000, false)]
    [TestCase(60, 48000, true)]
    [TestCase(null, null, false)]
    public async Task Export_uses_project_rates_unless_explicitly_overridden(
        int? frameRate, int? sampleRate, bool overrideRates)
    {
        string directory = TestWorkspace.CreateDirectory();
        var scene = new Scene(64, 64, "rates")
        {
            Uri = new Uri(Path.Combine(directory, "rates.scene")),
            Duration = MediaTime.FromSeconds(1)
        };
        if (frameRate.HasValue && sampleRate.HasValue)
        {
            var project = new Project();
            project.Variables[ProjectVariableKeys.FrameRate] = frameRate.Value.ToString();
            project.Variables[ProjectVariableKeys.SampleRate] = sampleRate.Value.ToString();
            project.Items.Add(scene);
        }

        using var session = new AgentToolkitTestSession(scene);
        Rational encodedFrameRate = default;
        long encodedSampleRate = 0;
        long encodedSampleCount = 0;
        var encoder = new DelegateEncoder(".rate-test", async (controller, frames, samples, token) =>
        {
            encodedFrameRate = frames.FrameRate;
            encodedSampleRate = samples.SampleRate;
            encodedSampleCount = samples.SampleCount;
            using var audio = (await samples.ReadNextSamples((int)samples.SampleCount))!;
            Assert.That(audio.SampleRate, Is.EqualTo(samples.SampleRate));
            Assert.That(audio.SampleCount, Is.EqualTo(samples.SampleCount));
            await File.WriteAllTextAsync(controller.OutputFile, "encoded", token);
        });
        using var jobs = new RenderJobManager();
        RenderTools tools = TestRenderTools.Create(session.CreateManager(), new EncoderRegistration(encoder), jobs);
        string output = Path.Combine(directory, "rates.rate-test");

        ToolResult<ExportVideoResult> result = overrideRates
            ? await tools.ExportVideo(output, 24000, 1001, 96000)
            : await tools.ExportVideo(output);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Rational expectedFrameRate = overrideRates ? new Rational(24000, 1001) : new Rational(frameRate ?? 30, 1);
        int expectedSampleRate = overrideRates ? 96000 : sampleRate ?? 44100;
        Assert.Multiple(() =>
        {
            Assert.That(encoder.Controller!.VideoSettings.FrameRate, Is.EqualTo(expectedFrameRate));
            Assert.That(encoder.Controller.AudioSettings.SampleRate, Is.EqualTo(expectedSampleRate));
            Assert.That(encodedFrameRate, Is.EqualTo(expectedFrameRate));
            Assert.That(encodedSampleRate, Is.EqualTo(expectedSampleRate));
            Assert.That(encodedSampleCount, Is.EqualTo(expectedSampleRate));
        });
    }
}

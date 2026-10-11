using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Editor;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

[TestFixture]
public sealed class ExportVideoRateTests
{
    [TestCase(60, 48000, EditingSessionSource.File, false)]
    [TestCase(24, 48000, EditingSessionSource.LiveEditor, false)]
    [TestCase(60, 48000, EditingSessionSource.File, true)]
    [TestCase(60, 48000, EditingSessionSource.LiveEditor, true)]
    [TestCase(null, null, EditingSessionSource.File, false)]
    public async Task Export_uses_project_rates_unless_explicitly_overridden(
        int? frameRate, int? sampleRate, EditingSessionSource source, bool overrideRates)
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scene = new Scene(64, 64, "rates")
        {
            Uri = new Uri(Path.Combine(directory, "rates.scene")),
            Duration = TimeSpan.FromSeconds(1)
        };
        if (frameRate.HasValue && sampleRate.HasValue)
        {
            var project = new Project();
            project.Variables[ProjectVariableKeys.FrameRate] = frameRate.Value.ToString();
            project.Variables[ProjectVariableKeys.SampleRate] = sampleRate.Value.ToString();
            project.Items.Add(scene);
        }

        using var session = new AgentToolkitTestSession(scene, source);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var encoder = new RecordingEncoder();
        var renderer = new StillRenderer();
        using var jobs = new RenderJobManager();
        var tools = new RenderTools(manager, new DestructiveGuard(), renderer, new StoryboardRenderer(),
            new FrameDifferenceAnalyzer(renderer), new AudioRhythmAnalyzer(),
            new VideoExporter(new EncoderRegistration(encoder)), jobs,
            StandaloneOutputOperationLeaseProvider.Instance);
        string output = Path.Combine(directory, "rates.rate-test");

        ToolResult<ExportVideoResult> result = overrideRates
            ? await tools.ExportVideo(output, 24000, 1001, 96000)
            : await tools.ExportVideo(output);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Rational expectedFrameRate = overrideRates ? new Rational(24000, 1001) : new Rational(frameRate ?? 30, 1);
        int expectedSampleRate = overrideRates ? 96000 : sampleRate ?? 44100;
        Assert.Multiple(() =>
        {
            Assert.That(encoder.Controller.VideoSettings.FrameRate, Is.EqualTo(expectedFrameRate));
            Assert.That(encoder.Controller.AudioSettings.SampleRate, Is.EqualTo(expectedSampleRate));
            Assert.That(encoder.Controller.FrameRate, Is.EqualTo(expectedFrameRate));
            Assert.That(encoder.Controller.SampleRate, Is.EqualTo(expectedSampleRate));
            Assert.That(encoder.Controller.SampleCount, Is.EqualTo(expectedSampleRate));
        });
    }

    private sealed class RecordingEncoder : ControllableEncodingExtension
    {
        public RecordingController Controller { get; private set; } = null!;
        public override IEnumerable<string> SupportExtensions() => [".rate-test"];
        public override EncodingController CreateController(string file) => Controller = new RecordingController(file);
    }

    private sealed class RecordingController(string file) : EncodingController(file)
    {
        public override VideoEncoderSettings VideoSettings { get; } = new();
        public override AudioEncoderSettings AudioSettings { get; } = new();
        public Rational FrameRate { get; private set; }
        public long SampleRate { get; private set; }
        public long SampleCount { get; private set; }

        public override async ValueTask Encode(IFrameProvider frames, ISampleProvider samples, CancellationToken token)
        {
            FrameRate = frames.FrameRate;
            SampleRate = samples.SampleRate;
            SampleCount = samples.SampleCount;
            using var pcm = await samples.Sample(0, samples.SampleCount);
            Assert.That(pcm.SampleRate, Is.EqualTo(samples.SampleRate));
            Assert.That(pcm.NumSamples, Is.EqualTo(samples.SampleCount));
            await File.WriteAllTextAsync(OutputFile, "encoded", token);
        }
    }
}

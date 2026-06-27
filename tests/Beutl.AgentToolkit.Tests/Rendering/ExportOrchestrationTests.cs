using Beutl.AgentToolkit.Rendering;
using Beutl.Extensions.FFmpeg;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class ExportOrchestrationTests
{
    [Test]
    public void Encoder_registration_resolves_builtin_ffmpeg_and_rejects_unknown_extension()
    {
        var registration = new EncoderRegistration();

        Assert.Multiple(() =>
        {
            Assert.That(registration.FindForOutput("movie.mp4"), Is.Not.Null);
            Assert.That(registration.FindForOutput("movie.unknown"), Is.Null);
        });
    }

    [Test]
    public void Missing_encoder_surfaces_codec_unavailable()
    {
        var exporter = new VideoExporter(new EncoderRegistration());
        var scene = new Scene(64, 64, "export") { Duration = TimeSpan.FromSeconds(1) };

        Assert.ThrowsAsync<CodecUnavailableException>(async () =>
            await exporter.ExportAsync(
                scene,
                Path.Combine(CreateWorkspace(), "movie.unknown"),
                new Rational(30, 1),
                44100,
                1,
                CancellationToken.None));
    }

    [Test]
    public void Missing_ffmpeg_libraries_surface_codec_unavailable_without_starting_worker()
    {
        var exporter = new VideoExporter(new EncoderRegistration());
        var scene = new Scene(64, 64, "export") { Duration = TimeSpan.FromSeconds(1) };
        FFmpegLibraryState.MarkMissing();

        try
        {
            Assert.ThrowsAsync<CodecUnavailableException>(async () =>
                await exporter.ExportAsync(
                    scene,
                    Path.Combine(CreateWorkspace(), "movie.mp4"),
                    new Rational(30, 1),
                    44100,
                    1,
                    CancellationToken.None));
        }
        finally
        {
            FFmpegLibraryState.MarkInstalled();
        }
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

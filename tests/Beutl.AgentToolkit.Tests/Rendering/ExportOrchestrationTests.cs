using Beutl.AgentToolkit.Rendering;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Graphics.Rendering;
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
    public void Export_composer_forces_original_source_for_audio()
    {
        var scene = new Scene(64, 64, "export") { Duration = TimeSpan.FromSeconds(1) };

        using SceneComposer composer = VideoExporter.CreateExportComposer(scene, 44100);

        Assert.Multiple(() =>
        {
            Assert.That(composer.Compositor.ForceOriginalSource, Is.True);
            Assert.That(composer.Compositor.DisableResourceShare, Is.True);
            Assert.That(composer.Compositor.RenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(composer.SampleRate, Is.EqualTo(44100));
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
            // .mkv is FFmpeg-only (AVFoundation does not support it), so there is no fallback
            // encoder and the missing-libraries failure surfaces as CodecUnavailable on every OS.
            Assert.ThrowsAsync<CodecUnavailableException>(async () =>
                await exporter.ExportAsync(
                    scene,
                    Path.Combine(CreateWorkspace(), "movie.mkv"),
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

    [Test]
    public void Shared_container_falls_back_to_avfoundation_after_ffmpeg_on_macos()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("AVFoundation is only registered on macOS.");
        }

        IReadOnlyList<ControllableEncodingExtension> candidates =
            new EncoderRegistration().FindAllForOutput("movie.mp4");

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(candidates[0], Is.InstanceOf<FFmpegHeadlessEncodingExtension>());
            Assert.That(
                candidates.Any(encoder => encoder is Beutl.Extensions.AVFoundation.Encoding.AVFEncodingExtension),
                Is.True);
        });
    }

    [Test]
    public void Missing_worker_falls_through_to_avfoundation_instead_of_codec_unavailable()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("AVFoundation is only registered on macOS.");
        }

        if (FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
        {
            Assert.Ignore("An FFmpeg worker is present next to the test host; the fallback path is not exercised.");
        }

        var exporter = new VideoExporter(new EncoderRegistration());
        var scene = new Scene(64, 64, "export") { Duration = TimeSpan.FromSeconds(1) };
        string output = Path.Combine(CreateWorkspace(), "movie.mov");

        try
        {
            exporter.ExportAsync(scene, output, new Rational(30, 1), 44100, 1, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            Assert.That(File.Exists(output), Is.True);
        }
        catch (CodecUnavailableException)
        {
            Assert.Fail("The FFmpeg worker being absent must fall through to AVFoundation, not surface codec_unavailable.");
        }
        catch (Exception ex)
        {
            // The FFmpeg skip is what matters here; AVFoundation itself may not run in a headless runner.
            Assert.Ignore($"AVFoundation encode could not run in this environment: {ex.GetType().Name}.");
        }
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

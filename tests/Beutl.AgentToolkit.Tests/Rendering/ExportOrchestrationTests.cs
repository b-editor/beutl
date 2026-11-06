using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
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
            Assert.That(registration.FindAllForOutput("movie.mp4"), Is.Not.Empty);
            Assert.That(registration.FindAllForOutput("movie.unknown"), Is.Empty);
        });
    }

    [TestCase(SpeakerLayout.Mono)]
    [TestCase(SpeakerLayout.Stereo)]
    [TestCase(SpeakerLayout.Surround51)]
    [TestCase(SpeakerLayout.Surround71)]
    public void Export_composer_uses_the_project_layout_and_forces_original_source_for_audio(SpeakerLayout layout)
    {
        var scene = new Scene(64, 64, "export") { Duration = { Value = MediaTime.FromSeconds(1) } };
        var project = new Project { ChannelLayout = { Value = layout }, Items = { scene } };

        using SceneComposer composer = ExportRendererFactory.CreateComposer(scene, 44100);

        Assert.Multiple(() =>
        {
            Assert.That(composer.Compositor.ForceOriginalSource, Is.True);
            Assert.That(composer.Compositor.DisableResourceShare, Is.True);
            Assert.That(composer.SampleRate, Is.EqualTo(44100));
            Assert.That(composer.ChannelLayout, Is.EqualTo(project.GetChannelLayout()));
        });
    }

    [Test]
    public async Task Export_uses_and_reports_the_project_channel_count()
    {
        var encoder = new DelegateEncoder(".layout-test", async (controller, frames, samples, token) =>
        {
            Assert.That(controller.AudioSettings.Channels.Value, Is.EqualTo(6));
            Assert.That(controller.AudioSettings.Bitrate.Value, Is.EqualTo(384_000));
            Assert.That(samples.ChannelLayout, Is.EqualTo(AudioChannelLayout.Surround51));
            using var audio = await samples.ReadNextSamples(32);
            Assert.That(audio!.ChannelCount, Is.EqualTo(6));
            await File.WriteAllTextAsync(controller.OutputFile, "encoded output", token);
        });
        var scene = new Scene(64, 64, "layout") { Duration = { Value = MediaTime.FromSeconds(0.1) } };
        var project = new Project { Items = { scene }, ChannelLayout = { Value = SpeakerLayout.Surround51 } };
        var exporter = new VideoExporter(new EncoderRegistration(encoder));
        var response = await exporter.ExportAsync(scene, Path.Combine(TestWorkspace.CreateDirectory(), "movie.layout-test"),
            new Rational(30), 48000, 1, CancellationToken.None);
        Assert.That(response.AudioChannels, Is.EqualTo(project.GetChannelLayout().ChannelCount));
    }

    [Test]
    public async Task Missing_encoder_surfaces_codec_unavailable()
    {
        var exporter = new VideoExporter(new EncoderRegistration());
        var scene = new Scene(64, 64, "export") { Duration = { Value = MediaTime.FromSeconds(1) } };

        await Assert.ThrowsAsync<CodecUnavailableException>(async () =>
            await exporter.ExportAsync(
                scene,
                Path.Combine(TestWorkspace.CreateDirectory(), "movie.unknown"),
                new Rational(30, 1),
                44100,
                1,
                CancellationToken.None));
    }

    [Test]
    public async Task Missing_ffmpeg_libraries_surface_codec_unavailable_without_starting_worker()
    {
        var exporter = new VideoExporter(new EncoderRegistration());
        var scene = new Scene(64, 64, "export") { Duration = { Value = MediaTime.FromSeconds(1) } };
        FFmpegLibraryState.MarkMissing();

        try
        {
            // .mkv is FFmpeg-only (AVFoundation does not support it), so there is no fallback
            // encoder and the missing-libraries failure surfaces as CodecUnavailable on every OS.
            await Assert.ThrowsAsync<CodecUnavailableException>(async () =>
                await exporter.ExportAsync(
                    scene,
                    Path.Combine(TestWorkspace.CreateDirectory(), "movie.mkv"),
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
    public async Task Frame_progress_reaches_total_only_after_encode_returns()
    {
        var progress = new List<(long Completed, long Total)>();
        bool completedWasReportedBeforeReturn = false;
        var encoder = new DelegateEncoder(".progress-test", async (controller, frames, _, token) =>
        {
            for (long frame = 0; frame < frames.FrameCount; frame++)
            {
                token.ThrowIfCancellationRequested();
                using Bitmap? bitmap = await frames.ReadNextFrame();
                completedWasReportedBeforeReturn |= progress.Any(item => item.Total > 0 && item.Completed == item.Total);
            }
            await File.WriteAllTextAsync(controller.OutputFile, "encoded output", token);
        });
        var exporter = new VideoExporter(new EncoderRegistration(encoder));
        var scene = new Scene(64, 64, "progress") { Duration = { Value = MediaTime.FromSeconds(0.1) } };

        ExportVideoResponse response = await exporter.ExportAsync(
            scene,
            Path.Combine(TestWorkspace.CreateDirectory(), "movie.progress-test"),
            new Rational(30, 1),
            44100,
            1,
            CancellationToken.None,
            onFrameProgress: (completed, total) => progress.Add((completed, total)));

        Assert.Multiple(() =>
        {
            Assert.That(response.Frames, Is.EqualTo(3));
            Assert.That(completedWasReportedBeforeReturn, Is.False);
            Assert.That(progress.Select(item => item.Completed), Is.EqualTo(new long[] { 0, 1, 2, 3 }));
            Assert.That(progress, Has.All.Matches<(long Completed, long Total)>(item => item.Total == 3));
            Assert.That(progress[^1], Is.EqualTo((3L, 3L)));
        });
    }

    [Test]
    public async Task Audio_only_output_gets_no_frames_and_ignores_missing_video_sources()
    {
        string directory = TestWorkspace.CreateDirectory();
        long framesSeen = -1;
        long samplesSeen = -1;
        var encoder = new DelegateEncoder(".audio-only", async (controller, frames, samples, token) =>
        {
            framesSeen = frames.FrameCount;
            samplesSeen = samples.SampleCount;
            using Bitmap? frame = await frames.ReadNextFrame();
            Assert.That(frame, Is.Null);
            await File.WriteAllTextAsync(controller.OutputFile, "encoded output", token);
        }, videoEnabled: false);
        var scene = new Scene(64, 64, "audio-only") { Duration = { Value = MediaTime.FromSeconds(0.1) } };
        _ = new Project { Items = { scene } };
        scene.Children.Add(CreateMissingVideoElement(directory));
        var exporter = new VideoExporter(new EncoderRegistration(encoder));

        ExportVideoResponse response = await exporter.ExportAsync(
            scene,
            Path.Combine(directory, "sound.audio-only"),
            new Rational(30, 1),
            44100,
            1,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(framesSeen, Is.Zero);
            Assert.That(samplesSeen, Is.EqualTo(4410));
            Assert.That(response.Frames, Is.Zero);
            Assert.That(response.Samples, Is.EqualTo(4410));
        });
    }

    [Test]
    public async Task Output_with_video_still_rejects_missing_video_sources()
    {
        string directory = TestWorkspace.CreateDirectory();
        var encoder = new DelegateEncoder(".with-video", (_, _, _, _) => Task.CompletedTask);
        var scene = new Scene(64, 64, "with-video") { Duration = { Value = MediaTime.FromSeconds(0.1) } };
        _ = new Project { Items = { scene } };
        scene.Children.Add(CreateMissingVideoElement(directory));
        var exporter = new VideoExporter(new EncoderRegistration(encoder));

        await Assert.ThrowsAsync<RenderingUnavailableException>(async () =>
            await exporter.ExportAsync(
                scene,
                Path.Combine(directory, "movie.with-video"),
                new Rational(30, 1),
                44100,
                1,
                CancellationToken.None));
    }

    private static Element CreateMissingVideoElement(string directory)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(Path.Combine(directory, "missing.mov")));
        var drawable = new SourceVideo();
        drawable.Source.Value = source;
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(directory, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        return element;
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
        var scene = new Scene(64, 64, "export") { Duration = { Value = MediaTime.FromSeconds(1) } };
        string output = Path.Combine(TestWorkspace.CreateDirectory(), "movie.mov");

        // Only the export call may ignore environment failures: assertions run outside the try so
        // a wrong Encoder/Warnings value fails instead of being converted into Assert.Ignore.
        ExportVideoResponse? response = null;
        try
        {
            response = exporter.ExportAsync(
                    scene,
                    output,
                    new Rational(30, 1),
                    44100,
                    1,
                    CancellationToken.None,
                    crf: 28)
                .AsTask()
                .GetAwaiter()
                .GetResult();
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

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(output), Is.True);
            Assert.That(response!.Encoder, Is.EqualTo("AVFoundation"));
            Assert.That(
                response.Warnings,
                Has.Some.Contains("AVFoundation").And.Some.Contains("crf").And.Some.Contains("ignored"));
        });
    }
}

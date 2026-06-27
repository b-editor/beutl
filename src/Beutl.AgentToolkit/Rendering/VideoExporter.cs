using System.Reactive.Subjects;
using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed record ExportVideoResponse(string OutputPath, long Frames, long Samples, string Duration);

public sealed class VideoExporter(EncoderRegistration encoders)
{
    public async ValueTask<ExportVideoResponse> ExportAsync(
        Scene scene,
        string outputPath,
        Rational frameRate,
        int sampleRate,
        float renderScale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        ControllableEncodingExtension encoder = encoders.FindForOutput(outputPath)
                                                 ?? throw new CodecUnavailableException(
                                                     $"No encoder is registered for '{Path.GetExtension(outputPath)}'.");

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        float normalizedScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        int normalizedSampleRate = sampleRate > 0 ? sampleRate : 44100;

        try
        {
            EncodingController controller = encoder.CreateController(outputPath);
            controller.VideoSettings.SourceSize = scene.FrameSize;
            controller.VideoSettings.DestinationSize = scene.FrameSize;
            controller.VideoSettings.FrameRate = frameRate;
            controller.AudioSettings.SampleRate = normalizedSampleRate;
            controller.AudioSettings.Channels = 2;

            using var renderer = new SceneRenderer(scene, normalizedScale, disableResourceShare: true);
            renderer.CacheOptions = RenderCacheOptions.Disabled;
            using var frameProgress = new Subject<TimeSpan>();
            using var frameProvider = new FrameProviderImpl(scene, frameRate, renderer, frameProgress);
            using var composer = new SceneComposer(scene, disableResourceShare: true) { SampleRate = normalizedSampleRate };
            using var sampleProgress = new Subject<TimeSpan>();
            using var sampleProvider = new SampleProviderImpl(scene, composer, normalizedSampleRate, sampleProgress);

            await controller.Encode(frameProvider, sampleProvider, cancellationToken).ConfigureAwait(false);

            return new ExportVideoResponse(
                outputPath,
                frameProvider.FrameCount,
                sampleProvider.SampleCount,
                scene.Duration.ToString("c"));
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            throw new CodecUnavailableException("FFmpeg libraries are not available.", ex);
        }
        catch (FFmpegWorkerException ex)
        {
            throw new CodecUnavailableException(ex.Message, ex);
        }
    }
}

using System.Globalization;
using System.Reactive.Subjects;
using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Models;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed record ExportVideoResponse(string OutputPath, long Frames, long Samples, string Duration);

public sealed record ExportVideoResult(
    string Status,
    string? JobId,
    ExportVideoResponse? Result);

public sealed class VideoExporter(EncoderRegistration encoders)
{
    public async ValueTask<ExportVideoResponse> ExportAsync(
        Scene scene,
        string outputPath,
        Rational frameRate,
        int sampleRate,
        float renderScale,
        CancellationToken cancellationToken,
        int? crf = null,
        int? bitrate = null)
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
            ApplyQualitySettings(controller.VideoSettings, crf, bitrate);

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

    internal static void ApplyQualitySettings(VideoEncoderSettings settings, int? crf, int? bitrate)
    {
        if (settings is FFmpegVideoEncoderSettings ffmpeg)
        {
            if (crf is int crfValue)
            {
                SetOption(ffmpeg.Options, "crf", crfValue.ToString(CultureInfo.InvariantCulture));
            }

            if (bitrate is int bitrateValue)
            {
                settings.Bitrate = bitrateValue;
                // libx264 ignores the target bitrate while a crf option is present, so drop crf for ABR.
                RemoveOption(ffmpeg.Options, "crf");
            }
        }
        else if (bitrate is int bitrateValue)
        {
            settings.Bitrate = bitrateValue;
        }
    }

    private static void SetOption(CoreList<AdditionalOption> options, string name, string value)
    {
        foreach (AdditionalOption option in options)
        {
            if (string.Equals(option.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                option.Value = value;
                return;
            }
        }

        options.Add(new AdditionalOption(name, value));
    }

    private static void RemoveOption(CoreList<AdditionalOption> options, string name)
    {
        for (int i = options.Count - 1; i >= 0; i--)
        {
            if (string.Equals(options[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                options.RemoveAt(i);
            }
        }
    }
}

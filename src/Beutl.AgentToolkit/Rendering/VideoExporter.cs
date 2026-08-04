using System.Globalization;
using System.Reactive.Subjects;
using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Models;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed record ExportVideoResponse(
    string OutputPath,
    long Frames,
    long Samples,
    string Duration,
    string Encoder,
    IReadOnlyList<string> Warnings);

public sealed record ExportVideoResult(
    string Status,
    string? JobId,
    ExportVideoResponse? Result);

public sealed class VideoExporter(EncoderRegistration encoders)
{
    // True only when FFmpeg is the sole encoder for this container, so a missing worker leaves no
    // fallback. AVFoundation (macOS .mp4/.mov) makes this false and the export must not preflight-reject.
    public bool RequiresFFmpegWorker(string outputPath)
    {
        IReadOnlyList<ControllableEncodingExtension> candidates = encoders.FindAllForOutput(outputPath);
        return candidates.Count > 0 && candidates.All(encoder => encoder is FFmpegHeadlessEncodingExtension);
    }

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

        IReadOnlyList<ControllableEncodingExtension> candidates = encoders.FindAllForOutput(outputPath);
        if (candidates.Count == 0)
        {
            throw new CodecUnavailableException(
                $"No encoder is registered for '{Path.GetExtension(outputPath)}'.");
        }

        // Scene3DRenderNode silently renders nothing without a 3D-capable context, so exporting
        // would succeed with the 3D layers missing; fail up front like render_still does.
        if (StillRenderer.ContainsGpuOnlyContent(scene)
            && !await StillRenderer.Has3DGraphicsContextAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new RenderingUnavailableException(
                "The scene contains 3D content, but no GPU context with 3D rendering support is available.");
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        float normalizedScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        int normalizedSampleRate = sampleRate > 0 ? sampleRate : 44100;

        async Task<ExportVideoResponse> EncodeWithAsync(ControllableEncodingExtension encoder)
        {
            EncodingController controller = encoder.CreateController(outputPath);
            controller.VideoSettings.SourceSize = scene.FrameSize;
            controller.VideoSettings.DestinationSize = scene.FrameSize;
            controller.VideoSettings.FrameRate = frameRate;
            controller.AudioSettings.SampleRate = normalizedSampleRate;
            controller.AudioSettings.Channels = 2;
            var warnings = new List<string>(
                ApplyQualitySettings(controller.VideoSettings, crf, bitrate));

            // A final export forces original media (no proxy fallback), so a missing original would encode
            // a blank/silent segment instead of failing. Preflight the exported range's renderable sources
            // (graphics + audio) and fail fast, matching the save-frame/export guard. CollectRenderableSources
            // walks the mutable scene graph, so run it on the render thread like StillRenderer does rather
            // than racing UI/render-thread mutations from this (possibly off-thread) caller.
            IReadOnlySet<string> renderableSources = await RenderThread.Dispatcher.InvokeAsync(
                () => Beutl.Editor.ExportSourceValidator.CollectRenderableSources(
                    scene, new TimeRange(scene.Start, scene.Duration)),
                ct: cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> missingSources = Beutl.Editor.ExportSourceValidator.GetMissingPaths(renderableSources);
            if (missingSources.Count > 0)
            {
                throw new RenderingUnavailableException(
                    $"Missing source files required to export: {string.Join(", ", missingSources)}");
            }

            using var renderer = ExportRendererFactory.Create(scene, normalizedScale);
            using var frameProgress = new Subject<TimeSpan>();
            using var frameProvider = new FrameProviderImpl(scene, frameRate, renderer, frameProgress);
            using var composer = CreateExportComposer(scene, normalizedSampleRate);
            using var sampleProgress = new Subject<TimeSpan>();
            using var sampleProvider = new SampleProviderImpl(scene, composer, normalizedSampleRate, sampleProgress);

            await controller.Encode(frameProvider, sampleProvider, cancellationToken).ConfigureAwait(false);
            if (encoder is AVFEncodingExtension
                && bitrate is int requestedBitrate
                && CreateAvFoundationBitrateWarning(
                    outputPath,
                    scene.Duration,
                    requestedBitrate,
                    controller.AudioSettings.Bitrate > 0
                        ? controller.AudioSettings.Bitrate
                        : null) is { } bitrateWarning)
            {
                warnings.Add(bitrateWarning);
            }

            return new ExportVideoResponse(
                outputPath,
                frameProvider.FrameCount,
                sampleProvider.SampleCount,
                scene.Duration.ToString("c"),
                GetEncoderName(encoder),
                warnings);
        }

        // FFmpeg registers first, but its native libraries or out-of-process worker may be absent; fall
        // through to the next encoder that supports this container (e.g. AVFoundation on macOS) before giving up.
        bool workerAvailable = FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory);
        CodecUnavailableException? ffmpegFailure = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            // A missing worker fails during process start (not FFmpegLibrariesNotFoundException), so skip
            // the FFmpeg encoder up front when the worker is absent and another encoder can take over.
            if (candidates[i] is FFmpegHeadlessEncodingExtension && !workerAvailable && candidates.Count > 1)
            {
                continue;
            }

            try
            {
                return await EncodeWithAsync(candidates[i]).ConfigureAwait(false);
            }
            catch (FFmpegLibrariesNotFoundException ex)
            {
                ffmpegFailure = new CodecUnavailableException("FFmpeg libraries are not available.", ex);
            }
            catch (FFmpegWorkerException ex)
            {
                ffmpegFailure = new CodecUnavailableException(ex.Message, ex);
            }
        }

        throw ffmpegFailure ?? new CodecUnavailableException("FFmpeg libraries are not available.");
    }

    // Final output: audio must never read proxy media, even though audio opens skip the proxy resolver today.
    internal static SceneComposer CreateExportComposer(Scene scene, int sampleRate)
    {
        return new SceneComposer(scene, disableResourceShare: true, forceOriginalSource: true)
        {
            SampleRate = sampleRate,
        };
    }

    internal static IReadOnlyList<string> ApplyQualitySettings(
        VideoEncoderSettings settings,
        int? crf,
        int? bitrate)
    {
        if (settings is FFmpegVideoEncoderSettings ffmpeg)
        {
            ApplySdrColorMetadata(ffmpeg);

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

            return [];
        }

        if (settings is AVFVideoEncoderSettings avFoundation)
        {
            ApplySdrColorMetadata(avFoundation);
            if (bitrate is int bitrateValue)
            {
                settings.Bitrate = bitrateValue;
            }

            return crf.HasValue
                ?
                [
                    "AVFoundation ignored the requested crf parameter because its VideoToolbox encoder "
                    + "has no CRF control; use bitrate to request an average data rate."
                ]
                : [];
        }

        if (bitrate is int fallbackBitrate)
        {
            settings.Bitrate = fallbackBitrate;
        }

        return [];
    }

    internal static string? CreateAvFoundationBitrateWarning(
        string outputPath,
        TimeSpan duration,
        int requestedBitrate,
        int? configuredAudioBitrate = null)
    {
        if (requestedBitrate <= 0
            || duration <= TimeSpan.Zero
            || !File.Exists(outputPath))
        {
            return null;
        }

        double containerBitrate = new FileInfo(outputPath).Length * 8d / duration.TotalSeconds;
        double measuredVideoBitrate = configuredAudioBitrate is > 0
            ? Math.Max(0, containerBitrate - configuredAudioBitrate.Value)
            : containerBitrate;
        if (measuredVideoBitrate >= requestedBitrate * 0.5d)
            return null;

        if (configuredAudioBitrate is > 0)
        {
            return FormattableString.Invariant(
                $"AVFoundation produced an estimated average video bitrate of {Math.Round(measuredVideoBitrate):F0} bit/s after subtracting the configured {configuredAudioBitrate.Value} bit/s audio stream from the {Math.Round(containerBitrate):F0} bit/s container rate. This is below 50% of the requested {requestedBitrate} bit/s; VideoToolbox treats the bitrate as a target rather than a guaranteed floor.");
        }

        return FormattableString.Invariant(
            $"AVFoundation produced an average container bitrate of {Math.Round(containerBitrate):F0} bit/s, below 50% of the requested {requestedBitrate} bit/s. This container-wide approximation includes audio and muxing overhead because the audio stream bitrate is not known; VideoToolbox treats the bitrate as a target rather than a guaranteed floor.");
    }

    private static void ApplySdrColorMetadata(FFmpegVideoEncoderSettings settings)
    {
        if (settings.ColorPrimaries == FFColorPrimaries.UNSPECIFIED)
            settings.ColorPrimaries = FFColorPrimaries.BT709;
        if (settings.ColorTrc == FFColorTransfer.UNSPECIFIED)
            settings.ColorTrc = FFColorTransfer.BT709;
        if (settings.ColorSpace == FFColorSpace.UNSPECIFIED)
            settings.ColorSpace = FFColorSpace.BT709;
        if (settings.ColorRange == FFColorRange.UNSPECIFIED)
            settings.ColorRange = FFColorRange.MPEG;
    }

    private static void ApplySdrColorMetadata(AVFVideoEncoderSettings settings)
    {
        if (settings.ColorPrimaries == AVFVideoEncoderSettings.ColorPrimariesType.Default)
            settings.ColorPrimaries = AVFVideoEncoderSettings.ColorPrimariesType.Bt709;
        if (settings.ColorTransfer == AVFVideoEncoderSettings.ColorTransferCharacteristic.Default)
            settings.ColorTransfer = AVFVideoEncoderSettings.ColorTransferCharacteristic.Bt709;
        if (settings.YCbCrMatrix == AVFVideoEncoderSettings.YCbCrMatrixType.Default)
            settings.YCbCrMatrix = AVFVideoEncoderSettings.YCbCrMatrixType.Bt709;
    }

    private static string GetEncoderName(ControllableEncodingExtension encoder)
    {
        return encoder switch
        {
            FFmpegHeadlessEncodingExtension => "FFmpeg",
            AVFEncodingExtension => "AVFoundation",
            _ => encoder.GetType().Name,
        };
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

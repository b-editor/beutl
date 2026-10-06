using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.Proxy;

public sealed partial class FFmpegProxyGenerator(IProxyStore store) : IProxyGenerator, IProxyGeneratorAvailability
{
    private static readonly ILogger s_logger = Log.CreateLogger<FFmpegProxyGenerator>();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public bool IsAvailable
        => !FFmpegInstallNotifier.IsLibrariesMissing
            && !FFmpegInstallNotifier.IsVerificationInProgress;

    public event EventHandler? AvailabilityChanged
    {
        add => FFmpegInstallNotifier.AvailabilityChanged += value;
        remove => FFmpegInstallNotifier.AvailabilityChanged -= value;
    }

    public async ValueTask GenerateAsync(ProxyJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        string sourcePath = job.Source.SourcePath;
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(null, sourcePath);

        // The file at this path can be replaced while the job waits in the queue; encoding the new
        // bytes but publishing under job.Source (the old fingerprint / proxy path) would report
        // success yet leave the current fingerprint with no usable proxy and an orphaned entry. Skip
        // instead — a fresh job keyed on the new fingerprint is enqueued by the normal UI scan.
        if (ProxyFingerprint.FromFile(sourcePath) != job.Source)
        {
            // Mark the old proxies stale before skipping: they no longer match the replaced file, and if
            // that replacement goes offline before ProxyResolver notices, offline resolution (which
            // cannot restat the source) would otherwise rank a Ready same-fingerprint proxy for the file.
            MarkSourceProxiesStale(store, job.Source, "Source changed since the job was queued.");
            throw new ProxyGenerationSkippedException("Source changed since the job was queued.");
        }

        if (IsAlwaysStillImage(sourcePath))
            throw new ProxyGenerationSkippedException("Still images are not eligible for proxy generation.");

        using MediaReader reader = OpenSourceReader(sourcePath);

        if (!reader.HasVideo)
            throw new ProxyGenerationSkippedException("Source has no video stream.");

        long frameCount = ResolveFrameCount(reader.VideoInfo);
        if (frameCount <= 0)
            throw new ProxyGenerationSkippedException("Source has no decodable video frames.");

        // An APNG/GIF/WebP container is exposed as video by the animated-image readers; only its
        // multi-frame form is an expensive timeline source worth proxying. A single-frame one is a
        // still image, so skip it here — the frame count, not the extension, decides.
        if (frameCount <= 1 && IsAnimatableImage(sourcePath))
            throw new ProxyGenerationSkippedException("Still images are not eligible for proxy generation.");

        PixelSize originalSize = reader.VideoInfo.FrameSize;
        if (originalSize.Width <= 0 || originalSize.Height <= 0)
            throw new ProxyGenerationSkippedException("Source video has no frame size.");

        PixelSize proxySize = CalculateProxySize(originalSize, job.Preset);
        string relative = ProxyPathUtilities.BuildRelativePath(job.Source, job.Preset, Guid.NewGuid());
        string finalPath = Path.Combine(store.StoreRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        string tempPath = CreateTempPathForOutput(finalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        await EncodeAndPublishGuardedAsync(tempPath, async () =>
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using var frameProvider = new ReaderFrameProvider(reader, frameCount, job.Progress);
            using var sampleProvider = new SilentSampleProvider();
            var controller = new FFmpegEncodingControllerProxy(tempPath, new FFmpegEncodingSettings());
            Configure(controller, reader.VideoInfo, proxySize, job.Preset);

            await controller.Encode(frameProvider, sampleProvider, job.CancellationToken);

            EnsureSourceUnchanged(store, job, sourcePath);

            await PublishAsync(tempPath, finalPath, job, relative, originalSize, proxySize, job.CancellationToken);
        });
    }

    // Re-stat the source after the encode so a Ready proxy is never published for bytes that no longer
    // match the current source — a stale same-path proxy the resolver could later select once the
    // original is gone. A source that vanished mid-encode (deleted/moved) is an obsolete job like a
    // content change, so TryFromFile keeps it Skipped rather than letting FromFile's exception surface
    // as a Failed generation.
    internal static void EnsureSourceUnchanged(IProxyStore store, ProxyJob job, string sourcePath)
    {
        if (!ProxyFingerprint.TryFromFile(sourcePath, out ProxyFingerprint current))
            throw new ProxyGenerationSkippedException("Source became unavailable during encoding.");
        if (current != job.Source)
        {
            MarkSourceProxiesStale(store, job.Source, "Source changed during encoding.");
            throw new ProxyGenerationSkippedException("Source changed during encoding.");
        }
    }

    // A source path can back Ready proxies for several presets (Half, Quarter, …), all sharing the same
    // fingerprint. Offline ResolveByPath groups by that fingerprint and cannot restat the file, so leaving
    // any preset Ready lets it serve stale bytes for a replaced/missing source. Mark every Ready entry for
    // this fingerprint stale, not just the queued preset.
    internal static void MarkSourceProxiesStale(IProxyStore store, ProxyFingerprint source, string reason)
    {
        foreach (ProxyEntry entry in store.Enumerate())
        {
            if (entry.Source == source && entry.State == ProxyState.Ready)
                store.TryTransition(entry.Source, entry.Preset, ProxyState.Stale, reason);
        }
    }

    // The temp artifact must not outlive a failed or canceled generation: nothing else reclaims it
    // until the age-based reconcile sweep, so every failure path deletes it here.
    internal static async Task EncodeAndPublishGuardedAsync(string tempPath, Func<Task> encodeAndPublish)
    {
        try
        {
            await encodeAndPublish();
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            TryDelete(tempPath);
            throw CreateUnavailableException(ex);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    internal static void Configure(
        FFmpegEncodingControllerProxy controller,
        VideoStreamInfo videoInfo,
        PixelSize proxySize,
        ProxyPreset preset)
    {
        ProxyEncodeParameters parameters = ProxyPresetDefinitions.Get(preset);
        var videoSettings = controller.VideoSettings;
        videoSettings.SourceSize = videoInfo.FrameSize;
        videoSettings.DestinationSize = proxySize;
        videoSettings.FrameRate = videoInfo.FrameRate;
        videoSettings.Codec = new CodecRecord("libx264", "H.264 / AVC");
        videoSettings.Options.Clear();
        videoSettings.Options.Add(new AdditionalOption("preset", parameters.Preset));
        videoSettings.Options.Add(new AdditionalOption("crf", parameters.Crf.ToString()));
        videoSettings.Options.Add(new AdditionalOption("tune", parameters.Tune));
        videoSettings.Options.Add(new AdditionalOption("profile", "high"));
        // No fixed level: a hard cap (e.g. 4.0) rejects legal high-FPS proxies such as 1080p60 from a
        // 4K60 source. libx264 derives a valid level from the frame size and rate.
    }

    internal static PixelSize CalculateProxySize(PixelSize original, ProxyPreset preset)
    {
        ProxyEncodeParameters parameters = ProxyPresetDefinitions.Get(preset);
        float scale = parameters.Scale;
        int longEdge = Math.Max(original.Width, original.Height);
        if (parameters.LongEdgeClamp is { } clamp && longEdge * scale > clamp)
        {
            scale = clamp / (float)longEdge;
        }

        // Round the long edge from the single scale, then derive the short edge from the *realized*
        // long edge so both axes share one scale and the proxy aspect ratio tracks the source. The
        // even-dimension constraint still leaves an unavoidable sub-pixel AR deviation.
        if (original.Width >= original.Height)
        {
            int width = MakeEven(original.Width * scale);
            int height = MakeEven(width * (double)original.Height / original.Width);
            return new PixelSize(width, height);
        }
        else
        {
            int height = MakeEven(original.Height * scale);
            int width = MakeEven(height * (double)original.Width / original.Height);
            return new PixelSize(width, height);
        }
    }

    private static MediaReader OpenSourceReader(string sourcePath)
    {
        try
        {
            return MediaReader.Open(
                sourcePath,
                new MediaOptions(MediaMode.Video) { PreferProxy = false });
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            throw CreateUnavailableException(ex);
        }
        catch (Exception ex) when (FFmpegInstallNotifier.IsLibrariesMissing)
        {
            // The FFmpeg decoder recorded the libraries missing while opening and no fallback decoder
            // could open this source; treat it as unavailable so the queue pauses rather than draining
            // the batch as ordinary per-file failures.
            throw CreateUnavailableException(ex);
        }
    }

    private static int MakeEven(double value)
    {
        int rounded = (int)Math.Round(value / 2.0, MidpointRounding.AwayFromZero) * 2;
        return Math.Max(2, rounded);
    }

    // Formats that can only ever hold one frame: reject them without paying to open a reader.
    internal static bool IsAlwaysStillImage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".jpg"
            or ".jpeg"
            or ".bmp"
            or ".tif"
            or ".tiff";
    }

    // Image containers that may be animated (APNG / GIF / WebP): stillness is decided by frame count
    // after opening the reader, not by the extension. `.apng` is included because AnimatedPngDecoderInfo
    // advertises it too, so a single-frame .apng must also be frame-count-checked rather than proxied.
    internal static bool IsAnimatableImage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png"
            or ".apng"
            or ".gif"
            or ".webp";
    }

    private static ProxyGeneratorUnavailableException CreateUnavailableException(Exception ex)
    {
        FFmpegInstallNotifier.NotifyMissing();
        return new ProxyGeneratorUnavailableException(ex.Message);
    }

    // Some containers / variable-frame-rate media report nb_frames == 0 even though they decode
    // video; derive the count from duration * frame rate so the encode loop (bounded by
    // FrameProvider.FrameCount) does not produce an empty proxy.
    internal static long ResolveFrameCount(VideoStreamInfo info)
    {
        if (info.NumFrames > 0)
            return info.NumFrames;

        // A positive duration * frame rate that truncates to 0 (very short clip or imprecise
        // metadata) still has at least one frame; don't skip it over rounding.
        double estimate = (info.Duration * info.FrameRate).ToDouble();
        return estimate > 0 ? Math.Max(1L, (long)estimate) : 0;
    }

    internal static string CreateTempPathForOutput(string finalPath)
    {
        string directory = Path.GetDirectoryName(finalPath) ?? string.Empty;
        string extension = Path.GetExtension(finalPath);
        string fileName = $"{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.tmp{extension}";
        return Path.Combine(directory, fileName);
    }
}

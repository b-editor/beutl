using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Extensions.FFmpeg.Proxy;

public sealed class FFmpegProxyGenerator(IProxyStore store) : IProxyGenerator
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async ValueTask GenerateAsync(ProxyJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        string sourcePath = job.Source.AbsolutePath;
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(null, sourcePath);

        if (IsStillImage(sourcePath))
            throw new ProxyGenerationSkippedException("Still images are not eligible for proxy generation.");

        using MediaReader reader = MediaReader.Open(
            sourcePath,
            new MediaOptions(MediaMode.Video) { PreferProxy = false });

        if (!reader.HasVideo || reader.VideoInfo.NumFrames <= 0)
            throw new ProxyGenerationSkippedException("Source has no video stream.");

        PixelSize originalSize = reader.VideoInfo.FrameSize;
        if (originalSize.Width <= 0 || originalSize.Height <= 0)
            throw new ProxyGenerationSkippedException("Source video has no frame size.");

        PixelSize proxySize = CalculateProxySize(originalSize, job.Preset);
        string relative = ProxyPathUtilities.BuildRelativePath(job.Source, job.Preset);
        string finalPath = Path.Combine(store.StoreRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        string tempPath = CreateTempPathForOutput(finalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        bool finalFileMoved = false;

        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using var frameProvider = new ReaderFrameProvider(reader, job.Progress);
            using var sampleProvider = new SilentSampleProvider();
            var controller = new FFmpegEncodingControllerProxy(tempPath, new FFmpegEncodingSettings());
            Configure(controller, reader.VideoInfo, proxySize, job.Preset);

            await controller.Encode(frameProvider, sampleProvider, job.CancellationToken);

            File.Move(tempPath, finalPath, overwrite: true);
            finalFileMoved = true;
            long fileSize = new FileInfo(finalPath).Length;
            var now = DateTime.UtcNow;
            var entry = new ProxyEntry(
                job.Source,
                job.Preset,
                ProxyState.Ready,
                relative,
                fileSize,
                originalSize,
                proxySize,
                now,
                now,
                null);

            WriteMetadata(finalPath, entry);
            store.Register(entry);
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            TryDelete(tempPath);
            if (finalFileMoved)
                TryDelete(finalPath);

            throw new ProxyGeneratorUnavailableException(ex.Message);
        }
        catch
        {
            TryDelete(tempPath);
            if (finalFileMoved)
                TryDelete(finalPath);

            throw;
        }
    }

    private static void Configure(
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
        videoSettings.Options.Add(new AdditionalOption("level", "4.0"));
    }

    private static PixelSize CalculateProxySize(PixelSize original, ProxyPreset preset)
    {
        ProxyEncodeParameters parameters = ProxyPresetDefinitions.Get(preset);
        float scale = parameters.Scale;
        int longEdge = Math.Max(original.Width, original.Height);
        if (parameters.LongEdgeClamp is { } clamp && longEdge * scale > clamp)
        {
            scale = clamp / (float)longEdge;
        }

        int width = MakeEven(Math.Max(2, (int)Math.Round(original.Width * scale)));
        int height = MakeEven(Math.Max(2, (int)Math.Round(original.Height * scale)));
        return new PixelSize(width, height);
    }

    private static int MakeEven(int value)
    {
        return value % 2 == 0 ? value : Math.Max(2, value - 1);
    }

    private static bool IsStillImage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png"
            or ".jpg"
            or ".jpeg"
            or ".bmp"
            or ".gif"
            or ".webp"
            or ".tif"
            or ".tiff";
    }

    internal static string CreateTempPathForOutput(string finalPath)
    {
        string directory = Path.GetDirectoryName(finalPath) ?? string.Empty;
        string extension = Path.GetExtension(finalPath);
        string fileName = $"{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.tmp{extension}";
        return Path.Combine(directory, fileName);
    }

    internal static void WriteMetadata(string finalPath, ProxyEntry entry)
    {
        string metadataPath = Path.Combine(Path.GetDirectoryName(finalPath)!, "meta.json");
        ProxyEntry[] entries = ReadMetadataEntries(metadataPath, entry.Source)
            .Where(existing => existing.Preset != entry.Preset)
            .Append(entry)
            .ToArray();
        var metadata = new ProxySourceMetadata
        {
            Source = entry.Source,
            Entries = [.. entries],
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, s_jsonOptions));
    }

    private static IEnumerable<ProxyEntry> ReadMetadataEntries(string metadataPath, ProxyFingerprint source)
    {
        if (!File.Exists(metadataPath))
            yield break;

        ProxySourceMetadata? metadata = null;
        try
        {
            metadata = JsonSerializer.Deserialize<ProxySourceMetadata>(
                File.ReadAllText(metadataPath),
                s_jsonOptions);
        }
        catch
        {
        }

        if (metadata is not { Version: ProxySourceMetadata.CurrentVersion }
            || metadata.Source != source)
        {
            yield break;
        }

        foreach (ProxyEntry entry in metadata.Entries)
        {
            if (entry.Source == source)
                yield return entry;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed class ReaderFrameProvider(
        MediaReader reader,
        IProgress<ProxyJobProgress>? progress) : IFrameProvider
    {
        public long FrameCount => reader.VideoInfo.NumFrames;

        public Rational FrameRate => reader.VideoInfo.FrameRate;

        public ValueTask<Bitmap> RenderFrame(long frame)
        {
            if (!reader.ReadVideo((int)frame, out Ref<Bitmap>? bitmapRef))
                throw new InvalidOperationException($"Could not decode source frame {frame}.");

            using (bitmapRef)
            {
                progress?.Report(new ProxyJobProgress(
                    FrameCount <= 0 ? 0 : Math.Clamp((frame + 1) / (double)FrameCount, 0, 1),
                    null));

                return ValueTask.FromResult(bitmapRef.Value.Clone());
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class SilentSampleProvider : ISampleProvider
    {
        public long SampleCount => 0;

        public long SampleRate => 44100;

        public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
        {
            return ValueTask.FromResult(new Pcm<Stereo32BitFloat>((int)SampleRate, (int)Math.Max(0, length)));
        }

        public void Dispose()
        {
        }
    }
}

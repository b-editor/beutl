using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Beutl.Configuration;
using Beutl.Extensions.FFmpeg;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source.Proxy;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.Proxy;

public sealed partial class ProxyGenerator
{
    private static readonly ILogger s_logger = Log.CreateLogger<ProxyGenerator>();

    [GeneratedRegex(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex ProgressTimeRegex();

    public static bool IsAvailable()
    {
        return TryResolveFFmpegPath(out _);
    }

    public async Task<ProxyGenerationResult> GenerateAsync(
        string originalPath,
        ProxyPresetKind preset,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(originalPath))
        {
            return new(false, null, $"Source file not found: {originalPath}");
        }

        if (!TryResolveFFmpegPath(out string? ffmpegPath))
        {
            return new(false, null, "FFmpeg executable was not found.");
        }

        var proxyConfig = GlobalConfiguration.Instance.ProxyConfig;
        var cacheManager = (ProxyCacheManager)ProxyCacheManager.Instance;

        PixelSize originalSize;
        TimeSpan originalDuration;
        Rational originalFrameRate;
        try
        {
            using var reader = MediaReader.Open(originalPath, new MediaOptions(MediaMode.Video));
            originalSize = reader.VideoInfo.FrameSize;
            originalDuration = TimeSpan.FromSeconds(reader.VideoInfo.Duration.ToDouble());
            originalFrameRate = reader.VideoInfo.FrameRate;
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to probe media before proxy generation: {Path}", originalPath);
            return new(false, null, "Failed to probe source media.");
        }

        if (originalSize.Width < proxyConfig.MinWidthToGenerate)
        {
            return new(false, null, "Source resolution is below the configured minimum for proxy generation.");
        }

        ProxyFFmpegOptions options = ProxyPresetOptions.For(preset);
        string cacheDir = proxyConfig.CacheDirectory;
        Directory.CreateDirectory(cacheDir);

        string key = cacheManager.ComputeKey(originalPath);
        string proxyPath = Path.Combine(cacheDir, key + options.Extension);
        string tempPath = proxyPath + ".tmp";

        cacheManager.MarkGenerating(originalPath);

        var arguments = new List<string>
        {
            "-hide_banner",
            "-y",
            "-i", originalPath,
            "-vf", options.VideoFilter,
        };
        arguments.AddRange(options.VideoCodecArgs);
        arguments.AddRange(options.AudioCodecArgs);
        arguments.Add(tempPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments) startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                cacheManager.MarkFailed(originalPath);
                return new(false, null, "Failed to start ffmpeg process.");
            }

            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* ignore */ }
            });

            var progressTask = Task.Run(async () =>
            {
                var regex = ProgressTimeRegex();
                string? line;
                while ((line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                {
                    var match = regex.Match(line);
                    if (!match.Success) continue;
                    if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours)) continue;
                    if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)) continue;
                    if (!double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) continue;

                    double elapsed = hours * 3600 + minutes * 60 + seconds;
                    if (originalDuration.TotalSeconds > 0)
                    {
                        double ratio = Math.Clamp(elapsed / originalDuration.TotalSeconds, 0, 1);
                        progress?.Report(ratio);
                    }
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await progressTask.ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                TryDeleteFile(tempPath);
                cacheManager.MarkFailed(originalPath);
                return new(false, null, "Cancelled.");
            }

            if (process.ExitCode != 0)
            {
                TryDeleteFile(tempPath);
                cacheManager.MarkFailed(originalPath);
                return new(false, null, $"ffmpeg exited with code {process.ExitCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            cacheManager.MarkFailed(originalPath);
            return new(false, null, "Cancelled.");
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Proxy generation failed for {Path}", originalPath);
            TryDeleteFile(tempPath);
            cacheManager.MarkFailed(originalPath);
            return new(false, null, ex.Message);
        }

        try
        {
            if (File.Exists(proxyPath)) File.Delete(proxyPath);
            File.Move(tempPath, proxyPath);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to finalize proxy file at {Path}", proxyPath);
            TryDeleteFile(tempPath);
            cacheManager.MarkFailed(originalPath);
            return new(false, null, "Failed to finalize proxy file.");
        }

        PixelSize proxySize = originalSize;
        try
        {
            using var proxyReader = MediaReader.Open(proxyPath, new MediaOptions(MediaMode.Video));
            proxySize = proxyReader.VideoInfo.FrameSize;
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Failed to probe generated proxy: {Path}", proxyPath);
        }

        var originalInfo = new FileInfo(originalPath);
        var proxyInfo = new FileInfo(proxyPath);
        var entry = new ProxyEntry(
            OriginalPath: originalPath,
            OriginalSize: originalInfo.Length,
            OriginalMtime: originalInfo.LastWriteTimeUtc,
            OriginalFrameSize: originalSize,
            ProxyPath: proxyPath,
            ProxyFileSize: proxyInfo.Length,
            ProxyFrameSize: proxySize,
            Preset: preset,
            GeneratedAt: DateTime.UtcNow,
            SchemaVersion: 1);

        cacheManager.Register(entry);
        progress?.Report(1.0);
        _ = originalFrameRate;
        return new(true, proxyPath, null);
    }

    private static bool TryResolveFFmpegPath(out string? ffmpegPath)
    {
        string exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string installed = Path.Combine(FFmpegInstallService.GetFFmpegInstallPath(), exeName);
        if (File.Exists(installed))
        {
            ffmpegPath = installed;
            return true;
        }

        if (TryFindOnPath(exeName, out var found))
        {
            ffmpegPath = found;
            return true;
        }

        ffmpegPath = null;
        return false;
    }

    private static bool TryFindOnPath(string executable, out string? fullPath)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            char separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
            foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir.Trim(), executable);
                if (File.Exists(candidate))
                {
                    fullPath = candidate;
                    return true;
                }
            }
        }

        fullPath = null;
        return false;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Failed to delete temp proxy file: {Path}", path);
        }
    }
}

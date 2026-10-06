using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Beutl.Extensions.FFmpeg.Properties;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg;

public partial class FFmpegInstallService
{
    private static Regex? GetAssetNameRegex()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => new(@"^ffmpeg-n8[0-9.-]+-.*-win64-gpl-shared-8[0-9.-]+\.zip$"),
                Architecture.Arm64 => new(@"^ffmpeg-n8[0-9.-]+-.*-winarm64-gpl-shared-8[0-9.-]+\.zip$"),
                _ => null
            };
        }
        else if (OperatingSystem.IsLinux())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => new(@"^ffmpeg-n8[0-9.-]+-.*-linux64-gpl-shared-8[0-9.-]+\.tar\.xz$"),
                Architecture.Arm64 => new(@"^ffmpeg-n8[0-9.-]+-.*-linuxarm64-gpl-shared-8[0-9.-]+\.tar\.xz$"),
                _ => null
            };
        }

        return null;
    }

    private async Task<string?> GetDownloadUrlAsync(HttpClient client, CancellationToken ct = default)
    {
        Regex? regex = GetAssetNameRegex();
        if (regex == null)
            return null;

        try
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Beutl");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            _logger.LogInformation("Fetching releases from GitHub API");
            GitHubRelease[]? releases = await client.GetFromJsonAsync<GitHubRelease[]>(GitHubReleasesApiUrl, ct);
            if (releases == null)
                return null;

            foreach (GitHubRelease release in releases)
            {
                if (release.Assets == null)
                    continue;

                foreach (GitHubAsset asset in release.Assets)
                {
                    if (asset.Name != null && regex.IsMatch(asset.Name))
                    {
                        _logger.LogInformation("Found FFmpeg asset: {Name}, URL: {Url}", asset.Name,
                            asset.BrowserDownloadUrl);
                        return asset.BrowserDownloadUrl;
                    }
                }
            }

            _logger.LogWarning("No matching FFmpeg n8.0 asset found in releases");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch releases from GitHub API");
            return null;
        }
        finally
        {
            client.DefaultRequestHeaders.Remove("User-Agent");
            client.DefaultRequestHeaders.Remove("Accept");
        }
    }

    private async Task InstallFromBtbNBuildsAsync(HttpClient client)
    {
        CancellationToken ct = _cts.Token;

        // 1. Get download URL from GitHub API
        ProgressTextChanged?.Invoke(Strings.Fetching_release_information);
        IndeterminateChanged?.Invoke(true);

        string? downloadUrl = await GetDownloadUrlAsync(client, ct);
        if (downloadUrl == null)
        {
            _logger.LogError("Failed to get download URL for FFmpeg");
            ProgressTextChanged?.Invoke(Strings.Failed_to_find_FFmpeg_release);
            Completed?.Invoke(false);
            return;
        }

        // 2. Download
        string? downloadedFile = await DownloadFileAsync(client, downloadUrl, ct);
        if (downloadedFile == null)
        {
            Completed?.Invoke(false);
            return;
        }

        // 3. Extract
        string installPath = GetFFmpegInstallPath();
        bool extracted = await ExtractArchiveAsync(downloadedFile, installPath, ct);
        if (!extracted)
        {
            Completed?.Invoke(false);
            return;
        }

        // 4. Clean up downloaded file
        try
        {
            File.Delete(downloadedFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete downloaded file: {File}", downloadedFile);
        }

        // 5. Verify FFmpeg installation
        await VerifyAndCompleteAsync();
    }

    private async Task<string?> DownloadFileAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            ProgressChanged?.Invoke(0, 1);
            IndeterminateChanged?.Invoke(false);
            ProgressTextChanged?.Invoke(Language.MessageStrings.Downloading);

            _logger.LogInformation("Downloading FFmpeg from {Url}", url);
            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            string fileName = Path.GetFileName(new Uri(url).LocalPath);

            string tempDir = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp");
            Directory.CreateDirectory(tempDir);
            string filePath = Path.Combine(tempDir, fileName);

            await using FileStream destination = File.Create(filePath);
            await using Stream download = await response.Content.ReadAsStreamAsync(ct);

            if (!contentLength.HasValue)
            {
                IndeterminateChanged?.Invoke(true);
                await download.CopyToAsync(destination, ct);
            }
            else
            {
                const int bufferSize = 81920;
                byte[] buffer = new byte[bufferSize];
                long totalBytesRead = 0;
                int bytesRead;
                while ((bytesRead = await download.ReadAsync(buffer, ct)) != 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalBytesRead += bytesRead;
                    ProgressChanged?.Invoke(totalBytesRead, contentLength.Value);
                }
            }

            ProgressTextChanged?.Invoke(Language.MessageStrings.DownloadComplete);
            IndeterminateChanged?.Invoke(false);
            _logger.LogInformation("Downloaded FFmpeg to {FilePath}", filePath);

            return filePath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download FFmpeg");
            ProgressTextChanged?.Invoke(ex.Message);
            return null;
        }
    }

    private async Task<bool> ExtractArchiveAsync(string archivePath, string destinationPath, CancellationToken ct)
    {
        try
        {
            ProgressTextChanged?.Invoke(Language.MessageStrings.Extracting);
            IndeterminateChanged?.Invoke(true);

            _logger.LogInformation("Extracting {Archive} to {Destination}", archivePath, destinationPath);

            // Clean up existing directory
            if (Directory.Exists(destinationPath))
            {
                Directory.Delete(destinationPath, true);
            }

            Directory.CreateDirectory(destinationPath);

            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractZipAsync(archivePath, destinationPath, ct);
            }
            else if (archivePath.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractTarXzAsync(archivePath, destinationPath, ct);
            }
            else
            {
                throw new NotSupportedException($"Unsupported archive format: {archivePath}");
            }

            // Move files from inner directory (e.g., ffmpeg-n8.0-20241125-win64-gpl-shared/bin) to destination
            MoveExtractedFiles(destinationPath, ct);

            ProgressTextChanged?.Invoke(Language.MessageStrings.ExtractionComplete);
            IndeterminateChanged?.Invoke(false);
            _logger.LogInformation("Extraction complete");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract archive");
            ProgressTextChanged?.Invoke(ex.Message);
            return false;
        }
    }

    private async Task ExtractZipAsync(string zipPath, string destinationPath, CancellationToken ct)
    {
        await using ZipArchive archive = await ZipFile.OpenReadAsync(zipPath, ct);
        int total = archive.Entries.Count;
        int current = 0;

        // Compare against the destination plus a separator: a bare prefix would also accept a sibling such as
        // "ffmpeg-evil" next to "ffmpeg".
        string destinationRoot = Path.GetFullPath(destinationPath);
        string destinationPrefix = Path.EndsInDirectorySeparator(destinationRoot)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;

        IndeterminateChanged?.Invoke(false);
        ProgressChanged?.Invoke(0, total);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Length != 0)
            {
                string entryPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                if (!entryPath.StartsWith(destinationPrefix, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Entry is outside of the target directory.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
                await using FileStream fs = File.Create(entryPath);
                await using Stream es = await entry.OpenAsync(ct);
                await es.CopyToAsync(fs, ct);
            }

            current++;
            ProgressChanged?.Invoke(current, total);
        }
    }

    private static async Task ExtractTarXzAsync(string tarXzPath, string destinationPath, CancellationToken ct)
    {
        // Use tar command on Linux
        ProcessStartInfo psi = new("tar")
        {
            ArgumentList = { "-xJf", tarXzPath, "-C", destinationPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process? process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start tar process");
        }

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            string error = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"tar extraction failed: {error}");
        }
    }

    private static void MoveExtractedFiles(string destinationPath, CancellationToken ct)
    {
        // BtbN builds extract to a subdirectory like "ffmpeg-n8.0-20241125-win64-gpl-shared"
        // We need to find the bin directory and move its contents
        string[] directories = Directory.GetDirectories(destinationPath);
        if (directories.Length == 1)
        {
            string extractedDir = directories[0];
            string binDir = Path.Combine(extractedDir, "bin");

            if (Directory.Exists(binDir))
            {
                // Move all files from bin to destination
                foreach (string file in Directory.GetFiles(binDir))
                {
                    ct.ThrowIfCancellationRequested();
                    string destFile = Path.Combine(destinationPath, Path.GetFileName(file));
                    File.Move(file, destFile, true);
                }

                // Also copy library files if they exist separately (for shared builds)
                string libDir = Path.Combine(extractedDir, "lib");
                if (Directory.Exists(libDir))
                {
                    foreach (string file in Directory.GetFiles(libDir))
                    {
                        ct.ThrowIfCancellationRequested();
                        string destFile = Path.Combine(destinationPath, Path.GetFileName(file));
                        File.Move(file, destFile, true);
                    }
                }

                // Delete the extracted subdirectory
                Directory.Delete(extractedDir, true);
            }
        }
    }
}

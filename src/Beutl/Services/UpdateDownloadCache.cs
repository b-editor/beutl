using System.Security.Cryptography;
using Beutl.IO;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

// A ready installer still needs its file when the user eventually chooses to install it.
// Concurrent updates use separate folders so neither can replace or prune the other's file.
internal sealed class UpdateDownloadCache : IDisposable
{
    private const string InstancePrefix = ".instance-";
    private static readonly ILogger s_logger = Log.CreateLogger<UpdateDownloadCache>();
    private static readonly EnumerationOptions s_regularEntries = new() { AttributesToSkip = FileAttributes.ReparsePoint };
    private readonly string _root;
    private readonly FileStream _claim;

    private UpdateDownloadCache(string root, FileStream claim)
    {
        _root = root;
        _claim = claim;
        DirectoryPath = Path.GetDirectoryName(claim.Name)!;
    }

    public string DirectoryPath { get; }

    public static UpdateDownloadCache Open(string root)
    {
        root = Path.GetFullPath(root);
        for (int i = 0; ; i++)
        {
            string directory = i == 0 ? root : Path.Combine(root, $"{InstancePrefix}{i}");
            DirectoryInfo info = Directory.CreateDirectory(directory);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException(MessageStrings.DownloadFailed);

            if (TryClaim(directory) is { } claim) return new UpdateDownloadCache(root, claim);
        }
    }

    private static FileStream? TryClaim(string directory)
    {
        string path = Path.Combine(directory, ".lock");
        if (new FileInfo(path).LinkTarget != null) return null;
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) when (File.Exists(path)) { return null; }
    }

    // The response may have supplied the name in Content-Disposition, so the URL need not identify the file.
    public async Task<string?> FindVerifiedAsync(string expectedHash, long maximumBytes, CancellationToken ct)
    {
        foreach (FileInfo file in CompletedFiles(DirectoryPath).Concat(OtherDirectories().SelectMany(CompletedFiles)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (file.Length == 0 || file.Length > maximumBytes) continue;
                using FileStream stream = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                    bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (!await MatchesAsync(stream, expectedHash, maximumBytes, ct)) continue;
                if (file.DirectoryName == DirectoryPath) return file.FullName;

                // Keep our own copy when another Beutl owns (or might reclaim) the source folder.
                string target = Path.Combine(DirectoryPath, file.Name);
                if (new FileInfo(target).LinkTarget != null) continue;
                using var copy = new StagedOutputFile(target);
                stream.Position = 0;
                await using (FileStream output = File.Create(copy.TemporaryPath))
                {
                    await stream.CopyToAsync(output, ct);
                    output.Position = 0;
                    if (!await MatchesAsync(output, expectedHash, maximumBytes, ct)) continue;
                }
                copy.Commit(ct);
                return target;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                s_logger.LogWarning(ex, "Could not read the cached update {File}", file.FullName);
            }
        }

        return null;
    }

    private static async Task<bool> MatchesAsync(FileStream stream, string expectedHash, long maximumBytes, CancellationToken ct)
    {
        if (stream.Length == 0 || stream.Length > maximumBytes) return false;
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        return string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    public void RemoveOtherDownloads(string keep)
    {
        try
        {
            RemoveOldFiles(DirectoryPath, keep);
            foreach (string directory in OtherDirectories())
            {
                using FileStream? claim = TryClaim(directory);
                if (claim != null) RemoveOldFiles(directory, null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            s_logger.LogWarning(ex, "Could not finish cleaning old update downloads");
        }
    }

    private static void RemoveOldFiles(string directory, string? keep)
    {
        foreach (FileInfo file in CompletedFiles(directory))
        {
            try
            {
                if (keep == null || !FilePathComparison.AreSameCanonicalPath(file.FullName, keep))
                    file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                s_logger.LogWarning(ex, "Could not remove the old update {File}", file.FullName);
            }
        }
    }

    // Only the ordinary files the downloader can publish. Lock files, partial transfers and links are never candidates.
    private static IEnumerable<FileInfo> CompletedFiles(string directory)
        => new DirectoryInfo(directory).EnumerateFiles("*", s_regularEntries)
            .Where(file => file.Name == NormalizeFileName(file.Name));

    private IEnumerable<string> OtherDirectories()
    {
        if (_root != DirectoryPath) yield return _root;
        foreach (DirectoryInfo directory in new DirectoryInfo(_root).EnumerateDirectories(InstancePrefix + "*", s_regularEntries))
        {
            if (directory.FullName != DirectoryPath && File.Exists(Path.Combine(directory.FullName, ".lock")))
                yield return directory.FullName;
        }
    }

    // Keep a single component safe both as a file name and inside the Linux install command.
    internal static string? NormalizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string name = value.Trim().Trim('"').Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = string.Concat(name.Select(c =>
                char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'))
            .Trim(' ', '.');
        return name.Length == 0 ? null : name;
    }

    public void Dispose() => _claim.Dispose();
}

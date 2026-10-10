using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Configuration;

public static class LiveMcpTokenStore
{
    public const string FileName = "live-mcp-token.json";
    internal const string LockFileName = "live-mcp-token.lock";
    private const int UnixLockExclusive = 2;
    private const int UnixLockNonBlocking = 4;
    private static readonly TimeSpan s_lockTimeout = TimeSpan.FromSeconds(10);

    public static string GetOrCreate(string profileDirectory, string? legacyToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        string directory = Path.GetFullPath(profileDirectory);
        string path = Path.Combine(directory, FileName);
        if (ReadToken(path) is { } existing)
            return existing;

        Directory.CreateDirectory(directory);
        using FileStream lease = AcquireLock(Path.Combine(directory, LockFileName));
        // Another process may have published the token while we waited for its lease.
        if (ReadToken(path) is { } published)
            return published;

        string token = ReadLegacyToken(Path.Combine(directory, "settings.json"))
                       ?? (!string.IsNullOrWhiteSpace(legacyToken) ? legacyToken : null)
                       ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return PublishToken(path, token);
    }

    private static string PublishToken(string path, string token)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (FileStream stream = new(temporary, options))
            using (var writer = new Utf8JsonWriter(stream))
            {
                new JsonObject { ["schemaVersion"] = 1, ["token"] = token }.WriteTo(writer);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, path, overwrite: false); }
            catch (IOException)
            {
                if (ReadToken(path) is { } winner)
                    return winner;
                throw;
            }
            return token;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string? ReadToken(string path)
    {
        JsonNode? document;
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            document = JsonNode.Parse(stream);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The live MCP token store is invalid; refusing to replace the saved token.", ex);
        }

        if (document is JsonObject obj && obj["schemaVersion"] is JsonValue version
            && version.TryGetValue<int>(out int schemaVersion) && schemaVersion == 1
            && obj["token"] is JsonValue value && value.TryGetValue<string>(out string? token)
            && !string.IsNullOrWhiteSpace(token))
            return token;

        throw new InvalidDataException("The live MCP token store is invalid; refusing to replace the saved token.");
    }

    private static string? ReadLegacyToken(string path)
    {
        JsonNode? document;
        try
        {
            // Older versions still replace settings.json atomically. Keep a readable snapshot
            // without preventing that replacement; this file is used only for initial migration.
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            document = JsonNode.Parse(stream);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Cannot migrate the live MCP token from invalid settings.", ex);
        }

        if (document is not JsonObject obj)
            throw new InvalidDataException("Cannot migrate the live MCP token from invalid settings.");
        if (obj["AiAgent"] is JsonObject section
            && section["LiveMcpToken"] is JsonValue value && value.TryGetValue<string>(out string? token)
            && !string.IsNullOrWhiteSpace(token))
            return token;
        return null;
    }

    private static FileStream AcquireLock(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        // Keep the lock file named after releasing it. Unlinking it lets two processes lock
        // different inodes at the same path and generate different tokens.
        Stopwatch elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                FileStream stream = new(path, options);
                // FileShare locking on Unix is best-effort in .NET. Require the actual lock;
                // an unsupported filesystem must not silently allow two token creators.
                if (!OperatingSystem.IsWindows() && Flock(stream.SafeFileHandle, UnixLockExclusive | UnixLockNonBlocking) != 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    stream.Dispose();
                    throw new IOException($"Cannot exclusively lock the live MCP token store (error {error}).");
                }
                return stream;
            }
            catch (IOException) when (elapsed.Elapsed < s_lockTimeout)
            {
                Thread.Sleep(25);
            }
        }
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(SafeFileHandle handle, int operation);
}

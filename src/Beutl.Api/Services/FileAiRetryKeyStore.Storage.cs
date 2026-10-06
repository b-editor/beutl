using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Beutl.Api.Services;

internal sealed partial class FileAiRetryKeyStore
{
    private StoreData Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new StoreData();
            long length = new FileInfo(_path).Length;
            if (length is <= 0 or > MaximumBytes)
                throw new AiRetryStoreUnavailableException("Retry key store is unreadable.");

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(_path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number)
            {
                throw new AiRetryStoreUnavailableException("Retry key store has an unsupported version.");
            }

            int value = version.GetInt32();
            return value switch
            {
                1 => LoadVersion1(root),
                Version => LoadVersion2(root),
                _ => throw new AiRetryStoreUnavailableException("Retry key store has an unsupported version."),
            };
        }
        catch (AiRetryStoreUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or FormatException
            or OverflowException
            or InvalidOperationException
            or ArgumentException)
        {
            throw new AiRetryStoreUnavailableException("Retry key store could not be read.", ex);
        }
    }

    private static StoreData LoadVersion1(JsonElement root)
    {
        if (root.EnumerateObject().Count() != 2
            || !root.TryGetProperty("entries", out JsonElement entriesElement)
            || entriesElement.ValueKind != JsonValueKind.Array)
        {
            throw new AiRetryStoreUnavailableException("Retry key store has an invalid legacy shape.");
        }

        var data = new StoreData();
        int count = 0;
        foreach (JsonElement element in entriesElement.EnumerateArray())
        {
            if (++count > MaximumEntries
                || element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Count() != 2
                || !element.TryGetProperty("identity", out JsonElement identity)
                || !element.TryGetProperty("key", out JsonElement key))
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid entries.");
            }

            string identityValue = ReadIdentity(identity);
            string keyValue = ReadKey(key);
            if (!data.Entries.TryAdd(identityValue, new Entry(keyValue, 1, string.Empty, 0, null, null)))
                throw new AiRetryStoreUnavailableException("Retry key store contains duplicate entries.");
            data.Generations[identityValue] = 1;
        }

        return data;
    }

    private static StoreData LoadVersion2(JsonElement root)
    {
        if (root.EnumerateObject().Count() != 4
            || !root.TryGetProperty("entries", out JsonElement entriesElement)
            || entriesElement.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("generations", out JsonElement generationsElement)
            || generationsElement.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("attempts", out JsonElement attemptsElement)
            || attemptsElement.ValueKind != JsonValueKind.Array)
        {
            throw new AiRetryStoreUnavailableException("Retry key store has an invalid shape.");
        }

        var data = new StoreData();
        int generationCount = 0;
        foreach (JsonElement element in generationsElement.EnumerateArray())
        {
            if (++generationCount > MaximumGenerations
                || element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Count() != 2
                || !element.TryGetProperty("identity", out JsonElement identity)
                || !element.TryGetProperty("generation", out JsonElement generation)
                || generation.ValueKind != JsonValueKind.Number)
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid generations.");
            }

            string identityValue = ReadIdentity(identity);
            long generationValue = generation.GetInt64();
            if (generationValue < 0 || !data.Generations.TryAdd(identityValue, generationValue))
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid generations.");
        }

        int entryCount = 0;
        foreach (JsonElement element in entriesElement.EnumerateArray())
        {
            if (++entryCount > MaximumEntries
                || element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Count() != 7
                || !element.TryGetProperty("identity", out JsonElement identity)
                || !element.TryGetProperty("key", out JsonElement key)
                || !element.TryGetProperty("generation", out JsonElement generation)
                || !element.TryGetProperty("payloadDigest", out JsonElement payloadDigest)
                || !element.TryGetProperty("payloadVersion", out JsonElement payloadVersion)
                || !element.TryGetProperty("inFlightOwner", out JsonElement owner)
                || !element.TryGetProperty("inFlightUntil", out JsonElement until)
                || generation.ValueKind != JsonValueKind.Number
                || payloadDigest.ValueKind != JsonValueKind.String
                || payloadVersion.ValueKind != JsonValueKind.Number
                || owner.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                || until.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid entries.");
            }

            string identityValue = ReadIdentity(identity);
            string keyValue = ReadKey(key);
            long generationValue = generation.GetInt64();
            string payloadDigestValue = ReadDigest(payloadDigest);
            int payloadVersionValue = payloadVersion.GetInt32();
            string? ownerValue = owner.ValueKind == JsonValueKind.Null ? null : owner.GetString();
            DateTimeOffset? untilValue = until.ValueKind == JsonValueKind.Null
                ? null
                : until.GetDateTimeOffset();
            if (ownerValue is not null && !IsPrintable(ownerValue))
                throw new AiRetryStoreUnavailableException("Retry key store contains an invalid lease owner.");
            if (ownerValue is null != (untilValue is null)
                || payloadVersionValue <= 0
                || generationValue <= 0
                || !data.Entries.TryAdd(
                    identityValue,
                    new Entry(
                        keyValue,
                        generationValue,
                        payloadDigestValue,
                        payloadVersionValue,
                        ownerValue,
                        untilValue))
                || (!data.Generations.TryGetValue(identityValue, out long recorded)
                    ? !data.Generations.TryAdd(identityValue, generationValue)
                    : recorded != generationValue))
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid or duplicate entries.");
            }
        }

        int attemptCount = 0;
        foreach (JsonElement element in attemptsElement.EnumerateArray())
        {
            if (++attemptCount > MaximumAttempts
                || element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Count() != 10
                || !element.TryGetProperty("token", out JsonElement token)
                || !element.TryGetProperty("accountId", out JsonElement account)
                || !element.TryGetProperty("identity", out JsonElement identity)
                || !element.TryGetProperty("key", out JsonElement key)
                || !element.TryGetProperty("generation", out JsonElement generation)
                || !element.TryGetProperty("kind", out JsonElement kind)
                || !element.TryGetProperty("payloadDigest", out JsonElement payloadDigest)
                || !element.TryGetProperty("payloadVersion", out JsonElement payloadVersion)
                || !element.TryGetProperty("createdAt", out JsonElement createdAt)
                || !element.TryGetProperty("expiresAt", out JsonElement expiresAt)
                || generation.ValueKind != JsonValueKind.Number
                || token.ValueKind != JsonValueKind.String
                || account.ValueKind != JsonValueKind.String
                || kind.ValueKind != JsonValueKind.String
                || payloadDigest.ValueKind != JsonValueKind.String
                || payloadVersion.ValueKind != JsonValueKind.Number
                || createdAt.ValueKind != JsonValueKind.String
                || expiresAt.ValueKind != JsonValueKind.String)
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid attempts.");
            }

            string tokenValue = token.GetString() ?? string.Empty;
            string accountValue = account.GetString() ?? string.Empty;
            string identityValue = ReadIdentity(identity);
            string keyValue = ReadKey(key);
            long generationValue = generation.GetInt64();
            string payloadDigestValue = ReadDigest(payloadDigest);
            int payloadVersionValue = payloadVersion.GetInt32();
            DateTimeOffset createdAtValue = createdAt.GetDateTimeOffset();
            DateTimeOffset expiresAtValue = expiresAt.GetDateTimeOffset();
            AiRetryAttemptKind kindValue = kind.GetString() switch
            {
                "recovery" => AiRetryAttemptKind.Recovery,
                "newPurchase" => AiRetryAttemptKind.NewPurchase,
                _ => throw new AiRetryStoreUnavailableException("Retry key store contains an invalid attempt kind."),
            };
            if (tokenValue.Length is < 32 or > 128
                || !IsPrintable(tokenValue)
                || accountValue.Length is 0 or > 256
                || generationValue < 0
                || payloadVersionValue <= 0
                || expiresAtValue <= createdAtValue
                || !data.Attempts.TryAdd(
                    tokenValue,
                    new PendingAttempt(
                        tokenValue,
                        accountValue,
                        identityValue,
                        keyValue,
                        generationValue,
                        kindValue,
                        payloadDigestValue,
                        payloadVersionValue,
                        createdAtValue,
                        expiresAtValue)))
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid or duplicate attempts.");
            }
        }

        foreach ((string identity, Entry entry) in data.Entries)
        {
            if (!data.Generations.TryGetValue(identity, out long generation)
                || generation != entry.Generation)
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains inconsistent generations.");
            }
        }

        return data;
    }

    private static string ReadIdentity(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid identity.");
        string value = element.GetString() ?? string.Empty;
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid identity.");
        return value;
    }

    private static string ReadKey(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid key.");
        string value = element.GetString() ?? string.Empty;
        if (!IsPrintable(value))
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid key.");
        return value;
    }

    private static string ReadDigest(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid payload digest.");
        string value = element.GetString() ?? string.Empty;
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid payload digest.");
        return value;
    }

    private void Save(StoreData data)
    {
        if (data.Entries.Count > MaximumEntries
            || data.Generations.Count > MaximumGenerations
            || data.Attempts.Count > MaximumAttempts)
        {
            throw new AiRetryStoreUnavailableException("Retry key store is full.");
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = Version,
            entries = data.Entries
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new
                {
                    identity = pair.Key,
                    key = pair.Value.Key,
                    generation = pair.Value.Generation,
                    payloadDigest = pair.Value.PayloadDigest,
                    payloadVersion = pair.Value.PayloadVersion,
                    inFlightOwner = pair.Value.InFlightOwner,
                    inFlightUntil = pair.Value.InFlightUntil,
                }),
            generations = data.Generations
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new { identity = pair.Key, generation = pair.Value }),
            attempts = data.Attempts.Values
                .OrderBy(attempt => attempt.Token, StringComparer.Ordinal)
                .Select(attempt => new
                {
                    token = attempt.Token,
                    accountId = attempt.AccountId,
                    identity = attempt.Identity,
                    key = attempt.Key,
                    generation = attempt.Generation,
                    kind = attempt.Kind == AiRetryAttemptKind.Recovery
                        ? "recovery"
                        : "newPurchase",
                    payloadDigest = attempt.PayloadDigest,
                    payloadVersion = attempt.PayloadVersion,
                    createdAt = attempt.CreatedAt,
                    expiresAt = attempt.ExpiresAt,
                }),
        });
        if (bytes.Length > MaximumBytes)
            throw new AiRetryStoreUnavailableException("Retry key store is full.");

        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            WritePrivateBytes(temporary, bytes);
            AtomicReplace(temporary, _path, overwrite: true);
            RestrictFile(_path);
            EnsureDirectorySynced(_directory);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private FileStream AcquireLock()
    {
        IOException? last = null;
        for (int attempt = 0; attempt < LockAttempts; attempt++)
        {
            try
            {
                FileStream stream = new(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                RestrictFile(LockPath);
                return stream;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(LockDelay);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new AiRetryStoreUnavailableException("Retry key store is in use.", ex);
            }
        }

        throw new AiRetryStoreUnavailableException("Retry key store is in use.", last);
    }

    private void SweepTemporaryFiles()
    {
        try
        {
            DateTime cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);
            foreach (string path in Directory.EnumerateFiles(_directory, "retry-keys.json.*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff
                    && !IsFileLocked(path))
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsFileLocked(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsPrintable(string value)
        => value.Length is > 0 and <= 255 && value.All(c => c is >= '\x20' and <= '\x7e');

    private static void WritePrivateBytes(string path, ReadOnlySpan<byte> bytes)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough | FileOptions.SequentialScan,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using FileStream stream = new(path, options);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        RestrictFile(path);
    }

    private static void AtomicReplace(string temporary, string destination, bool overwrite)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.Move(temporary, destination, overwrite);
            return;
        }
        const uint replace = 0x1;
        const uint writeThrough = 0x8;
        if (!MoveFileEx(temporary, destination, writeThrough | (overwrite ? replace : 0)))
            throw new IOException("Atomic retry-store replacement failed.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    /// <summary>
    /// Unix directory fsync closes the rename durability window. Windows has
    /// no portable directory fsync; file bytes are flushed and rename is
    /// atomic, while directory-entry persistence remains filesystem-defined.
    /// </summary>
    private static void EnsureDirectorySynced(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        int fd = UnixOpen(path, 0);
        if (fd < 0)
            throw new IOException($"Unable to open directory for durability sync (errno {Marshal.GetLastWin32Error()}).");
        try
        {
            if (UnixFsync(fd) != 0)
                throw new IOException($"Unable to fsync directory (errno {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            if (UnixClose(fd) != 0)
                throw new IOException($"Unable to close synced directory (errno {Marshal.GetLastWin32Error()}).");
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int UnixFsync(int fd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int UnixClose(int fd);

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

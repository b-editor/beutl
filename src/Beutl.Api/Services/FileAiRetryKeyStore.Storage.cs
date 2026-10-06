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
        ReadGenerations(generationsElement, data);
        ReadEntries(entriesElement, data);
        ReadAttempts(attemptsElement, data);
        EnsureEntryGenerations(data);
        return data;
    }

    private static void ReadGenerations(JsonElement generationsElement, StoreData data)
    {
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
    }

    private static void ReadEntries(JsonElement entriesElement, StoreData data)
    {
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
    }

    private static void ReadAttempts(JsonElement attemptsElement, StoreData data)
    {
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
            AiRetryAttemptKind kindValue = ReadAttemptKind(kind);
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
    }

    private static AiRetryAttemptKind ReadAttemptKind(JsonElement kind)
        => kind.GetString() switch
        {
            "recovery" => AiRetryAttemptKind.Recovery,
            "newPurchase" => AiRetryAttemptKind.NewPurchase,
            _ => throw new AiRetryStoreUnavailableException("Retry key store contains an invalid attempt kind."),
        };

    private static void EnsureEntryGenerations(StoreData data)
    {
        foreach ((string identity, Entry entry) in data.Entries)
        {
            if (!data.Generations.TryGetValue(identity, out long generation)
                || generation != entry.Generation)
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains inconsistent generations.");
            }
        }
    }

    private static string ReadIdentity(JsonElement element)
        => ReadSha256Hex(element, "Retry key store contains an invalid identity.");

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
        => ReadSha256Hex(element, "Retry key store contains an invalid payload digest.");

    private static string ReadSha256Hex(JsonElement element, string invalidMessage)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new AiRetryStoreUnavailableException(invalidMessage);
        string value = element.GetString() ?? string.Empty;
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new AiRetryStoreUnavailableException(invalidMessage);
        return value;
    }

    private void Save(StoreData data)
    {
        byte[] bytes = SerializeStore(data);
        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            PrivateDurableFile.WritePrivateBytes(temporary, bytes);
            PrivateDurableFile.AtomicReplace(
                temporary,
                _path,
                overwrite: true,
                failureMessage: "Atomic retry-store replacement failed.");
            PrivateDurableFile.RestrictFile(_path);
            PrivateDurableFile.EnsureDirectorySynced(_directory);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static byte[] SerializeStore(StoreData data)
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
        return bytes;
    }

    private FileStream AcquireLock()
    {
        IOException? last = null;
        for (int attempt = 0; attempt < LockAttempts; attempt++)
        {
            try
            {
                FileStream stream = new(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                PrivateDurableFile.RestrictFile(LockPath);
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
                    && !PrivateDurableFile.IsFileLocked(path))
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

    private static bool IsPrintable(string value)
        => value.Length is > 0 and <= 255 && value.All(c => c is >= '\x20' and <= '\x7e');
}

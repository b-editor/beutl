using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beutl.Services;

internal sealed record TelemetryIdentity(string InstallationId, string FirstSeenMonth);

/// <summary>
/// Persists only the random identity used by the product event stream. Every read,
/// create, and reset is serialized per home directory in-process and cross-process.
/// </summary>
internal sealed class TelemetryIdentityStore
{
    private const string DirectoryName = "telemetry";
    private const string FileName = "identity.json";
    private const string LockFileName = "identity.lock";
    private static readonly ConcurrentDictionary<string, object> s_homeGates = new(GetPathComparer());
    private static readonly TimeSpan s_lockTimeout = TimeSpan.FromSeconds(10);
    private readonly string _directory;
    private readonly string _path;
    private readonly string _lockPath;
    private readonly string _mutexName;
    private readonly object _homeGate;

    internal TelemetryIdentityStore(string? homeDirectory = null)
    {
        homeDirectory ??= BeutlEnvironment.GetHomeDirectoryPath();
        string canonicalHome = System.IO.Path.GetFullPath(homeDirectory);
        string? pathRoot = System.IO.Path.GetPathRoot(canonicalHome);
        if (pathRoot is not null && canonicalHome.Length > pathRoot.Length)
        {
            canonicalHome = canonicalHome.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);
        }
        _directory = System.IO.Path.Combine(canonicalHome, DirectoryName);
        _path = System.IO.Path.Combine(_directory, FileName);
        _lockPath = System.IO.Path.Combine(_directory, LockFileName);
        _mutexName = CreateMutexName(canonicalHome);
        _homeGate = s_homeGates.GetOrAdd(canonicalHome, static _ => new object());
    }

    internal string Path => _path;

    internal string LockPath => _lockPath;

    internal TelemetryIdentity GetOrCreate()
    {
        return ExecuteLocked(
            () =>
            {
                // Re-read only after both locks are held. Another process may have
                // completed first creation while this process was waiting.
                TelemetryIdentity? existing = TryReadUnlocked();
                if (existing is not null)
                {
                    return existing;
                }

                TelemetryIdentity created = CreateIdentity();
                WriteUnlocked(created);
                return created;
            },
            CreateIdentity);
    }

    internal TelemetryIdentity? TryRead()
    {
        return ExecuteLocked(TryReadUnlocked, static () => null);
    }

    internal void Reset()
    {
        ExecuteLocked(
            () =>
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                return true;
            },
            static () => false);
    }

    private T ExecuteLocked<T>(Func<T> action, Func<T> fallback)
    {
        lock (_homeGate)
        {
            Mutex? mutex = null;
            bool ownsMutex = false;
            try
            {
                mutex = new Mutex(initiallyOwned: false, _mutexName);
                try
                {
                    ownsMutex = mutex.WaitOne(s_lockTimeout);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                {
                    return fallback();
                }

                Directory.CreateDirectory(_directory);
                using FileStream lockFile = AcquireLockFile();
                return action();
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or PlatformNotSupportedException)
            {
                // Telemetry storage is never allowed to prevent application startup
                // or consent revocation. Export remains gated independently.
                return fallback();
            }
            finally
            {
                if (ownsMutex && mutex is not null)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // The fallback path already preserves application behavior.
                    }
                }

                mutex?.Dispose();
            }
        }
    }

    private FileStream AcquireLockFile()
    {
        long startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(startedAt) < s_lockTimeout)
            {
                Thread.Sleep(20);
            }
        }
    }

    private TelemetryIdentity? TryReadUnlocked()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            TelemetryIdentity? identity = JsonSerializer.Deserialize<TelemetryIdentity>(File.ReadAllText(_path));
            return IsValid(identity) ? identity : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void WriteUnlocked(TelemetryIdentity identity)
    {
        string temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(identity));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The identity file has already been atomically published.
            }
        }
    }

    private static TelemetryIdentity CreateIdentity()
    {
        return new TelemetryIdentity(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    }

    private static string CreateMutexName(string canonicalHome)
    {
        string mutexKey = OperatingSystem.IsWindows()
            ? canonicalHome.ToUpperInvariant()
            : canonicalHome;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(mutexKey));
        return $"Beutl.Telemetry.Identity.{Convert.ToHexString(hash)}";
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static bool IsValid(TelemetryIdentity? identity)
    {
        return identity is not null
            && Guid.TryParseExact(identity.InstallationId, "N", out _)
            && DateTime.TryParseExact(identity.FirstSeenMonth, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _);
    }
}

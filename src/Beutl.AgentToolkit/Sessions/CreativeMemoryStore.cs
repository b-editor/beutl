using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beutl.AgentToolkit.Sessions;

public sealed record CreativeDirectionFingerprint(
    string ConceptLabel,
    IReadOnlyList<string> PaletteRoles,
    IReadOnlyList<string> MotionVerbs,
    string StructuralSignature,
    DateTimeOffset Timestamp);

public sealed class CreativeMemoryStore
{
    public const int DefaultCapacity = 12;

    // Overrides the user-global memory root, primarily so tests stay off the real home directory.
    public const string GlobalRootVariable = "BEUTL_AGENT_MEMORY_HOME";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    // The global memory file is shared by every store instance in the process (each anchored to a
    // different workspace) and by every Beutl process on the machine. A per-instance lock cannot
    // serialize that shared read-modify-write, so we key an in-process lock on the normalized global
    // path and pair it with a machine-wide named mutex for the cross-process case.
    private static readonly ConcurrentDictionary<string, object> s_globalLocks = new();

    private readonly string _workspacePath;
    private readonly string _globalPath;
    private readonly bool _globalIsSeparate;
    private readonly string _globalLockKey;
    private readonly string _globalMutexName;
    private readonly int _capacity;
    private readonly object _gate = new();

    public CreativeMemoryStore(string workspaceRoot, int capacity = DefaultCapacity, string? globalRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _capacity = Math.Max(1, capacity);
        _workspacePath = MemoryPath(workspaceRoot);
        _globalPath = MemoryPath(ResolveGlobalRoot(globalRoot));
        _globalIsSeparate = !PathsEqual(_globalPath, _workspacePath);
        _globalLockKey = NormalizeKey(_globalPath);
        _globalMutexName = MutexName(_globalLockKey);
    }

    public IReadOnlyList<CreativeDirectionFingerprint> ReadRecent()
    {
        lock (_gate)
        {
            IReadOnlyList<CreativeDirectionFingerprint> workspace = ReadUnlocked(_workspacePath);
            if (!_globalIsSeparate)
            {
                return workspace;
            }

            // Anti-repeat considers what the agent produced in this project *and* elsewhere.
            return Merge(workspace, ReadUnlocked(_globalPath));
        }
    }

    public void Record(CreativeDirectionFingerprint fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint.ConceptLabel)
            && fingerprint.PaletteRoles.Count == 0
            && fingerprint.MotionVerbs.Count == 0
            && string.IsNullOrWhiteSpace(fingerprint.StructuralSignature))
        {
            return;
        }

        CreativeDirectionFingerprint normalized = fingerprint with
        {
            ConceptLabel = fingerprint.ConceptLabel.Trim(),
            PaletteRoles = NormalizeList(fingerprint.PaletteRoles),
            MotionVerbs = NormalizeList(fingerprint.MotionVerbs),
            StructuralSignature = fingerprint.StructuralSignature.Trim(),
            Timestamp = fingerprint.Timestamp == default ? DateTimeOffset.UtcNow : fingerprint.Timestamp
        };

        // The workspace layer is single-owner, so the per-instance gate is enough; the global layer
        // is shared and goes through the cross-instance / cross-process path instead.
        lock (_gate)
        {
            RecordUnlocked(_workspacePath, normalized);
        }

        if (_globalIsSeparate)
        {
            RecordGlobal(normalized);
        }
    }

    private void RecordGlobal(CreativeDirectionFingerprint normalized)
    {
        object pathLock = s_globalLocks.GetOrAdd(_globalLockKey, static _ => new object());
        lock (pathLock)
        {
            using var mutex = new Mutex(false, _globalMutexName);
            bool owns = false;
            try
            {
                try
                {
                    owns = mutex.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                    // A prior owner crashed mid-write; the mutex is still ours. The atomic replace
                    // means it can only have left a complete previous file, so we proceed.
                    owns = true;
                }

                RecordUnlocked(_globalPath, normalized);
            }
            finally
            {
                if (owns)
                {
                    mutex.ReleaseMutex();
                }
            }
        }
    }

    private void RecordUnlocked(string path, CreativeDirectionFingerprint normalized)
    {
        List<CreativeDirectionFingerprint> items = ReadUnlocked(path).ToList();
        items.RemoveAll(item => SameCreativeDirection(item, normalized));
        items.Insert(0, normalized);
        if (items.Count > _capacity)
        {
            items.RemoveRange(_capacity, items.Count - _capacity);
        }

        WriteAtomic(path, JsonSerializer.Serialize(items, s_jsonOptions));
    }

    private static void WriteAtomic(string path, string content)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        // Write to a sibling temp file, then swap it in with an atomic same-volume replace so a crash
        // or a concurrent writer can never expose a torn/partial JSON file to the next reader.
        string temp = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private IReadOnlyList<CreativeDirectionFingerprint> ReadUnlocked(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            CreativeDirectionFingerprint[]? items =
                JsonSerializer.Deserialize<CreativeDirectionFingerprint[]>(File.ReadAllText(path), s_jsonOptions);
            return items?
                .Where(item => item is not null)
                .Take(_capacity)
                .ToArray()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private IReadOnlyList<CreativeDirectionFingerprint> Merge(
        IReadOnlyList<CreativeDirectionFingerprint> workspace,
        IReadOnlyList<CreativeDirectionFingerprint> global)
    {
        var merged = new List<CreativeDirectionFingerprint>();
        foreach (CreativeDirectionFingerprint item in workspace.Concat(global).OrderByDescending(item => item.Timestamp))
        {
            if (!merged.Any(existing => SameCreativeDirection(existing, item)))
            {
                merged.Add(item);
            }
        }

        if (merged.Count > _capacity)
        {
            merged.RemoveRange(_capacity, merged.Count - _capacity);
        }

        return merged;
    }

    private static string ResolveGlobalRoot(string? globalRoot)
    {
        if (!string.IsNullOrWhiteSpace(globalRoot))
        {
            return globalRoot;
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(GlobalRootVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return BeutlEnvironment.GetHomeDirectoryPath();
    }

    private static string MemoryPath(string root)
        => Path.Combine(root, "agent-output", "creative-memory.json");

    private static bool PathsEqual(string left, string right)
        => string.Equals(NormalizeKey(left), NormalizeKey(right), StringComparison.Ordinal);

    private static string NormalizeKey(string path)
    {
        string full = Path.GetFullPath(path);
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? full.ToLowerInvariant()
            : full;
    }

    private static string MutexName(string normalizedKey)
    {
        // Named mutexes cap the name length and forbid path separators, so hash the (already
        // case-normalized) path to a fixed-width token. Global\ makes it machine-wide, not per-session.
        string token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedKey)));
        return $"Global\\Beutl.AgentToolkit.CreativeMemory.{token}";
    }

    private static string[] NormalizeList(IEnumerable<string> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool SameCreativeDirection(CreativeDirectionFingerprint left, CreativeDirectionFingerprint right)
        => string.Equals(left.ConceptLabel, right.ConceptLabel, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.StructuralSignature, right.StructuralSignature, StringComparison.OrdinalIgnoreCase);
}

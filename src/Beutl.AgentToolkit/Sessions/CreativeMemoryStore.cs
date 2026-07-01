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

    private readonly string _workspacePath;
    private readonly string _globalPath;
    private readonly bool _globalIsSeparate;
    private readonly int _capacity;
    private readonly object _gate = new();

    public CreativeMemoryStore(string workspaceRoot, int capacity = DefaultCapacity, string? globalRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _capacity = Math.Max(1, capacity);
        _workspacePath = MemoryPath(workspaceRoot);
        _globalPath = MemoryPath(ResolveGlobalRoot(globalRoot));
        _globalIsSeparate = !PathsEqual(_globalPath, _workspacePath);
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

        lock (_gate)
        {
            // Both layers are updated so the entry survives as per-project history and as a
            // cross-project fingerprint the next project can avoid.
            RecordUnlocked(_workspacePath, normalized);
            if (_globalIsSeparate)
            {
                RecordUnlocked(_globalPath, normalized);
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

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(items, s_jsonOptions));
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
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

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

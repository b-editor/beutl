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

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly int _capacity;
    private readonly object _gate = new();

    public CreativeMemoryStore(string workspaceRoot, int capacity = DefaultCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _capacity = Math.Max(1, capacity);
        _path = Path.Combine(workspaceRoot, "agent-output", "creative-memory.json");
    }

    public IReadOnlyList<CreativeDirectionFingerprint> ReadRecent()
    {
        lock (_gate)
        {
            return ReadUnlocked();
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
            List<CreativeDirectionFingerprint> items = ReadUnlocked().ToList();
            items.RemoveAll(item => SameCreativeDirection(item, normalized));
            items.Insert(0, normalized);
            if (items.Count > _capacity)
            {
                items.RemoveRange(_capacity, items.Count - _capacity);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(items, s_jsonOptions));
        }
    }

    private IReadOnlyList<CreativeDirectionFingerprint> ReadUnlocked()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            CreativeDirectionFingerprint[]? items =
                JsonSerializer.Deserialize<CreativeDirectionFingerprint[]>(File.ReadAllText(_path), s_jsonOptions);
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

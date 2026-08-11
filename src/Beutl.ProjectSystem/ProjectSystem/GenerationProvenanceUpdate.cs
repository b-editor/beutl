using System.Collections.Immutable;

namespace Beutl.ProjectSystem;

public enum GenerationProvenanceUpdateKind
{
    Preserve,
    Append,
    Replace,
    Clear,
}

/// <summary>
/// Describes an intentional update to an element's generation provenance.
/// The default value preserves existing records.
/// </summary>
public readonly struct GenerationProvenanceUpdate : IEquatable<GenerationProvenanceUpdate>
{
    private readonly ImmutableArray<GenerationProvenance> _items;

    private GenerationProvenanceUpdate(
        GenerationProvenanceUpdateKind kind,
        IEnumerable<GenerationProvenance>? items)
    {
        Kind = kind;
        _items = items is null ? [] : [.. items];
        if (kind is GenerationProvenanceUpdateKind.Append or GenerationProvenanceUpdateKind.Replace)
        {
            if (_items.IsEmpty)
                throw new ArgumentException("Append and replace updates require at least one record.", nameof(items));
            if (_items.Any(item => item is null))
                throw new ArgumentException("Provenance updates cannot contain null records.", nameof(items));
            if (kind == GenerationProvenanceUpdateKind.Replace)
                _items = GenerationProvenanceCollection.Validate(_items, nameof(items));
        }
    }

    public GenerationProvenanceUpdateKind Kind { get; }

    public IReadOnlyList<GenerationProvenance> Items
        => _items.IsDefault ? [] : _items;

    public static GenerationProvenanceUpdate Preserve => default;

    public static GenerationProvenanceUpdate Clear { get; }
        = new(GenerationProvenanceUpdateKind.Clear, null);

    public static GenerationProvenanceUpdate Append(IEnumerable<GenerationProvenance> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new GenerationProvenanceUpdate(GenerationProvenanceUpdateKind.Append, items);
    }

    public static GenerationProvenanceUpdate Replace(IEnumerable<GenerationProvenance> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new GenerationProvenanceUpdate(GenerationProvenanceUpdateKind.Replace, items);
    }

    public ImmutableArray<GenerationProvenance> ApplyTo(
        ImmutableArray<GenerationProvenance> existing)
    {
        if (existing.IsDefault)
            existing = [];

        ImmutableArray<GenerationProvenance> result = Kind switch
        {
            GenerationProvenanceUpdateKind.Preserve => existing,
            GenerationProvenanceUpdateKind.Append => existing.AddRange(_items),
            GenerationProvenanceUpdateKind.Replace => _items,
            GenerationProvenanceUpdateKind.Clear => [],
            _ => throw new InvalidOperationException($"Unknown provenance update kind: {Kind}."),
        };
        return GenerationProvenanceCollection.Validate(result, nameof(existing));
    }

    public bool Equals(GenerationProvenanceUpdate other)
        => Kind == other.Kind && Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj)
        => obj is GenerationProvenanceUpdate other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (GenerationProvenance item in Items)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(
        GenerationProvenanceUpdate left,
        GenerationProvenanceUpdate right)
        => left.Equals(right);

    public static bool operator !=(
        GenerationProvenanceUpdate left,
        GenerationProvenanceUpdate right)
        => !left.Equals(right);
}

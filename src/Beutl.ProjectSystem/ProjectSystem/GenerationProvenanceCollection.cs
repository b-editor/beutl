using System.Collections.Immutable;

namespace Beutl.ProjectSystem;

/// <summary>
/// Defines the provenance collection contract for in-memory mutation. Mutations exceeding
/// <see cref="Capacity"/> are rejected. Deserialization treats provenance as optional metadata
/// and retains at most this many valid records so malformed metadata cannot block element load.
/// </summary>
public static class GenerationProvenanceCollection
{
    public const int Capacity = 32;

    public static ImmutableArray<GenerationProvenance> Validate(
        IEnumerable<GenerationProvenance> records,
        string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(records, parameterName);
        ImmutableArray<GenerationProvenance> result = [.. records];
        if (result.Length > Capacity)
        {
            throw new GenerationProvenanceCapacityException(result.Length, Capacity, parameterName);
        }
        if (result.Any(record => record is null))
        {
            throw new ArgumentException("Provenance collections cannot contain null records.", parameterName);
        }

        return result;
    }
}

public sealed class GenerationProvenanceCapacityException : ArgumentException
{
    public GenerationProvenanceCapacityException(
        int actualCount,
        int capacity,
        string? parameterName = null)
        : base(
            $"Generation provenance contains {actualCount} records, exceeding the capacity of {capacity}.",
            parameterName)
    {
        ActualCount = actualCount;
        Capacity = capacity;
    }

    public int ActualCount { get; }

    public int Capacity { get; }
}

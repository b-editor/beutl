using Beutl.Editor.Models;
using Beutl.Engine;
using Beutl.Graphics;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Provides shared defaults to caption element factories without constraining the number or type
/// of element descriptions they produce.
/// </summary>
public sealed record CaptionElementContext
{
    public CaptionElementContext(
        int layer,
        string elementName,
        Point defaultPosition = default,
        TimeSpan? minimumLength = null)
    {
        ArgumentNullException.ThrowIfNull(elementName);
        TimeSpan resolvedMinimumLength = minimumLength ?? TimeSpan.FromSeconds(0.5);
        if (resolvedMinimumLength <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumLength));

        Layer = layer;
        ElementName = elementName;
        DefaultPosition = defaultPosition;
        MinimumLength = resolvedMinimumLength;
    }

    public int Layer { get; }

    public string ElementName { get; }

    public Point DefaultPosition { get; }

    public TimeSpan MinimumLength { get; }

    public ElementDescription CreateDescription(
        CaptionCue cue,
        Func<EngineObject> engineObjectFactory,
        int layerOffset = 0,
        string? name = null,
        Point? position = null)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(engineObjectFactory);

        TimeSpan cueLength = cue.End > cue.Start ? cue.End - cue.Start : TimeSpan.Zero;
        return new ElementDescription(
            Start: cue.Start,
            Length: cueLength < MinimumLength ? MinimumLength : cueLength,
            Layer: checked(Layer + layerOffset),
            Source: new ElementSource.EngineObject(engineObjectFactory),
            Name: name ?? ElementName,
            Position: position);
    }
}

/// <summary>
/// Creates one or more timeline element requests for a caption cue.
/// </summary>
public interface ICaptionElementFactory
{
    IReadOnlyList<ElementDescription> CreateElements(CaptionCue cue, CaptionElementContext context);
}

/// <summary>
/// Applies placement independently from element construction.
/// </summary>
public interface ICaptionPlacementPolicy
{
    ElementDescription Place(
        CaptionCue cue,
        CaptionElementContext context,
        ElementDescription description,
        int elementIndex);
}

/// <summary>
/// Places requests at the context default when their factory did not request an explicit position.
/// </summary>
public sealed class DefaultCaptionPlacementPolicy : ICaptionPlacementPolicy
{
    public static DefaultCaptionPlacementPolicy Instance { get; } = new();

    public ElementDescription Place(
        CaptionCue cue,
        CaptionElementContext context,
        ElementDescription description,
        int elementIndex)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(context);
        return description.Position is null
            ? description with { Position = context.DefaultPosition }
            : description;
    }
}

/// <summary>
/// Leaves positions supplied by a factory or object template unchanged.
/// </summary>
public sealed class PreserveCaptionPlacementPolicy : ICaptionPlacementPolicy
{
    public static PreserveCaptionPlacementPolicy Instance { get; } = new();

    public ElementDescription Place(
        CaptionCue cue,
        CaptionElementContext context,
        ElementDescription description,
        int elementIndex)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(context);
        return description;
    }
}

/// <summary>
/// Identifies a caption template without constraining extensions to a closed set of values.
/// </summary>
public readonly struct CaptionTemplateId : IEquatable<CaptionTemplateId>
{
    private readonly string? _value;

    public CaptionTemplateId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value.Trim();
    }

    public string Value => _value ?? string.Empty;

    public bool Equals(CaptionTemplateId other)
        => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is CaptionTemplateId other && Equals(other);

    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(CaptionTemplateId left, CaptionTemplateId right) => left.Equals(right);

    public static bool operator !=(CaptionTemplateId left, CaptionTemplateId right) => !left.Equals(right);
}

/// <summary>
/// Identifies the extension or host component that owns a caption template.
/// </summary>
public readonly struct CaptionTemplateProviderId : IEquatable<CaptionTemplateProviderId>
{
    private readonly string? _value;

    public CaptionTemplateProviderId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value.Trim();
    }

    public string Value => _value ?? string.Empty;

    public bool Equals(CaptionTemplateProviderId other)
        => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is CaptionTemplateProviderId other && Equals(other);

    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(
        CaptionTemplateProviderId left,
        CaptionTemplateProviderId right)
        => left.Equals(right);

    public static bool operator !=(
        CaptionTemplateProviderId left,
        CaptionTemplateProviderId right)
        => !left.Equals(right);
}

public static class CaptionTemplateIds
{
    public static CaptionTemplateId DefaultText { get; } = new("beutl.caption.default-text");
}

public static class CaptionTemplateProviders
{
    public static CaptionTemplateProviderId BuiltIn { get; } = new("beutl");

    public static CaptionTemplateProviderId User { get; } = new("beutl.user");
}

/// <summary>
/// Contributes a stable, provider-owned caption template to a caption catalog.
/// </summary>
public sealed class CaptionTemplateContribution
{
    public CaptionTemplateContribution(
        CaptionTemplateId id,
        CaptionTemplateProviderId providerId,
        string name,
        ICaptionElementFactory elementFactory,
        ICaptionPlacementPolicy placementPolicy,
        int order = 0)
    {
        if (id.Value.Length == 0)
            throw new ArgumentException("A caption template identifier is required.", nameof(id));
        if (providerId.Value.Length == 0)
            throw new ArgumentException("A caption template provider identifier is required.", nameof(providerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(elementFactory);
        ArgumentNullException.ThrowIfNull(placementPolicy);

        Id = id;
        ProviderId = providerId;
        Name = name;
        ElementFactory = elementFactory;
        PlacementPolicy = placementPolicy;
        Order = order;
    }

    public CaptionTemplateId Id { get; }

    public CaptionTemplateProviderId ProviderId { get; }

    public string Name { get; }

    public ICaptionElementFactory ElementFactory { get; }

    public ICaptionPlacementPolicy PlacementPolicy { get; }

    public int Order { get; }

    public IReadOnlyList<ElementDescription> CreateElements(CaptionCue cue, CaptionElementContext context)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<ElementDescription> descriptions = ElementFactory.CreateElements(cue, context)
            ?? throw new InvalidOperationException($"Caption template '{Id}' returned null.");
        if (descriptions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Caption template '{Id}' must create at least one element description.");
        }
        if (descriptions.Any(description => description is null))
        {
            throw new InvalidOperationException(
                $"Caption template '{Id}' returned a null element description.");
        }

        var placed = new ElementDescription[descriptions.Count];
        for (int i = 0; i < descriptions.Count; i++)
        {
            placed[i] = PlacementPolicy.Place(cue, context, descriptions[i], i)
                        ?? throw new InvalidOperationException(
                            $"Caption template '{Id}' placement returned null for element {i}.");
        }

        return Array.AsReadOnly(placed);
    }
}

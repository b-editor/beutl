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
/// Host-owned layout values that a placement policy may change without replacing an element's
/// package-owned source.
/// </summary>
public sealed record CaptionElementPlacement
{
    public CaptionElementPlacement(
        TimeSpan start,
        TimeSpan? length,
        int layer,
        string name,
        Point? position)
    {
        ArgumentNullException.ThrowIfNull(name);
        Start = start;
        Length = length;
        Layer = layer;
        Name = name;
        Position = position;
    }

    public TimeSpan Start { get; init; }

    public TimeSpan? Length { get; init; }

    public int Layer { get; init; }

    public string Name { get; init; }

    public Point? Position { get; init; }

    internal static CaptionElementPlacement FromDescription(ElementDescription description)
        => new(
            description.Start,
            description.Length,
            description.Layer,
            description.Name,
            description.Position);

    internal ElementDescription ApplyTo(ElementDescription description)
        => new(Start, Length, Layer, description.Source, Name, Position);
}

/// <summary>
/// Applies data-only placement independently from element construction. The host always preserves
/// the factory-created <see cref="ElementDescription.Source"/>.
/// </summary>
public interface ICaptionPlacementPolicy
{
    CaptionElementPlacement Place(
        CaptionCue cue,
        CaptionElementContext context,
        CaptionElementPlacement placement,
        int elementIndex);
}

/// <summary>
/// Places requests at the context default when their factory did not request an explicit position.
/// </summary>
public sealed class DefaultCaptionPlacementPolicy : ICaptionPlacementPolicy
{
    public static DefaultCaptionPlacementPolicy Instance { get; } = new();

    public CaptionElementPlacement Place(
        CaptionCue cue,
        CaptionElementContext context,
        CaptionElementPlacement placement,
        int elementIndex)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(context);
        return placement.Position is null
            ? placement with { Position = context.DefaultPosition }
            : placement;
    }
}

/// <summary>
/// Leaves positions supplied by a factory or object template unchanged.
/// </summary>
public sealed class PreserveCaptionPlacementPolicy : ICaptionPlacementPolicy
{
    public static PreserveCaptionPlacementPolicy Instance { get; } = new();

    public CaptionElementPlacement Place(
        CaptionCue cue,
        CaptionElementContext context,
        CaptionElementPlacement placement,
        int elementIndex)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(context);
        return placement;
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

/// <summary>A convenience bundle containing the three independent registrations for a template.</summary>
public sealed class CaptionTemplateRegistrationSet
{
    public CaptionTemplateRegistrationSet(
        CaptionTemplateDescriptorRegistration descriptor,
        CaptionElementFactoryRegistration elementFactory,
        CaptionPlacementPolicyRegistration placement)
    {
        DescriptorRegistration = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        ElementFactoryRegistration = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));
        PlacementPolicyRegistration = placement ?? throw new ArgumentNullException(nameof(placement));
        if (DescriptorRegistration.Descriptor.Id != ElementFactoryRegistration.TemplateId
            || DescriptorRegistration.Descriptor.Id != PlacementPolicyRegistration.TemplateId)
        {
            throw new ArgumentException("Caption template registration identifiers must match.");
        }
    }

    public CaptionTemplateDescriptorRegistration DescriptorRegistration { get; }

    public CaptionElementFactoryRegistration ElementFactoryRegistration { get; }

    public CaptionPlacementPolicyRegistration PlacementPolicyRegistration { get; }
}

internal sealed class CaptionTemplateComposition(
    CaptionTemplateDescriptor descriptor,
    ICaptionElementFactory elementFactory,
    ICaptionPlacementPolicy placementPolicy)
{
    public CaptionTemplateDescriptor Descriptor { get; } = descriptor;

    public ICaptionElementFactory ElementFactory { get; } = elementFactory;

    public ICaptionPlacementPolicy PlacementPolicy { get; } = placementPolicy;

    public IReadOnlyList<ElementDescription> CreateElements(CaptionCue cue, CaptionElementContext context)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<ElementDescription> descriptions = ElementFactory.CreateElements(cue, context)
            ?? throw new InvalidOperationException($"Caption template '{Descriptor.Id}' returned null.");
        if (descriptions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Caption template '{Descriptor.Id}' must create at least one element description.");
        }
        if (descriptions.Any(description => description is null))
        {
            throw new InvalidOperationException(
                $"Caption template '{Descriptor.Id}' returned a null element description.");
        }

        var placed = new ElementDescription[descriptions.Count];
        for (int i = 0; i < descriptions.Count; i++)
        {
            CaptionElementPlacement placement = PlacementPolicy.Place(
                    cue,
                    context,
                    CaptionElementPlacement.FromDescription(descriptions[i]),
                    i)
                ?? throw new InvalidOperationException(
                    $"Caption template '{Descriptor.Id}' placement returned null for element {i}.");
            placed[i] = placement.ApplyTo(descriptions[i]);
        }

        return Array.AsReadOnly(placed);
    }
}

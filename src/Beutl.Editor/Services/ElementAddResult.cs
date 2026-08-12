using Beutl.Editor.Models;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public readonly struct ElementAddFailureId : IEquatable<ElementAddFailureId>
{
    private readonly string? _value;

    public ElementAddFailureId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value.Trim();
    }

    public string Value => _value ?? string.Empty;

    public bool Equals(ElementAddFailureId other)
        => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is ElementAddFailureId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(ElementAddFailureId left, ElementAddFailureId right)
        => left.Equals(right);

    public static bool operator !=(ElementAddFailureId left, ElementAddFailureId right)
        => !left.Equals(right);
}

public static class ElementAddFailureIds
{
    public static ElementAddFailureId EmptyRequest { get; } = new("element.empty-request");

    public static ElementAddFailureId UnsupportedSource { get; } = new("element.unsupported-source");

    public static ElementAddFailureId LockedLayer { get; } = new("element.locked-layer");

    public static ElementAddFailureId Preflight { get; } = new("element.preflight");

    public static ElementAddFailureId Materialization { get; } = new("element.materialization");

    public static ElementAddFailureId InvalidMaterialization { get; }
        = new("element.invalid-materialization");

    public static ElementAddFailureId Persistence { get; } = new("element.persistence");

    public static ElementAddFailureId SceneChanged { get; } = new("element.scene-changed");

    public static ElementAddFailureId SceneMutation { get; } = new("element.scene-mutation");
}

/// <summary>
/// Base type for failures returned by the element-add pipeline. Source handlers may expose
/// their own typed failures with an open identifier.
/// </summary>
public abstract record ElementAddFailure
{
    protected ElementAddFailure(
        ElementAddFailureId id,
        string message,
        Exception? exception = null)
    {
        if (id.Value.Length == 0)
            throw new ArgumentException("A failure identifier is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Id = id;
        Message = message;
        Exception = exception;
    }

    public ElementAddFailureId Id { get; }

    public string Message { get; }

    public Exception? Exception { get; }
}

public sealed record EmptyElementAddRequestFailure : ElementAddFailure
{
    public EmptyElementAddRequestFailure()
        : base(ElementAddFailureIds.EmptyRequest, "At least one element description is required.")
    {
    }
}

public sealed record UnsupportedElementSourceFailure : ElementAddFailure
{
    public UnsupportedElementSourceFailure(Type sourceType, string? message = null)
        : base(
            ElementAddFailureIds.UnsupportedSource,
            message ?? $"No element source handler is registered for '{sourceType.FullName}'.")
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        SourceType = sourceType;
    }

    public Type SourceType { get; }
}

public sealed record LockedElementLayerFailure : ElementAddFailure
{
    public LockedElementLayerFailure(int layer)
        : base(ElementAddFailureIds.LockedLayer, $"Timeline layer {layer} is locked.")
    {
        Layer = layer;
    }

    public int Layer { get; }
}

public sealed record ElementSourcePreflightFailure : ElementAddFailure
{
    public ElementSourcePreflightFailure(string message, Exception? exception = null)
        : base(ElementAddFailureIds.Preflight, message, exception)
    {
    }
}

public sealed record ElementMaterializationFailure : ElementAddFailure
{
    public ElementMaterializationFailure(string message, Exception? exception = null)
        : base(ElementAddFailureIds.Materialization, message, exception)
    {
    }
}

public sealed record InvalidElementMaterializationFailure : ElementAddFailure
{
    public InvalidElementMaterializationFailure(string message)
        : base(ElementAddFailureIds.InvalidMaterialization, message)
    {
    }
}

public sealed record ElementPersistenceFailure : ElementAddFailure
{
    public ElementPersistenceFailure(Exception exception)
        : base(ElementAddFailureIds.Persistence, "The element batch could not be persisted.", exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
    }
}

public sealed record ElementSceneChangedFailure : ElementAddFailure
{
    public ElementSceneChangedFailure(Guid expectedSceneId, Guid actualSceneId)
        : base(
            ElementAddFailureIds.SceneChanged,
            $"The target scene changed from '{expectedSceneId}' to '{actualSceneId}' while elements were materialized.")
    {
        ExpectedSceneId = expectedSceneId;
        ActualSceneId = actualSceneId;
    }

    public Guid ExpectedSceneId { get; }

    public Guid ActualSceneId { get; }
}

public sealed record ElementSceneMutationFailure : ElementAddFailure
{
    public ElementSceneMutationFailure(Exception exception)
        : base(ElementAddFailureIds.SceneMutation, "The element batch could not be added to the scene.", exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
    }
}

public sealed class ElementAddItemResult
{
    public ElementAddItemResult(
        ElementDescription description,
        Element primaryElement,
        IReadOnlyList<Element> companionElements)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(primaryElement);
        ArgumentNullException.ThrowIfNull(companionElements);
        if (companionElements.Any(element => element is null))
            throw new ArgumentException("Companion elements cannot contain null.", nameof(companionElements));

        Description = description;
        PrimaryElement = primaryElement;
        CompanionElements = Array.AsReadOnly(companionElements.ToArray());
        Elements = Array.AsReadOnly(new[] { primaryElement }.Concat(CompanionElements).ToArray());
    }

    public ElementDescription Description { get; }

    public Element PrimaryElement { get; }

    public IReadOnlyList<Element> CompanionElements { get; }

    public IReadOnlyList<Element> Elements { get; }
}

public sealed class ElementAddResult
{
    private ElementAddResult(
        IReadOnlyList<ElementAddItemResult> items,
        ElementAddFailure? failure,
        ElementDescription? failedDescription)
    {
        Items = Array.AsReadOnly(items.ToArray());
        Elements = Array.AsReadOnly(Items.SelectMany(item => item.Elements).ToArray());
        Failure = failure;
        FailedDescription = failedDescription;
    }

    public bool IsSuccess => Failure is null;

    public IReadOnlyList<ElementAddItemResult> Items { get; }

    public IReadOnlyList<Element> Elements { get; }

    public ElementAddFailure? Failure { get; }

    public ElementDescription? FailedDescription { get; }

    public static ElementAddResult Succeeded(IReadOnlyList<ElementAddItemResult> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("A successful add must contain at least one item.", nameof(items));
        if (items.Any(item => item is null))
            throw new ArgumentException("A successful add cannot contain null items.", nameof(items));

        return new ElementAddResult(items, null, null);
    }

    public static ElementAddResult Failed(
        ElementAddFailure failure,
        ElementDescription? failedDescription = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new ElementAddResult([], failure, failedDescription);
    }
}

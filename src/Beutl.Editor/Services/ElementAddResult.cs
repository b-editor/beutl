using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public static class ElementAddFailureIds
{
    public static ElementAddFailureId EmptyRequest { get; } = new("element.empty-request");

    public static ElementAddFailureId UnsupportedSource { get; } = new("element.unsupported-source");

    public static ElementAddFailureId LockedLayer { get; } = new("element.locked-layer");

    public static ElementAddFailureId Preflight { get; } = new("element.preflight");

    public static ElementAddFailureId Materialization { get; } = new("element.materialization");

    public static ElementAddFailureId InvalidMaterialization { get; }
        = new("element.invalid-materialization");

    public static ElementAddFailureId SceneChanged { get; } = new("element.scene-changed");

    public static ElementAddFailureId SceneMutation { get; } = new("element.scene-mutation");
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
    }
}

public sealed record LockedElementLayerFailure : ElementAddFailure
{
    public LockedElementLayerFailure(int layer)
        : base(ElementAddFailureIds.LockedLayer, $"Timeline layer {layer} is locked.")
    {
    }
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

public sealed record ElementSceneChangedFailure : ElementAddFailure
{
    public ElementSceneChangedFailure(Guid expectedSceneId, Guid actualSceneId)
        : base(
            ElementAddFailureIds.SceneChanged,
            $"The target scene changed from '{expectedSceneId}' to '{actualSceneId}' while elements were materialized.")
    {
    }
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
        ElementAddFailure? failure)
    {
        Items = Array.AsReadOnly(items.ToArray());
        Elements = Array.AsReadOnly(Items.SelectMany(item => item.Elements).ToArray());
        Failure = failure;
    }

    public bool IsSuccess => Failure is null;

    public IReadOnlyList<ElementAddItemResult> Items { get; }

    public IReadOnlyList<Element> Elements { get; }

    public ElementAddFailure? Failure { get; }

    public static ElementAddResult Succeeded(IReadOnlyList<ElementAddItemResult> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("A successful add must contain at least one item.", nameof(items));
        if (items.Any(item => item is null))
            throw new ArgumentException("A successful add cannot contain null items.", nameof(items));

        return new ElementAddResult(items, null);
    }

    public static ElementAddResult Failed(ElementAddFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new ElementAddResult([], failure);
    }
}

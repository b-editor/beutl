using Beutl.Editor.PropertyEditors;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors;

// Picks the property whose value another property follows, by clicking it in the property panel.
internal interface IPropertyLinkSelection
{
    IReadOnlyReactiveProperty<bool> IsSelectingLinkTarget { get; }

    // A link whose SetExpression failed; its menu shows the error, or the notification service does when the source
    // is no longer shown.
    IReadOnlyReactiveProperty<PropertyLinkFailure?> Failure { get; }

    // Whether editor shows the property the picking writes, which can be another editor of it after a tab rebuild.
    bool IsSource(PropertyEditorViewModel editor);

    bool CanLinkTo(PropertyEditorViewModel target);

    bool TryChooseLinkTarget(PropertyEditorViewModel target);

    void BeginLinkSelection(PropertyEditorViewModel source);

    void CancelLinkSelection();
}

// A property tab that shows editors from an editor with link selection, for the rows and the prompt.
internal interface IPropertyLinkSelectionHost
{
    IPropertyLinkSelection? LinkSelection { get; }
}

// What a pick keeps of the source, so the link still writes after the element panel disposes its editors for another
// element.
internal sealed class PropertyLinkSource(
    PropertyEditorViewModel editor, IProperty property, CoreObject owner, PropertyLinkExpressionWriter? writer)
{
    public PropertyEditorViewModel Editor { get; } = editor;

    public IProperty Property { get; } = property;

    public CoreObject Owner { get; } = owner;

    public IHierarchical? Hierarchy { get; } = owner as IHierarchical;

    public Type ValueType => Property.ValueType;

    public PropertyLinkExpressionWriter? Writer { get; } = writer;

    public bool Matches(PropertyEditorViewModel editor)
        => !editor.IsDisposed
           && editor.PropertyAdapter.GetEngineProperty() is { } property
           && IsSameProperty(Property, property);

    // The same live property, or one of the copy that stands for several elements, which keeps the owner's Id and name.
    public static bool IsSameProperty(IProperty source, IProperty target)
    {
        if (ReferenceEquals(source, target)) return true;

        CoreObject? sourceOwner = source.GetOwnerObject();
        CoreObject? targetOwner = target.GetOwnerObject();
        return sourceOwner is not null
               && targetOwner is not null
               && sourceOwner.Id.Value == targetOwner.Id.Value
               && source.Name == target.Name;
    }
}

internal sealed record PropertyLinkFailure(PropertyLinkSource Source, string Error);

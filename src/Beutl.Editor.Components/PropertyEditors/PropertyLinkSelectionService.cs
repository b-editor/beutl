using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Language;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors;

// Links a property to another one by clicking it: the source gets the GetProperty<T> expression that reading the
// target would take, as Copy GetProperty Code writes it. The edit commits once, so undo restores the old expression.
internal sealed class PropertyLinkSelectionService : IPropertyLinkSelection, IDisposable
{
    private readonly ReactivePropertySlim<bool> _isSelecting = new();
    private readonly ReactivePropertySlim<PropertyLinkFailure?> _failure = new();
    private PropertyEditorViewModel? _source;
    private bool _disposed;

    public IReadOnlyReactiveProperty<bool> IsSelectingLinkTarget => _isSelecting;

    public PropertyEditorViewModel? Source => _source;

    public Type? SourceValueType => _source?.PropertyAdapter.GetEngineProperty()?.ValueType;

    public IReadOnlyReactiveProperty<PropertyLinkFailure?> Failure => _failure;

    public void BeginLinkSelection(PropertyEditorViewModel source)
    {
        if (_disposed
            || source.IsDisposed
            || source.PropertyAdapter.GetEngineProperty() is not { } property
            || property.GetOwnerObject() is null)
        {
            CancelLinkSelection();
            return;
        }

        // The candidates are those of the new source, so tell the rows even when selection was already on.
        _isSelecting.Value = false;
        _source = source;
        _isSelecting.Value = true;
    }

    public void CancelLinkSelection()
    {
        _source = null;
        if (!_disposed)
            _isSelecting.Value = false;
    }

    public bool CanLinkTo(PropertyEditorViewModel target)
    {
        if (_disposed || !_isSelecting.Value || _source is not { IsDisposed: false } source || target.IsDisposed)
            return false;
        if (target.EditsSeveralElements)
            return false;
        // Another editor of the same property, as its object's own panel shows, is the property itself too.
        if (source.PropertyAdapter.GetEngineProperty() is not { } sourceProperty
            || target.PropertyAdapter.GetEngineProperty() is not { } targetProperty
            || targetProperty.GetOwnerObject() is null
            || IsSameProperty(sourceProperty, targetProperty))
        {
            return false;
        }

        return targetProperty.ValueType == sourceProperty.ValueType;
    }

    public bool TryChooseLinkTarget(PropertyEditorViewModel target)
    {
        if (!CanLinkTo(target) || _source is not { } source) return false;

        if (source.PropertyAdapter.GetEngineProperty() is not { } sourceProperty
            || target.PropertyAdapter.GetEngineProperty() is not { } targetProperty
            || IsSameProperty(sourceProperty, targetProperty))
        {
            return false;
        }

        CoreObject owner = targetProperty.GetOwnerObject()!;
        string typeName = PropertyEditorMenu.GetTypeAlias(targetProperty.ValueType);
        string expression = $"GetProperty<{typeName}>(\"{{{owner.Id.Value}}}.{targetProperty.Name}\")";

        bool set = source.SetExpression(expression, out string? error);
        CancelLinkSelection();
        if (!set)
            _failure.Value = new PropertyLinkFailure(source, error ?? MessageStrings.OperationFailed);
        return set;
    }

    // The same live property, or one of the copy that stands for several elements, which keeps the owner's Id and name.
    private static bool IsSameProperty(IProperty sourceProperty, IProperty targetProperty)
    {
        if (ReferenceEquals(sourceProperty, targetProperty)) return true;

        CoreObject? sourceOwner = sourceProperty.GetOwnerObject();
        CoreObject? targetOwner = targetProperty.GetOwnerObject();
        return sourceOwner is not null
               && targetOwner is not null
               && sourceOwner.Id.Value == targetOwner.Id.Value
               && sourceProperty.Name == targetProperty.Name;
    }

    public void Dispose()
    {
        _disposed = true;
        _source = null;
        _isSelecting.Dispose();
        _failure.Dispose();
    }
}

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
        // The row of the property itself cannot follow itself.
        if (ReferenceEquals(target, source) || target.EditsSeveralElements)
            return false;
        if (target.PropertyAdapter.GetEngineProperty() is not { } targetProperty
            || targetProperty.GetOwnerObject() is null)
            return false;

        return targetProperty.ValueType == SourceValueType;
    }

    public bool TryChooseLinkTarget(PropertyEditorViewModel target)
    {
        if (!CanLinkTo(target) || _source is not { } source) return false;

        IProperty targetProperty = target.PropertyAdapter.GetEngineProperty()!;
        CoreObject owner = targetProperty.GetOwnerObject()!;
        string typeName = PropertyEditorMenu.GetTypeAlias(targetProperty.ValueType);
        string expression = $"GetProperty<{typeName}>(\"{{{owner.Id.Value}}}.{targetProperty.Name}\")";

        bool set = source.SetExpression(expression, out string? error);
        CancelLinkSelection();
        if (!set)
            _failure.Value = new PropertyLinkFailure(source, error ?? MessageStrings.OperationFailed);
        return set;
    }

    public void Dispose()
    {
        _disposed = true;
        _source = null;
        _isSelecting.Dispose();
        _failure.Dispose();
    }
}

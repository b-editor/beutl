using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Language;
using Beutl.Services;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors;

// Links a property to another one by clicking it: the source gets the GetProperty<T> expression that reading the
// target would take, as Copy GetProperty Code writes it. The edit commits once, so undo restores the old expression.
internal sealed class PropertyLinkSelectionService(INotificationService notifications) : IPropertyLinkSelection, IDisposable
{
    private readonly ReactivePropertySlim<bool> _isSelecting = new();
    private readonly ReactivePropertySlim<PropertyLinkFailure?> _failure = new();
    private PropertyLinkSource? _source;
    private bool _disposed;

    public IReadOnlyReactiveProperty<bool> IsSelectingLinkTarget => _isSelecting;

    public IReadOnlyReactiveProperty<PropertyLinkFailure?> Failure => _failure;

    public bool IsSource(PropertyEditorViewModel editor)
        => _source is { } source && source.Matches(editor);

    public void BeginLinkSelection(PropertyEditorViewModel source)
    {
        if (_disposed
            || source.IsDisposed
            || source.PropertyAdapter.GetEngineProperty() is not { } property
            || property.GetOwnerObject() is not { } owner)
        {
            CancelLinkSelection();
            return;
        }

        // The candidates are those of the new source, so tell the rows even when selection was already on.
        CancelLinkSelection();
        _source = new PropertyLinkSource(source, property, owner, source.CreateLinkExpressionWriter());
        // The panel can stop showing the source element; removing it from the scene ends the picking.
        if (_source.Hierarchy is { } hierarchy)
            hierarchy.DetachedFromHierarchy += OnSourceDetached;
        _isSelecting.Value = true;
    }

    public void CancelLinkSelection()
    {
        UnsubscribeSource();
        if (!_disposed)
            _isSelecting.Value = false;
    }

    public bool CanLinkTo(PropertyEditorViewModel target)
    {
        if (_disposed || !_isSelecting.Value || _source is not { } source || target.IsDisposed)
            return false;
        if (target.EditsSeveralElements)
            return false;
        // Another editor of the same property, as its object's own panel shows, is the property itself too.
        if (target.PropertyAdapter.GetEngineProperty() is not { } targetProperty
            || targetProperty.GetOwnerObject() is null
            || PropertyLinkSource.IsSameProperty(source.Property, targetProperty))
        {
            return false;
        }

        return targetProperty.ValueType == source.ValueType;
    }

    public bool TryChooseLinkTarget(PropertyEditorViewModel target)
    {
        if (!CanLinkTo(target) || _source is not { } source) return false;

        if (target.PropertyAdapter.GetEngineProperty() is not { } targetProperty
            || targetProperty.GetOwnerObject() is not { } owner
            || PropertyLinkSource.IsSameProperty(source.Property, targetProperty))
        {
            return false;
        }

        string typeName = PropertyEditorMenu.GetTypeAlias(targetProperty.ValueType);
        string expression = $"GetProperty<{typeName}>(\"{{{owner.Id.Value}}}.{targetProperty.Name}\")";

        bool set;
        string? error;
        if (source.Editor is { IsDisposed: false } editor)
            set = editor.SetExpression(expression, out error);
        else if (source.Writer is { } writer)
            set = writer.TrySet(expression, out error);
        else
        {
            set = false;
            error = MessageStrings.OperationFailed;
        }

        CancelLinkSelection();
        if (!set)
        {
            string message = error ?? MessageStrings.OperationFailed;
            if (source.Editor is { IsDisposed: false })
            {
                _failure.Value = new PropertyLinkFailure(source, message);
                // The shown menu took it synchronously; a menu built later must not reopen an old error.
                _failure.Value = null;
            }
            else
                notifications.ShowError(Strings.LinkToAnotherProperty, message);
        }

        return set;
    }

    private void UnsubscribeSource()
    {
        if (_source?.Hierarchy is { } hierarchy)
            hierarchy.DetachedFromHierarchy -= OnSourceDetached;
        _source = null;
    }

    private void OnSourceDetached(object? sender, HierarchyAttachmentEventArgs e) => CancelLinkSelection();

    public void Dispose()
    {
        _disposed = true;
        UnsubscribeSource();
        _isSelecting.Dispose();
        _failure.Dispose();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// Shows the picking prompt at the top of a property tab and ends picking on Esc, on a press outside the rows or
// when the tab goes away.
internal sealed class PropertyLinkSelectionAttachment : IDisposable
{
    private readonly Control _view;
    private readonly Control _prompt;
    private IPropertyLinkSelection? _selection;
    private IDisposable? _subscription;
    private TopLevel? _topLevel;
    private bool _attached;

    public PropertyLinkSelectionAttachment(Control view, Control prompt)
    {
        _view = view;
        _prompt = prompt;
        _prompt.IsVisible = false;
        _view.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _view.AttachedToVisualTree += OnAttachedToVisualTree;
        _view.DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public void SetDataContext(object? dataContext)
    {
        Unsubscribe();
        // Another tab replaces this one's picking rather than retargeting it.
        _selection?.CancelLinkSelection();
        _selection = (dataContext as IPropertyLinkSelectionHost)?.LinkSelection;
        _prompt.IsVisible = false;
        if (_attached)
            Subscribe();
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (_selection is { } selection)
            _subscription = selection.IsSelectingLinkTarget.Subscribe(selecting => _prompt.IsVisible = selecting);
    }

    private void Unsubscribe()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attached = true;
        _topLevel = TopLevel.GetTopLevel(_view);
        _topLevel?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        // A tab that is shown again follows the service again, from its current state.
        Subscribe();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _topLevel = null;
        }

        // A detached view keeps no reference from the service, and picking ends without changes.
        Unsubscribe();
        _prompt.IsVisible = false;
        _selection?.CancelLinkSelection();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _selection is not { IsSelectingLinkTarget.Value: true }) return;

        _selection.CancelLinkSelection();
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_selection is not { IsSelectingLinkTarget.Value: true }) return;
        // The rows' overlays take their own presses, and their menus must stay usable to cancel.
        if (e.Source is Visual source
            && (source.GetSelfAndVisualAncestors().OfType<PropertyLinkSelectionOverlay>().Any()
                || source.GetSelfAndVisualAncestors().OfType<PropertyEditorMenu>().Any()))
        {
            return;
        }

        _selection.CancelLinkSelection();
    }

    public void Dispose()
    {
        _attached = false;
        _view.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _view.AttachedToVisualTree -= OnAttachedToVisualTree;
        _view.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _topLevel = null;
        }

        Unsubscribe();
        _selection = null;
    }
}

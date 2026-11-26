using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Beutl.Views;

internal sealed class InWindowEditMenuFocus
{
    private readonly Menu _menu;
    private (TextBox Box, int Start, int End)? _selection;

    internal InWindowEditMenuFocus(Menu menu, CompositeDisposable disposables)
    {
        _menu = menu;
        disposables.Add(menu.AddDisposableHandler(InputElement.GettingFocusEvent, OnGettingFocus, handledEventsToo: true));
        disposables.Add(menu.AddDisposableHandler(InputElement.LostFocusEvent, OnLostFocus, handledEventsToo: true));
        disposables.Add(menu.AddDisposableHandler(MenuBase.ClosedEvent, OnMenuClosed, handledEventsToo: true));
        menu.DetachedFromVisualTree += OnMenuDetached;
        disposables.Add(Disposable.Create(() => menu.DetachedFromVisualTree -= OnMenuDetached));
    }

    internal InputElement? Target { get; private set; }

    internal void RestoreSelection()
    {
        // Refocusing the input ends the menu's focus session, so read the selection first.
        var selection = _selection;
        Target?.Focus();
        if (selection is { } saved && saved.Box.SelectionStart == saved.Box.SelectionEnd)
        {
            saved.Box.SetCurrentValue(TextBox.SelectionStartProperty, saved.Start);
            saved.Box.SetCurrentValue(TextBox.SelectionEndProperty, saved.End);
        }
    }

    private void OnGettingFocus(object? sender, FocusChangingEventArgs e)
    {
        if (IsMenuFocus(e.OldFocusedElement))
            return;

        // Pointer presses and keyboard navigation can focus an item before its submenu opens.
        // GettingFocus still sees the text selection that TextBox.OnLostFocus is about to clear.
        Clear();
        if (e.OldFocusedElement is InputElement previous && TopLevel.GetTopLevel(previous) == TopLevel.GetTopLevel(_menu)
            && FocusedEditCommand.IsTextInput(previous))
        {
            Target = previous;
            if (previous.FindAncestorOfType<TextBox>(includeSelf: true) is { } box && box.SelectionStart != box.SelectionEnd)
                _selection = (box, box.SelectionStart, box.SelectionEnd);
        }
    }

    private void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!IsMenuFocus(e.NewFocusedElement))
            Clear();
    }

    private void OnMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (e.Source == _menu)
            Clear();
    }

    private void OnMenuDetached(object? sender, VisualTreeAttachmentEventArgs e) => Clear();

    private bool IsMenuFocus(IInputElement? element)
        => element is ILogical logical && (element == _menu || _menu.IsLogicalAncestorOf(logical));

    private void Clear()
    {
        Target = null;
        _selection = null;
    }
}

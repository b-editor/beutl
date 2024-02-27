using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Reactive;

using FluentAvalonia.Core;

#nullable enable

namespace Beutl.Controls.PropertyEditors;

// PickerFlyoutPresenter.cs
public class DraggablePickerFlyoutPresenter : ContentControl
{
    private readonly CompositeDisposable _disposables = [];
    private Panel? _dragArea;
    private Button? _closebutton;
    private Button? _acceptButton;
    private Button? _dismissButton;

    private bool _pressed;
    private Point _point;

    private const string AcceptDismiss = ":acceptdismiss";
    private const string AcceptButton = "AcceptButton";
    private const string DismissButton = "DismissButton";
    private const string CloseButton = "CloseButton";
    private const string DragArea = "DragArea";

    public DraggablePickerFlyoutPresenter()
    {
        PseudoClasses.Add(AcceptDismiss);
    }

    public event TypedEventHandler<DraggablePickerFlyoutPresenter, EventArgs>? Confirmed;

    public event TypedEventHandler<DraggablePickerFlyoutPresenter, EventArgs>? Dismissed;

    public event TypedEventHandler<DraggablePickerFlyoutPresenter, EventArgs>? CloseClicked;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);

        _acceptButton = e.NameScope.Find<Button>(AcceptButton);
        _dismissButton = e.NameScope.Find<Button>(DismissButton);
        _dragArea = e.NameScope.Find<Panel>(DragArea);
        _closebutton = e.NameScope.Find<Button>(CloseButton);

        _acceptButton?.AddDisposableHandler(Button.ClickEvent, OnAcceptClick)
            .DisposeWith(_disposables);

        _dismissButton?.AddDisposableHandler(Button.ClickEvent, OnDismissClick)
            .DisposeWith(_disposables);

        _closebutton?.AddDisposableHandler(Button.ClickEvent, OnCloseClick)
            .DisposeWith(_disposables);

        if (_dragArea != null)
        {
            _dragArea.AddDisposableHandler(PointerPressedEvent, OnDragAreaPointerPressed)
                .DisposeWith(_disposables);
            _dragArea.AddDisposableHandler(PointerReleasedEvent, OnDragAreaPointerReleased)
                .DisposeWith(_disposables);
            _dragArea.AddDisposableHandler(PointerMovedEvent, OnDragAreaPointerMoved)
                .DisposeWith(_disposables);
            _dragArea.AddDisposableHandler(PointerExitedEvent, OnDragAreaPointerExited)
                .DisposeWith(_disposables);
        }
    }

    protected override bool RegisterContentPresenter(ContentPresenter presenter)
    {
        if (presenter.Name == "ContentPresenter")
            return true;

        return base.RegisterContentPresenter(presenter);
    }

    private void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
    }

    internal void ShowHideButtons(bool show)
    {
        PseudoClasses.Set(AcceptDismiss, show);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnDragAreaPointerExited(object? sender, PointerEventArgs e)
    {
        _pressed = false;
    }

    private void OnDragAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressed = false;
    }

    private void OnDragAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragArea == null || !_pressed) return;

        PointerPoint pointer = e.GetCurrentPoint(null);
        Point point = pointer.Position;
        Point delta = point - _point;

        if (TopLevel.GetTopLevel(this) is PopupRoot { Parent: Popup popup })
        {
            popup.HorizontalOffset += delta.X;
            popup.VerticalOffset += delta.Y;
        }
    }

    private void OnDragAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_dragArea == null) return;

        PointerPoint pointer = e.GetCurrentPoint(null);
        if (pointer.Properties.IsLeftButtonPressed)
        {
            _pressed = true;
            _point = pointer.Position;
        }
    }
}

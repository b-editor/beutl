using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Reactive;
using FluentAvalonia.Core;

#nullable enable

namespace Beutl.Controls.PropertyEditors;

// PickerFlyoutPresenter.cs
public class DraggablePickerFlyoutPresenter : ContentControl
{
    public static readonly StyledProperty<bool> ShowHideButtonsProperty =
        AvaloniaProperty.Register<DraggablePickerFlyoutPresenter, bool>(nameof(ShowHideButtons));

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
    }

    public event TypedEventHandler<DraggablePickerFlyoutPresenter, EventArgs>? Confirmed;

    public event TypedEventHandler<DraggablePickerFlyoutPresenter, EventArgs>? Dismissed;

    public event TypedEventHandler<DraggablePickerFlyoutPresenter, EventArgs>? CloseClicked;

    public bool ShowHideButtons
    {
        get => GetValue(ShowHideButtonsProperty);
        set => SetValue(ShowHideButtonsProperty, value);
    }

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowHideButtonsProperty)
        {
            PseudoClasses.Set(AcceptDismiss, ShowHideButtons);
        }
    }

    private void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnDragAreaPointerExited(object? sender, PointerEventArgs e)
    {
        // _pressed = false;
    }

    private void OnDragAreaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressed = false;
    }

    private void OnDragAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragArea == null || !_pressed) return;

        if (this.FindLogicalAncestorOfType<Popup>() is { } popup)
        {
            PointerPoint pointer = e.GetCurrentPoint(null);
            Point point = pointer.Position;
            Point delta = point - _point;

            popup.HorizontalOffset += delta.X;
            popup.VerticalOffset += delta.Y;
            if (this.FindAncestorOfType<PopupRoot>() == null)
            {
                _point = point;
            }
        }
    }

    private void OnDragAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_dragArea == null) return;

        var root = this.FindAncestorOfType<PopupRoot>();
        PointerPoint pointer = e.GetCurrentPoint(null);
        if (pointer.Properties.IsLeftButtonPressed)
        {
            _pressed = true;
            _point = pointer.Position;
            root?.Activate();
        }
    }
}

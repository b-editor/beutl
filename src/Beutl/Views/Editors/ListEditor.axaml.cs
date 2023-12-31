using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

using Beutl.Services;
using Beutl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public sealed class ListEditorDragBehavior : Behavior<Control>
{
    private bool _enableDrag;
    private bool _dragStarted;
    private Point _start;
    private int _draggedIndex;
    private int _targetIndex;
    private ItemsControl? _itemsControl;
    private Control? _draggedContainer;

    public static readonly StyledProperty<double> DragThresholdProperty =
        AvaloniaProperty.Register<ListEditorDragBehavior, double>(nameof(DragThreshold), 3);

    public static readonly StyledProperty<Control> DragControlProperty =
        AvaloniaProperty.Register<ListEditorDragBehavior, Control>(nameof(DragControl));

    public double DragThreshold
    {
        get => GetValue(DragThresholdProperty);
        set => SetValue(DragThresholdProperty, value);
    }

    [ResolveByName]
    public Control DragControl
    {
        get => GetValue(DragControlProperty);
        set => SetValue(DragControlProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (DragControl is { })
        {
            DragControl.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
            DragControl.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
            DragControl.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
            DragControl.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (DragControl is { })
        {
            DragControl.RemoveHandler(InputElement.PointerReleasedEvent, Released);
            DragControl.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
            DragControl.RemoveHandler(InputElement.PointerMovedEvent, Moved);
            DragControl.RemoveHandler(InputElement.PointerCaptureLostEvent, CaptureLost);
        }
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(AssociatedObject).Properties;
        if (properties.IsLeftButtonPressed
            && AssociatedObject?.FindLogicalAncestorOfType<ItemsControl>() is { } itemsControl)
        {
            _enableDrag = true;
            _dragStarted = false;
            _start = e.GetPosition(itemsControl);
            _draggedIndex = -1;
            _targetIndex = -1;
            _itemsControl = itemsControl;
            _draggedContainer = AssociatedObject?.FindAncestorOfType<ContentPresenter>();

            if (_draggedContainer is { })
            {
                SetDraggingPseudoClasses(_draggedContainer, true);
            }

            AddTransforms(_itemsControl);
        }
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_enableDrag)
        {
            if (_dragStarted)
            {
                e.Handled = true;
            }

            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                Released();
            }
        }
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragStarted)
        {
            e.Handled = true;
        }

        Released();
    }

    private void Released()
    {
        if (!_enableDrag)
        {
            return;
        }

        RemoveTransforms(_itemsControl);

        if (_itemsControl is { })
        {
            foreach (Control container in _itemsControl.GetRealizedContainers())
            {
                SetDraggingPseudoClasses(container, true);
            }
        }

        if (_dragStarted && _draggedIndex >= 0 && _targetIndex >= 0 && _draggedIndex != _targetIndex)
        {
            OnMoveDraggedItem(_itemsControl, _draggedIndex, _targetIndex);
        }

        if (_itemsControl is { })
        {
            foreach (Control container in _itemsControl.GetRealizedContainers())
            {
                SetDraggingPseudoClasses(container, false);
            }
        }

        if (_draggedContainer is { })
        {
            SetDraggingPseudoClasses(_draggedContainer, false);
        }

        _draggedIndex = -1;
        _targetIndex = -1;
        _enableDrag = false;
        _dragStarted = false;
        _itemsControl = null;

        _draggedContainer = null;
    }

    private static void AddTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl?.Items is null)
        {
            return;
        }

        int i = 0;

        foreach (object? _ in itemsControl.Items)
        {
            Control? container = itemsControl.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    private static void RemoveTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl?.Items is null)
        {
            return;
        }

        int i = 0;

        foreach (object? _ in itemsControl.Items)
        {
            Control? container = itemsControl.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    private static void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
    {
        if (itemsControl?.DataContext is IListEditorViewModel viewModel)
        {
            viewModel.MoveItem(oldIndex, newIndex);
        }
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        PointerPointProperties? properties = e.GetCurrentPoint(AssociatedObject).Properties;
        if (_enableDrag
            && properties?.IsLeftButtonPressed == true)
        {
            if (_itemsControl?.Items is null || _draggedContainer?.RenderTransform is null || !_enableDrag)
            {
                return;
            }

            Point position = e.GetPosition(_itemsControl);
            double delta = position.Y - _start.Y;

            if (!_dragStarted)
            {
                Point diff = _start - position;
                double dragThreshold = DragThreshold;

                if (Math.Abs(diff.Y) > dragThreshold)
                {
                    _dragStarted = true;
                }
                else
                {
                    return;
                }
            }

            SetTranslateTransform(_draggedContainer, 0, delta);

            _draggedIndex = _itemsControl.IndexFromContainer(_draggedContainer);
            _targetIndex = -1;

            Rect draggedBounds = _draggedContainer.Bounds;
            double draggedStart = draggedBounds.Y;
            double draggedDeltaStart = draggedBounds.Y + delta;
            double draggedDeltaEnd = draggedBounds.Y + delta + draggedBounds.Height;

            int i = 0;

            foreach (object? _ in _itemsControl.Items)
            {
                Control? targetContainer = _itemsControl.ContainerFromIndex(i);
                if (targetContainer?.RenderTransform is null || ReferenceEquals(targetContainer, _draggedContainer))
                {
                    i++;
                    continue;
                }

                Rect targetBounds = targetContainer.Bounds;
                double targetStart = targetBounds.Y;
                double targetMid = targetBounds.Y + targetBounds.Height / 2;
                int targetIndex = _itemsControl.IndexFromContainer(targetContainer);

                if (targetStart > draggedStart && draggedDeltaEnd >= targetMid)
                {
                    SetTranslateTransform(targetContainer, 0, -draggedBounds.Height);

                    _targetIndex = _targetIndex == -1 ? targetIndex :
                        targetIndex > _targetIndex ? targetIndex : _targetIndex;
                }
                else if (targetStart < draggedStart && draggedDeltaStart <= targetMid)
                {
                    SetTranslateTransform(targetContainer, 0, draggedBounds.Height);

                    _targetIndex = _targetIndex == -1 ? targetIndex :
                        targetIndex < _targetIndex ? targetIndex : _targetIndex;
                }
                else
                {
                    SetTranslateTransform(targetContainer, 0, 0);
                }

                i++;
            }
        }
    }

    private static void SetDraggingPseudoClasses(Control control, bool isDragging)
    {
        ((IPseudoClasses)control.Classes).Set(":dragging", isDragging);
    }

    private static void SetTranslateTransform(Control control, double x, double y)
    {
        var transformBuilder = new TransformOperations.Builder(1);
        transformBuilder.AppendTranslate(x, y);
        control.RenderTransform = transformBuilder.Build();
    }
}

public partial class ListEditor : UserControl
{
    private static readonly Lazy<CrossFade> s_crossFade = new(() => new(TimeSpan.FromSeconds(0.25)));
    private CancellationTokenSource? _lastTransitionCts;

    public ListEditor()
    {
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async value =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();

                if (value == true)
                {
                    await s_crossFade.Value.Start(null, expandItem, _lastTransitionCts.Token);
                }
                else
                {
                    await s_crossFade.Value.Start(expandItem, null, _lastTransitionCts.Token);
                }

                expandItem.IsVisible = value == true;
            });
    }

    private void InitializeClick(object? sender, RoutedEventArgs e)
    {
        OnInitializeClick();
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        OnDeleteClick();
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        await OnAddClick(sender);
    }

    protected virtual void OnInitializeClick()
    {
    }

    protected virtual void OnDeleteClick()
    {
    }

    protected virtual ValueTask OnAddClick(object? sender)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class ListEditor<TItem> : ListEditor
{
    protected override void OnInitializeClick()
    {
        if (DataContext is ListEditorViewModel<TItem> viewModel)
        {
            try
            {
                viewModel.Initialize();
            }
            catch (InvalidOperationException ex)
            {
                NotificationService.ShowError("Error", ex.Message);
            }
        }
    }

    protected override void OnDeleteClick()
    {
        if (DataContext is ListEditorViewModel<TItem> viewModel)
        {
            try
            {
                viewModel.Delete();
            }
            catch (InvalidOperationException ex)
            {
                NotificationService.ShowError("Error", ex.Message);
            }
        }
    }

    protected override async ValueTask OnAddClick(object? sender)
    {
        if (DataContext is ListEditorViewModel<TItem> viewModel)
        {
            if (viewModel.List.Value == null && sender is Button btn)
            {
                btn.ContextFlyout?.ShowAt(btn);
            }
            else if (viewModel.List.Value != null)
            {
                progress.IsVisible = progress.IsIndeterminate = true;

                await Task.Run(async () =>
                {
                    Type itemType = typeof(TItem);
                    Type[]? availableTypes = null;

                    if (itemType.IsSealed
                        && (itemType.GetConstructor([]) != null
                        || itemType.GetConstructors().Length == 0))
                    {
                        availableTypes = [itemType];
                    }
                    else
                    {
                        availableTypes = AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(x => x.GetTypes())
                            .Where(x => !x.IsAbstract
                                && x.IsPublic
                                && x.IsAssignableTo(itemType)
                                && (itemType.GetConstructor([]) != null
                                || itemType.GetConstructors().Length == 0))
                            .ToArray();
                    }

                    Type? selectedType = null;

                    if (availableTypes.Length == 1)
                    {
                        selectedType = availableTypes[0];
                    }
                    else if (availableTypes.Length > 1)
                    {
                        selectedType = await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var combobox = new ComboBox
                            {
                                ItemsSource = availableTypes,
                                SelectedIndex = 0
                            };

                            var dialog = new ContentDialog
                            {
                                Content = combobox,
                                Title = Message.MultipleTypesAreAvailable,
                                PrimaryButtonText = Strings.OK,
                                CloseButtonText = Strings.Cancel
                            };

                            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                            {
                                return combobox.SelectedItem as Type;
                            }
                            else
                            {
                                return null;
                            }
                        });
                    }

                    if (selectedType != null && Activator.CreateInstance(selectedType) is TItem item)
                    {
                        viewModel.AddItem(item);
                    }
                    else
                    {
                        NotificationService.ShowError("Error", "ListEditor<TItem>.OnAddClick");
                    }
                });

                progress.IsVisible = progress.IsIndeterminate = false;
            }
        }
    }
}

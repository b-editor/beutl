using System.Collections;
using System.Reflection;

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
using Avalonia.Xaml.Interactivity;

using BeUtl.Commands;
using BeUtl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

using Button = Avalonia.Controls.Button;
using ComboBox = Avalonia.Controls.ComboBox;

namespace BeUtl.Views.Editors;

public sealed class ListEditorDragBehavior : Behavior<Border>
{
    private bool _enableDrag;
    private bool _dragStarted;
    private Point _start;
    private int _draggedIndex;
    private int _targetIndex;
    private ItemsControl? _itemsControl;
    private IControl? _draggedContainer;

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, DragBorder_Released, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, DragBorder_Pressed, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, DragBorder_Moved, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerCaptureLostEvent, DragBorder_CaptureLost, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject != null)
        {
            AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, DragBorder_Released);
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, DragBorder_Pressed);
            AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, DragBorder_Moved);
            AssociatedObject.RemoveHandler(InputElement.PointerCaptureLostEvent, DragBorder_CaptureLost);
        }
    }

    private void DragBorder_Released(object? sender, PointerReleasedEventArgs e)
    {
        DragBorder_Released();

        e.Handled = true;
    }

    private void DragBorder_Pressed(object? sender, PointerPressedEventArgs e)
    {
        _enableDrag = true;
        _dragStarted = false;
        _draggedIndex = -1;
        _targetIndex = -1;
        _itemsControl = AssociatedObject.FindLogicalAncestorOfType<ItemsControl>();
        _start = e.GetPosition(_itemsControl);
        _draggedContainer = AssociatedObject.FindLogicalAncestorOfType<ContentPresenter>();

        AddTransforms(_itemsControl);

        e.Handled = true;
    }

    private void DragBorder_Moved(object? sender, PointerEventArgs e)
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
            const int verticalDragThreshold = 3;

            if (Math.Abs(diff.Y) > verticalDragThreshold)
            {
                _dragStarted = true;
            }
            else
            {
                return;
            }
        }

        SetTranslateTransform(_draggedContainer, 0, delta);

        _draggedIndex = _itemsControl.ItemContainerGenerator.IndexFromContainer(_draggedContainer);
        _targetIndex = -1;

        Rect draggedBounds = _draggedContainer.Bounds;
        double draggedStart = draggedBounds.Y;
        double draggedDeltaStart = draggedBounds.Y + delta;
        double draggedDeltaEnd = draggedBounds.Y + delta + draggedBounds.Height;

        int i = 0;

        foreach (object? _ in _itemsControl.Items)
        {
            IControl? targetContainer = _itemsControl.ItemContainerGenerator.ContainerFromIndex(i);
            if (targetContainer?.RenderTransform is null || ReferenceEquals(targetContainer, _draggedContainer))
            {
                i++;
                continue;
            }

            Rect targetBounds = targetContainer.Bounds;
            double targetStart = targetBounds.Y;
            double targetMid = targetBounds.Y + (targetBounds.Height / 2);
            int targetIndex = _itemsControl.ItemContainerGenerator.IndexFromContainer(targetContainer);

            if (targetStart > draggedStart && draggedDeltaEnd >= targetMid)
            {
                SetTranslateTransform(targetContainer, 0, -draggedBounds.Height);

                _targetIndex = _targetIndex == -1 ?
                    targetIndex :
                    targetIndex > _targetIndex ? targetIndex : _targetIndex;
                Debug.WriteLine($"Moved Right {_draggedIndex} -> {_targetIndex}");
            }
            else if (targetStart < draggedStart && draggedDeltaStart <= targetMid)
            {
                SetTranslateTransform(targetContainer, 0, draggedBounds.Height);

                _targetIndex = _targetIndex == -1 ?
                    targetIndex :
                    targetIndex < _targetIndex ? targetIndex : _targetIndex;
                Debug.WriteLine($"Moved Left {_draggedIndex} -> {_targetIndex}");
            }
            else
            {
                SetTranslateTransform(targetContainer, 0, 0);
            }

            i++;
        }

        Debug.WriteLine($"Moved {_draggedIndex} -> {_targetIndex}");

        e.Handled = true;
    }

    private void DragBorder_CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        DragBorder_Released();
    }

    private void DragBorder_Released()
    {
        if (!_enableDrag)
        {
            return;
        }

        RemoveTransforms(_itemsControl);

        if (_dragStarted && _draggedIndex >= 0 && _targetIndex >= 0 && _draggedIndex != _targetIndex)
        {
            Debug.WriteLine($"MoveItem {_draggedIndex} -> {_targetIndex}");
            MoveDraggedItem(_itemsControl, _draggedIndex, _targetIndex);
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

        foreach (object _ in itemsControl.Items)
        {
            IControl? container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i);
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

        foreach (object _ in itemsControl.Items)
        {
            IControl container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    private static void MoveDraggedItem(ItemsControl? itemsControl, int draggedIndex, int targetIndex)
    {
        if (itemsControl?.Items is not IList items)
        {
            return;
        }

        new MoveCommand(items, targetIndex, draggedIndex).DoAndRecord(CommandRecorder.Default);
    }

    private static void SetTranslateTransform(IControl control, double x, double y)
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
        toggle.GetObservable(ToggleButton.IsCheckedProperty)
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

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        progress.IsVisible = true;
        if (DataContext is ListEditorViewModel viewModel && viewModel.List.Value != null)
        {
            await Task.Run(async () =>
            {
                Type type = viewModel.WrappedProperty.AssociatedProperty.PropertyType;
                Type? interfaceType = type.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>));
                Type? itemtype = interfaceType?.GenericTypeArguments?.FirstOrDefault();
                if (itemtype != null)
                {
                    var types = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(x => x.GetTypes())
                        .Where(x => !x.IsAbstract
                            && x.IsPublic
                            && x.IsAssignableTo(itemtype)
                            && x.GetConstructor(Array.Empty<Type>()) != null)
                        .ToArray();
                    Type? type2 = null;
                    ConstructorInfo? constructorInfo = null;

                    if (types.Length == 1)
                    {
                        type2 = types[0];
                    }
                    else if (types.Length > 1)
                    {
                        type2 = await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var combobox = new ComboBox
                            {
                                Items = types,
                                SelectedIndex = 0
                            };

                            var dialog = new ContentDialog
                            {
                                Content = combobox,
                                Title = "複数の型が利用可能です",
                                PrimaryButtonText = "決定",
                                CloseButtonText = "キャンセル"
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
                    else if (itemtype.IsSealed)
                    {
                        type2 = itemtype;
                    }

                    constructorInfo = type2?.GetConstructor(Array.Empty<Type>());

                    if (constructorInfo != null)
                    {
                        var obj = constructorInfo.Invoke(null);
                        if (obj != null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => viewModel.List.Value.Add(obj));
                        }
                    }
                }
            });
        }

        progress.IsVisible = false;
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is ILogical logical
            && DataContext is ListEditorViewModel { List.Value: { } list } viewModel
            && this.FindLogicalAncestorOfType<ObjectPropertyEditor>().DataContext is ObjectPropertyEditorViewModel parentViewModel)
        {
            var grid = logical.FindLogicalAncestorOfType<Grid>();
            var index = items.ItemContainerGenerator.IndexFromContainer(grid.Parent);

            if (index >= 0 && list[index] is CoreObject obj)
            {
                parentViewModel.NavigateCore(obj, false);
            }
        }
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem
            && DataContext is ListEditorViewModel { List.Value: { } list } viewModel)
        {
            var grid = menuItem.FindLogicalAncestorOfType<Grid>();
            var index = items.ItemContainerGenerator.IndexFromContainer(grid.Parent);

            if (index >= 0)
            {
                new RemoveCommand(list, list[index]).DoAndRecord(CommandRecorder.Default);
            }
        }
    }
}

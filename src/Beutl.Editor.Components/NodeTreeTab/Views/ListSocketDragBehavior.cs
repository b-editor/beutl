using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

using Beutl.Editor.Components.NodeTreeTab.ViewModels;

namespace Beutl.Editor.Components.NodeTreeTab.Views;

public sealed class ListSocketDragBehavior : Behavior<SocketPoint>
{
    private enum DragDirection { None, Vertical, Horizontal }

    private const double DragThreshold = 5;

    private DragDirection _direction;
    private bool _enableDrag;
    private bool _dragStarted;
    private Point _start;
    private int _draggedIndex;
    private int _targetIndex;
    private StackPanel? _stackPanel;
    private Canvas? _canvas;
    private NodeView? _nodeView;
    private ConnectionLine? _tempLine;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (AssociatedObject == null) return;

        if (e.ClickCount == 2)
        {
            if (AssociatedObject.Tag is ConnectionViewModel connVM
                && AssociatedObject.DataContext is SocketViewModel socketVM)
            {
                socketVM.DisconnectConnection(connVM);
            }
            e.Handled = true;
            return;
        }

        PointerPoint point = e.GetCurrentPoint(AssociatedObject);
        if (point.Properties.IsLeftButtonPressed)
        {
            _stackPanel = AssociatedObject.FindAncestorOfType<StackPanel>();
            _canvas = AssociatedObject.FindAncestorOfType<Canvas>();
            _nodeView = AssociatedObject.FindAncestorOfType<NodeView>();
            if (_stackPanel != null)
            {
                _enableDrag = true;
                _dragStarted = false;
                _direction = DragDirection.None;
                _start = e.GetPosition(_stackPanel);
                _draggedIndex = _stackPanel.Children.IndexOf(AssociatedObject);
                _targetIndex = -1;

                AddTransforms();

                e.Handled = true;
                e.Pointer.Capture(AssociatedObject);
            }
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_enableDrag || AssociatedObject == null || _stackPanel == null) return;

        Point position = e.GetPosition(_stackPanel);
        double deltaX = position.X - _start.X;
        double deltaY = position.Y - _start.Y;

        if (!_dragStarted)
        {
            if (Math.Abs(deltaX) < DragThreshold && Math.Abs(deltaY) < DragThreshold)
            {
                e.Handled = true;
                return;
            }

            _dragStarted = true;
            _direction = Math.Abs(deltaY) > Math.Abs(deltaX)
                ? DragDirection.Vertical
                : DragDirection.Horizontal;
        }

        if (_direction == DragDirection.Vertical)
        {
            MoveVertical(deltaY);
        }
        else if (_direction == DragDirection.Horizontal)
        {
            MoveHorizontal(e);
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_enableDrag) return;

        if (_dragStarted)
        {
            if (_direction == DragDirection.Vertical)
            {
                ReleaseVertical();
            }
            else if (_direction == DragDirection.Horizontal)
            {
                ReleaseHorizontal(e);
            }
        }

        e.Handled = true;
        e.Pointer.Capture(null);
        CleanUp();
    }

    private void MoveVertical(double delta)
    {
        if (_stackPanel == null || AssociatedObject?.RenderTransform == null) return;

        SetTranslateTransform(AssociatedObject, 0, delta);

        _targetIndex = -1;
        Rect draggedBounds = AssociatedObject.Bounds;
        double draggedStart = draggedBounds.Y;
        double draggedDeltaStart = draggedBounds.Y + delta;
        double draggedDeltaEnd = draggedBounds.Y + delta + draggedBounds.Height;

        int connectedCount = _stackPanel.Children.Count - 1; // exclude placeholder
        for (int i = 0; i < connectedCount; i++)
        {
            if (_stackPanel.Children[i] is not SocketPoint target
                || ReferenceEquals(target, AssociatedObject)
                || target.RenderTransform == null)
            {
                continue;
            }

            Rect targetBounds = target.Bounds;
            double targetStart = targetBounds.Y;
            double targetMid = targetBounds.Y + targetBounds.Height / 2;

            if (targetStart > draggedStart && draggedDeltaEnd >= targetMid)
            {
                SetTranslateTransform(target, 0, -draggedBounds.Height);
                _targetIndex = _targetIndex == -1 ? i : Math.Max(i, _targetIndex);
            }
            else if (targetStart < draggedStart && draggedDeltaStart <= targetMid)
            {
                SetTranslateTransform(target, 0, draggedBounds.Height);
                _targetIndex = _targetIndex == -1 ? i : Math.Min(i, _targetIndex);
            }
            else
            {
                SetTranslateTransform(target, 0, 0);
            }
        }

        UpdateConnectionPositionsDuringDrag();
    }

    private void ReleaseVertical()
    {
        RemoveTransforms();

        if (_draggedIndex >= 0 && _targetIndex >= 0 && _draggedIndex != _targetIndex)
        {
            if (AssociatedObject?.DataContext is SocketViewModel socketVM)
            {
                socketVM.MoveConnectionSlot(_draggedIndex, _targetIndex);
            }
        }

        // Schedule position update after layout completes
        if (_nodeView is { } nodeView)
        {
            Dispatcher.UIThread.Post(() => nodeView.UpdateSocketPosition(), DispatcherPriority.Background);
        }
    }

    private void MoveHorizontal(PointerEventArgs e)
    {
        if (_canvas == null || AssociatedObject == null) return;

        if (_tempLine == null)
        {
            _tempLine = new ConnectionLine();
            Point? socketPos = AssociatedObject.TranslatePoint(new(5, 5), _canvas);
            if (socketPos.HasValue)
            {
                if (AssociatedObject.DataContext is InputSocketViewModel)
                {
                    _tempLine.EndPoint = socketPos.Value;
                    _tempLine.StartPoint = e.GetPosition(_canvas);
                }
                else
                {
                    _tempLine.StartPoint = socketPos.Value;
                    _tempLine.EndPoint = e.GetPosition(_canvas);
                }
            }
            _canvas.Children.Insert(0, _tempLine);
        }
        else
        {
            if (AssociatedObject.DataContext is InputSocketViewModel)
            {
                _tempLine.StartPoint = e.GetPosition(_canvas);
            }
            else
            {
                _tempLine.EndPoint = e.GetPosition(_canvas);
            }
        }
    }

    private void ReleaseHorizontal(PointerReleasedEventArgs e)
    {
        if (_tempLine != null && _canvas != null)
        {
            _canvas.Children.Remove(_tempLine);
            _tempLine = null;
        }

        if (_canvas != null && AssociatedObject != null)
        {
            IInputElement? elm = _canvas.InputHitTest(e.GetPosition(_canvas));
            if (elm is SocketPoint { DataContext: SocketViewModel targetVM } targetSp
                && targetSp != AssociatedObject
                && AssociatedObject.Tag is ConnectionViewModel connVM
                && AssociatedObject.DataContext is SocketViewModel socketVM)
            {
                socketVM.DisconnectConnection(connVM);
                socketVM.TryConnect(targetVM);
            }
        }
    }

    private void AddTransforms()
    {
        if (_stackPanel == null) return;
        int connectedCount = _stackPanel.Children.Count - 1;
        for (int i = 0; i < connectedCount; i++)
        {
            if (_stackPanel.Children[i] is SocketPoint sp)
            {
                SetTranslateTransform(sp, 0, 0);
            }
        }
    }

    private void RemoveTransforms()
    {
        if (_stackPanel == null) return;
        int connectedCount = _stackPanel.Children.Count - 1;
        for (int i = 0; i < connectedCount; i++)
        {
            if (_stackPanel.Children[i] is SocketPoint sp)
            {
                sp.RenderTransform = null;
            }
        }
    }

    private void CleanUp()
    {
        if (_tempLine != null && _canvas != null)
        {
            _canvas.Children.Remove(_tempLine);
        }
        _tempLine = null;
        _enableDrag = false;
        _dragStarted = false;
        _direction = DragDirection.None;
        _stackPanel = null;
        _canvas = null;
        _nodeView = null;
    }

    private static void SetTranslateTransform(Control control, double x, double y)
    {
        var transformBuilder = new TransformOperations.Builder(1);
        transformBuilder.AppendTranslate(x, y);
        control.RenderTransform = transformBuilder.Build();
    }

    private void UpdateConnectionPositionsDuringDrag()
    {
        if (_stackPanel == null || _nodeView?.DataContext is not NodeViewModel nodeVM) return;
        bool isInput = AssociatedObject?.DataContext is InputSocketViewModel;

        int connectedCount = _stackPanel.Children.Count - 1; // exclude placeholder
        for (int i = 0; i < connectedCount; i++)
        {
            if (_stackPanel.Children[i] is SocketPoint sp && sp.Tag is ConnectionViewModel connVM)
            {
                Point? basePos = sp.TranslatePoint(new(5, 5), _nodeView);
                if (basePos.HasValue)
                {
                    Point canvasPos = basePos.Value + nodeVM.Position.Value;

                    if (isInput)
                        connVM.InputSocketPosition.Value = canvasPos;
                    else
                        connVM.OutputSocketPosition.Value = canvasPos;
                }
            }
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BEditorNext.ViewModels;

using static BEditorNext.Views.Timeline;

namespace BEditorNext.Views;

public sealed partial class LayerHeader : UserControl
{
    private MouseFlags _mouseFlag = MouseFlags.MouseUp;
    private Timeline? _timeline;
    private Point _startRel;

    public LayerHeader()
    {
        InitializeComponent();
        NameTextBox.AddHandler(KeyDownEvent, NameTextBox_KeyDown, RoutingStrategies.Tunnel);
    }

    private TimelineLayerViewModel ViewModel => (TimelineLayerViewModel)DataContext!;

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _timeline = this.FindLogicalAncestorOfType<Timeline>();
    }

    private void Border_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_timeline == null || _mouseFlag == MouseFlags.MouseUp)
            return;

        TimelineLayerViewModel vm = ViewModel;

        vm.Margin.Value = new Thickness(
            0,
            Math.Max(e.GetPosition(_timeline.TimelinePanel).Y - _startRel.Y, 0),
            0,
            0);

        e.Handled = true;
    }

    private void Border_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseFlag = MouseFlags.MouseUp;
        ViewModel.SyncModelToViewModel();
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(border);
        if (point.Properties.IsLeftButtonPressed)
        {
            _mouseFlag = MouseFlags.MouseDown;
            _startRel = point.Position;
        }
    }

    private void NameTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            Application.Current?.FocusManager?.Focus(null);
        }
    }
}

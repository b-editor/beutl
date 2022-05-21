using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;

using BeUtl.Commands;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels;

using static BeUtl.Views.Timeline;

namespace BeUtl.Views;

public sealed partial class LayerHeader : UserControl
{
    private MouseFlags _mouseFlag = MouseFlags.MouseUp;
    private Timeline? _timeline;
    private Point _startRel;
    private TimeSpan _oldStart;
    private TimeSpan _oldLength;

    public LayerHeader()
    {
        InitializeComponent();
        StartTextBox.GotFocus += StartTextBox_GotFocus;
        StartTextBox.LostFocus += StartTextBox_LostFocus;
        StartTextBox.TextInput += StartTextBox_TextInput;
        DurationTextBox.GotFocus += DurationTextBox_GotFocus;
        DurationTextBox.LostFocus += DurationTextBox_LostFocus;
        DurationTextBox.TextInput += DurationTextBox_TextInput;
    }

    private void StartTextBox_TextInput(object? sender, TextInputEventArgs e)
    {
        if (TimeSpan.TryParse(StartTextBox.Text, out TimeSpan ts))
        {
            ViewModel.Model.Start = ts;
        }
    }

    private void StartTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (TimeSpan.TryParse(StartTextBox.Text, out TimeSpan ts))
        {
            var command = new ChangePropertyCommand<TimeSpan>(
                ViewModel.Model,
                Layer.StartProperty,
                ts,
                _oldStart);

            CommandRecorder.Default.DoAndPush(command);
        }
    }

    private void StartTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        _oldStart = ViewModel.Model.Start;
    }

    private void DurationTextBox_TextInput(object? sender, TextInputEventArgs e)
    {
        if (TimeSpan.TryParse(DurationTextBox.Text, out TimeSpan ts))
        {
            ViewModel.Model.Length = ts;
        }
    }

    private void DurationTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        _oldLength = ViewModel.Model.Length;
    }

    private void DurationTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (TimeSpan.TryParse(DurationTextBox.Text, out TimeSpan ts))
        {
            var command = new ChangePropertyCommand<TimeSpan>(
                ViewModel.Model,
                Layer.LengthProperty,
                ts,
                _oldLength);

            CommandRecorder.Default.DoAndPush(command);
        }
    }

    private TimelineLayerViewModel ViewModel => (TimelineLayerViewModel)DataContext!;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TimelineLayerViewModel viewModel)
        {
            viewModel.AnimationRequested2 = async (margin) =>
            {
                var animation1 = new Avalonia.Animation.Animation
                {
                    Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.67),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame()
                        {
                            Cue = new Cue(0),
                            Setters =
                            {
                                new Setter(MarginProperty, Margin)
                            }
                        },
                        new KeyFrame()
                        {
                            Cue = new Cue(1),
                            Setters =
                            {
                                new Setter(MarginProperty, margin)
                            }
                        }
                    }
                };

                await animation1.RunAsync(this, null);
            };
        }
    }

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
}

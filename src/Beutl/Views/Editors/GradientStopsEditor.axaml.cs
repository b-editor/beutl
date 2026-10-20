using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Immutable;
using Beutl.Controls;
using Beutl.Media;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using AM = Avalonia.Media;

namespace Beutl.Views.Editors;

public sealed partial class GradientStopsEditor : UserControl
{
    private readonly GradientStopDrag _drag;

    public GradientStopsEditor()
    {
        InitializeComponent();
        _drag = new GradientStopDrag(() => slider, slider.StopDrag);

        colorPicker.FlyoutConfirmed += ColorPicker_FlyoutConfirmed;

        slider.Changed += OnSliderChanged;
        slider.Confirmed += OnSliderConfirmed;
        slider.Deleted += OnSliderDeleted;
        slider.Added += OnSliderAdded;
    }

    private void OnSliderAdded(object? sender, (int Index, AM.GradientStop Object) e)
    {
        if (DataContext is not GradientStopsEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.InsertGradientStop(e.Index, e.Object.ToBtlGradientStop());
    }

    private void OnSliderDeleted(object? sender, (int Index, AM.GradientStop Object) e)
    {
        if (DataContext is not GradientStopsEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.RemoveGradientStop(e.Index);
    }

    private void OnSliderConfirmed(
        object? sender,
        (int OldIndex, int NewIndex, AM.GradientStop Object, ImmutableGradientStop OldObject) e)
    {
        if (DataContext is GradientStopsEditorViewModel { IsElementEditable: true, Value.Value: { } list } viewModel)
            _drag.Release(viewModel, list, e.OldIndex, e.NewIndex);
    }

    private void OnSliderChanged(object? sender, (int OldIndex, int NewIndex, AM.GradientStop Object) e)
    {
        if (DataContext is GradientStopsEditorViewModel { IsElementEditable: true, Value.Value: { } list } viewModel)
            _drag.Move(viewModel, list, e.OldIndex, e.NewIndex, e.Object);
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GradientStopsEditorViewModel viewModel
            && viewModel.SelectedItem.Value is AM.GradientStop astop)
        {
            int index = viewModel.Stops.Value.IndexOf(astop);
            viewModel.RemoveGradientStop(index);
        }
    }

    private void ColorPicker_FlyoutConfirmed(ColorPickerButton sender, ColorButtonColorChangedEventArgs args)
    {
        if (DataContext is GradientStopsEditorViewModel { IsElementEditable: true } viewModel
            && viewModel.SelectedItem.Value is AM.GradientStop astop
            && args.NewColor.HasValue
            && args.OldColor.HasValue)
        {
            int index = viewModel.Stops.Value.IndexOf(astop);
            var bstop = viewModel.Value.Value[index];
            bstop.Color.CurrentValue = args.NewColor.Value.ToBtlColor();
            viewModel.ConfirmeGradientStop(index, index);
        }
    }
}

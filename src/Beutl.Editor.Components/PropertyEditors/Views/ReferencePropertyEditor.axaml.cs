using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.PropertyEditors.ViewModels;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class ReferencePropertyEditor : UserControl
{
    public ReferencePropertyEditor()
    {
        InitializeComponent();
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IReferencePropertyEditorViewModel { IsDisposed: false, CanEditProperty.Value: true } vm) return;

        await TargetSelectionHelper.HandleSelectTargetRequestAsync<IReferencePropertyEditorViewModel, CoreObject>(
            this, vm, viewModel => viewModel.GetAvailableTargets(), (viewModel, target) => viewModel.SetTarget(target));
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IReferencePropertyEditorViewModel { IsDisposed: false } vm)
            vm.SetTarget(null);
    }
}

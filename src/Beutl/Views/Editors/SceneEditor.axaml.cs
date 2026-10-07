using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ProjectSystem;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class SceneEditor : UserControl
{
    private bool _flyoutOpen;

    public SceneEditor()
    {
        InitializeComponent();
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SceneEditorViewModel { IsDisposed: false } vm) return;
        if (_flyoutOpen) return;

        try
        {
            _flyoutOpen = true;
            await TargetSelectionHelper.HandleSelectTargetRequestAsync<SceneEditorViewModel, Scene>(
                this,
                vm,
                viewModel => viewModel.GetAvailableScenes(),
                (viewModel, target) => viewModel.SetTarget(target));
        }
        finally
        {
            _flyoutOpen = false;
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SceneEditorViewModel { IsDisposed: false } vm) return;
        vm.SetNull();
    }
}

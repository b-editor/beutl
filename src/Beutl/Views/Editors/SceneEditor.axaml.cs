using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ProjectSystem;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class SceneEditor : UserControl
{
    public SceneEditor()
    {
        InitializeComponent();
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SceneEditorViewModel { IsDisposed: false } vm) return;

        await TargetSelectionHelper.HandleSelectTargetRequestAsync<SceneEditorViewModel, Scene>(
            this,
            vm,
            viewModel => viewModel.GetAvailableScenes(),
            (viewModel, target) => viewModel.SetTarget(target));
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SceneEditorViewModel { IsDisposed: false } vm) return;
        vm.SetNull();
    }
}

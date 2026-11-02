using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.PropertyEditors.ViewModels;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class SceneEditor : UserControl
{
    public SceneEditor()
    {
        InitializeComponent();
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SceneEditorViewModel { IsDisposed: false } vm) return;

        await TargetSelectionHelper.PickTargetAsync(this, sender as Control, vm);
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SceneEditorViewModel { IsDisposed: false } vm) return;
        vm.SetNull();
    }
}

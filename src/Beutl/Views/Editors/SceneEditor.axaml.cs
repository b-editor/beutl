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
            var targets = vm.GetAvailableScenes();
            var pickerVm = new TargetPickerFlyoutViewModel();
            pickerVm.Initialize(targets);

            var flyout = new TargetPickerFlyout(pickerVm);
            flyout.ShowAt(this);

            var tcs = new TaskCompletionSource<Scene?>();
            flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
            flyout.Confirmed += (_, _) => tcs.TrySetResult(
                (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object as Scene);

            var result = await tcs.Task;
            if (result != null)
            {
                vm.SetTarget(result);
            }
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

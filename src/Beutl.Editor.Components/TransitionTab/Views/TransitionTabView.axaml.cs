using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Components.TransitionTab.ViewModels;
using Beutl.Graphics.Transitions;
using Beutl.Services;

namespace Beutl.Editor.Components.TransitionTab.Views;

public partial class TransitionTabView : UserControl
{
    public TransitionTabView()
    {
        InitializeComponent();
    }

    private async void OnTypeDropDownClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TransitionTabViewModel viewModel) return;

        await PickerGuard.RunAsync(this, async () =>
        {
            try
            {
                var selectVm = new SelectLibraryItemDialogViewModel(KnownLibraryItemFormats.ClipTransition, typeof(ClipTransition));
                await selectVm.SelectTypeAsync(viewModel.Transition.Value?.GetType());
                Type? type = await LibraryItemPickerHelper.ShowAsync(
                    typeEditor,
                    selectVm,
                    KnownLibraryItemFormats.ClipTransition) as Type;
                if (type != null)
                {
                    viewModel.ChangeType(type);
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError(Strings.Error, ex.Message);
            }
        });
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as TransitionTabViewModel)?.ChangeType(null);
    }
}

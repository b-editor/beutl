using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
using Beutl.Views.Editors;

namespace Beutl.Views.Tools;

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
                Type? type = await LibraryItemPickerHelper.ShowAsync(
                    typeEditor, new SelectClipTransitionTypeViewModel(), KnownLibraryItemFormats.ClipTransition) as Type;
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

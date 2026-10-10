using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
using Beutl.Views.Editors;

namespace Beutl.Views.Tools;

public partial class TransitionTabView : UserControl
{
    private bool _flyoutOpen;

    public TransitionTabView()
    {
        InitializeComponent();
    }

    // Picks the new type from the library, as an effect's is picked.
    private async void ChangeTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TransitionTabViewModel viewModel || _flyoutOpen) return;

        try
        {
            _flyoutOpen = true;
            Type? type = await LibraryItemPickerHelper.ShowTypeOnlyAsync(
                changeButton, new SelectClipTransitionTypeViewModel(), KnownLibraryItemFormats.ClipTransition);
            if (type != null)
            {
                viewModel.ChangeType(type);
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
        finally
        {
            _flyoutOpen = false;
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as TransitionTabViewModel)?.ChangeType(null);
    }
}

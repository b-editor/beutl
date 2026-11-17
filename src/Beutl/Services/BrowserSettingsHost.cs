using Avalonia.Controls;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Pages;
using Beutl.ViewModels;

namespace Beutl.Services;

internal sealed class BrowserSettingsHost(Func<SettingsDialogViewModel> createDialog) : IBrowserSettingsHost
{
    private SettingsDialog? _dialog;
    private SettingsDialogViewModel? _viewModel;

    public async Task OpenBrowserSettingsAsync(Window owner)
    {
        if (_dialog != null)
        {
            _viewModel!.GoToBrowserSettingsPage();
            _dialog.Activate();
            return;
        }

        using var vm = createDialog();
        var dialog = new SettingsDialog { DataContext = vm };
        ColorPaletteService.SetService(dialog, ColorPaletteService.GetService(owner));
        ControlConfiguration.SetConfiguration(dialog, ControlConfiguration.GetConfiguration(owner));
        ControlFonts.SetFonts(dialog, ControlFonts.GetFonts(owner));
        _viewModel = vm;
        _dialog = dialog;
        try
        {
            vm.GoToBrowserSettingsPage();
            await dialog.ShowDialog(owner);
        }
        finally
        {
            _dialog = null;
            _viewModel = null;
        }
    }
}

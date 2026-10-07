using Avalonia.Controls;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed partial class EditViewModel
{
    private bool _checkedMissingMedia;
    private MissingMediaDialog? _missingMediaDialog;

    // In-place URI repairs do not produce property-history entries. Retain this
    // flag if saving fails, until an explicit save succeeds.
    public ReactivePropertySlim<bool> HasMediaRepairs { get; } = new();

    internal CancellationToken MediaRepairCancellationToken => _autoSaveCancellation.Token;

    internal async Task ShowMissingMediaAsync(Window owner, bool onlyOnFirstOpen = false)
    {
        if (_disposed || _missingMediaDialog != null || (onlyOnFirstOpen && _checkedMissingMedia)) return;
        _checkedMissingMedia = true;
        using var vm = new MissingMediaDialogViewModel(this);
        if (vm.Rows.Count == 0) return;
        var dialog = new MissingMediaDialog { DataContext = vm };
        _missingMediaDialog = dialog;
        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            _missingMediaDialog = null;
        }
    }
}

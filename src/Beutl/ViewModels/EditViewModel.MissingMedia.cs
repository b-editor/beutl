using Avalonia.Controls;
using Beutl.Editor;
using Beutl.Media.Source;
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
        if (!onlyOnFirstOpen)
        {
            using var suspension = EditorService.SuspendEditor(this);
            await Player.Pause();
            if (_disposed) return;
            foreach (MediaSource source in new Beutl.Editor.Services.ObjectSearcher(Scene,
                         value => value is MediaSource { HasUri: true }).SearchAll().OfType<MediaSource>())
                if (source.Uri.IsFile && File.Exists(source.Uri.LocalPath)) source.InvalidateResourceCache();
            Renderer.Value.ClearAllCaches();
            FrameCacheManager.Value.Clear();
            Player.QueuePreviewRender();
        }
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

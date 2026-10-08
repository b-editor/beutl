using Avalonia.Controls;
using Beutl.Editor;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using Microsoft.Extensions.Logging;
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
            foreach (IFileSource source in new Beutl.Editor.Services.ObjectSearcher(Scene,
                         value => value is MediaSource { HasUri: true } or ModelSource { HasUri: true, MeshCount: 0 })
                         .SearchAll().OfType<IFileSource>())
            {
                if (!source.Uri.IsFile || !File.Exists(source.Uri.LocalPath)) continue;
                if (source is MediaSource media) media.InvalidateResourceCache();
                else if (source is ModelSource)
                {
                    try
                    {
                        Uri uri = source.Uri;
                        var loaded = await Task.Run(() =>
                        {
                            var model = new ModelSource(); model.ReadFrom(uri); return model;
                        }, MediaRepairCancellationToken);
                        if (_disposed) return;
                        using (HistoryManager.SuppressRecording())
                            ResourceRelocationService.RelinkFileSource(source, uri, loaded);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                    {
                        _logger.LogWarning(ex, "Could not reload restored model {Uri}.", source.Uri);
                    }
                }
            }
            ScheduleMediaFingerprints();
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

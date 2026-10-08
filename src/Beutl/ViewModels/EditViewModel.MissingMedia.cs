using Beutl.Editor;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.Services;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed partial class EditViewModel
{
    private CancellationTokenSource? _missingMediaNotification;
    private bool _openingMissingMedia;
    private Task _missingMediaScanTask = Task.CompletedTask;
    private bool _missingMediaScanRequested;

    // In-place URI repairs do not produce property-history entries. Retain this
    // flag if saving fails, until an explicit save succeeds.
    public ReactivePropertySlim<bool> HasMediaRepairs { get; } = new();

    internal CancellationToken MediaRepairCancellationToken => _autoSaveCancellation.Token;

    internal void NotifyMissingMedia()
    {
        if (_disposed) return;
        _missingMediaScanRequested = true;
        if (_missingMediaScanTask.IsCompleted) _missingMediaScanTask = RecheckMissingMediaAsync();
    }

    internal Task WaitForMissingMediaAsync() => _missingMediaScanTask;

    private async Task RecheckMissingMediaAsync()
    {
        await Task.Yield();
        try
        {
            do
            {
                _missingMediaScanRequested = false;
                await Task.Delay(200, MediaRepairCancellationToken);
                if (_missingMediaScanRequested) continue;
                int count = (await new MissingMediaService().FindMissingAsync(Scene, MediaRepairCancellationToken)).Count;
                if (_disposed) return;
                if (_missingMediaScanRequested) continue;
                if (count == 0) DismissMissingMediaNotification();
                else if (_missingMediaNotification == null) ShowMissingMediaNotification(count);
            }
            while (_missingMediaScanRequested);
        }
        catch (OperationCanceledException) when (MediaRepairCancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not check missing media."); }
    }

    private void ShowMissingMediaNotification(int count)
    {
        _missingMediaNotification = CancellationTokenSource.CreateLinkedTokenSource(MediaRepairCancellationToken);
        NotificationService.Show(new Beutl.Services.Notification(
            MissingMediaStrings.Title,
            string.Format(MissingMediaStrings.NotificationMessage, Scene.Name, count),
            NotificationType.Warning,
            Expiration: Timeout.InfiniteTimeSpan,
            OnClose: DismissMissingMediaNotification,
            Actions: [new(MissingMediaStrings.OpenRepairTool, () => _ = OpenMissingMediaAsync(), DismissOnInvoke: false)])
        {
            CancellationToken = _missingMediaNotification.Token
        });
    }

    internal void DismissMissingMediaNotification()
    {
        _missingMediaNotification?.Cancel();
        _missingMediaNotification?.Dispose();
        _missingMediaNotification = null;
    }

    internal async Task OpenMissingMediaAsync(bool refresh = false)
    {
        if (_disposed || _openingMissingMedia) return;
        var existing = FindToolTab<MissingMediaViewModel>();
        if (existing != null && !refresh)
        {
            EditorService.ActivateTabItem(Scene);
            OpenToolTab(existing);
            return;
        }
        if (existing?.IsBusy.Value == true) return;
        CancellationToken cancellationToken = existing?.CancellationToken ?? MediaRepairCancellationToken;
        _openingMissingMedia = true;
        existing?.SetBusy(true);
        try
        {
            using var suspension = EditorService.SuspendEditor(this);
            await Player.Pause();
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;
            foreach (IFileSource source in new Beutl.Editor.Services.ObjectSearcher(Scene,
                         value => value is MediaSource { HasUri: true } or ModelSource { HasUri: true, MeshCount: 0 })
                         .SearchAll().OfType<IFileSource>())
            {
                if (!source.Uri.IsFile) continue;
                if (source is MediaSource media) media.InvalidateResourceCache();
                else if (source is ModelSource)
                {
                    try
                    {
                        Uri uri = source.Uri;
                        var loaded = await Task.Run(() =>
                        {
                            var model = new ModelSource(); model.ReadFrom(uri); return model;
                        }, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
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

            var missing = await new MissingMediaService().FindMissingAsync(Scene, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (existing != null) existing.RefreshRows(missing);
            else
            {
                existing = new MissingMediaViewModel(this, missing);
                if (!DockHost.OpenToolTab(existing, DockHost.Factory.GetAnchoredDock(existing.Extension.DefaultAnchor)))
                {
                    existing.Dispose();
                    return;
                }
            }
            EditorService.ActivateTabItem(Scene);
            OpenToolTab(existing);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _openingMissingMedia = false;
            existing?.SetBusy(false);
        }
    }
}

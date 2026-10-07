using System.Text.Json;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public sealed partial class EditViewModel
{
    private bool _fingerprintScanRequested;
    private Task _fingerprintScanTask = Task.CompletedTask;
    private HashSet<string> _savedMediaUris = new(StringComparer.Ordinal);
    internal Func<Scene, CancellationToken, Task> CaptureMediaFingerprints { get; set; }
        = static (scene, token) => new MissingMediaService().UpdateFingerprintsAsync(scene, token);

    internal void ScheduleMediaFingerprints()
    {
        if (_disposed) return;
        _fingerprintScanRequested = true;
        if (_fingerprintScanTask.IsCompleted)
            _fingerprintScanTask = UpdateMediaFingerprintsInBackgroundAsync();
    }

    internal Task WaitForMediaFingerprintsAsync() => _fingerprintScanTask;

    private void CaptureSavedMediaUris()
        => _savedMediaUris = MissingMediaService.GetFileUris(Scene).Select(uri => uri.AbsoluteUri).ToHashSet(StringComparer.Ordinal);

    private async Task UpdateMediaFingerprintsInBackgroundAsync()
    {
        // Begin outside the SaveAsync call stack and its storage transaction.
        await Task.Yield();
        try
        {
            do
            {
                _fingerprintScanRequested = false;
                Scene scene = Scene;
                Uri[] before = MissingMediaService.GetFileUris(scene);
                await CaptureMediaFingerprints(scene, _autoSaveCancellation.Token);
                if (_disposed) return;
                Uri[] after = MissingMediaService.GetFileUris(scene);
                if (!before.ToHashSet().SetEquals(after)) _fingerprintScanRequested = true;

                using var write = await EditorService.BeginProjectFileWriteAsync(_autoSaveCancellation.Token);
                if (_disposed || EditorService.IsWorktreeMutationActive || scene.Uri is not { IsFile: true } uri) return;
                var saved = scene.MediaFingerprints.Where(pair => _savedMediaUris.Contains(pair.Key))
                    .ToDictionary(pair => UriHelper.ToSerializedUri(new Uri(pair.Key), uri).ToString(), pair => pair.Value);
                // Patch advisory metadata only; never serialize unsaved scene or element edits.
                CoreSerializer.UpdateStoredMetadata(uri, scene.Id, nameof(Scene.MediaFingerprints), JsonSerializer.SerializeToNode(saved)!);
            }
            while (_fingerprintScanRequested);
        }
        catch (OperationCanceledException) when (_autoSaveCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update background media fingerprints.");
        }
    }
}

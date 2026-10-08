using System.Text.Json;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public sealed partial class EditViewModel
{
    private bool _fingerprintScanRequested;
    private bool _forceFingerprintScanRequested;
    private bool _fingerprintsFlushedForSave;
    private Task _fingerprintScanTask = Task.CompletedTask;
    private Task _fingerprintCaptureTask = Task.CompletedTask;
    private bool _finishFingerprintSaveSnapshot;
    private HashSet<string> _savedMediaUris = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MediaFileFingerprint> _savedMediaFingerprints = new(StringComparer.Ordinal);
    internal Func<Scene, CancellationToken, Task>? CaptureMediaFingerprints { get; set; }

    internal void ScheduleMediaFingerprints(bool finishSaveSnapshot = false, bool force = false)
    {
        if (_disposed) return;
        // Ordinary scans cannot request a Git snapshot or cancel a pending explicit save.
        _finishFingerprintSaveSnapshot |= finishSaveSnapshot;
        _forceFingerprintScanRequested |= force;
        _fingerprintScanRequested = true;
        if (_fingerprintScanTask.IsCompleted)
            _fingerprintScanTask = UpdateMediaFingerprintsInBackgroundAsync();
    }

    internal Task WaitForMediaFingerprintsAsync() => _fingerprintScanTask;

    private Task CaptureMediaFingerprintsAsync(Scene scene, bool force)
    {
        if (_fingerprintCaptureTask.IsCompleted)
            _fingerprintCaptureTask = CaptureMediaFingerprints?.Invoke(scene, _autoSaveCancellation.Token)
                ?? new MissingMediaService().UpdateFingerprintsAsync(scene, _autoSaveCancellation.Token, force);
        return _fingerprintCaptureTask;
    }

    internal async Task FlushMediaFingerprintsAsync()
    {
        // Lifecycle saves reserve the workspace. Wait only for hashing, never
        // for the background metadata writer queued behind that reservation.
        try
        {
            if (!_fingerprintCaptureTask.IsCompleted) await _fingerprintCaptureTask;
            await CaptureMediaFingerprintsAsync(Scene, force: true);
            _forceFingerprintScanRequested = false;
            _fingerprintsFlushedForSave = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not flush media fingerprints.");
        }
    }

    private void CaptureSavedMediaUris()
    {
        _savedMediaUris = MissingMediaService.GetFileUris(Scene).Select(uri => uri.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
        foreach (string key in _savedMediaFingerprints.Keys.Where(key => !_savedMediaUris.Contains(key)).ToArray())
            _savedMediaFingerprints.Remove(key);
        MergeSavedMediaFingerprints(Scene);
    }

    private void MergeSavedMediaFingerprints(Scene scene)
    {
        // Live scans may prune references removed by an unsaved edit. Keep the
        // saved graph's fingerprints until that graph change reaches disk.
        foreach (var pair in scene.MediaFingerprints.Where(pair => _savedMediaUris.Contains(pair.Key)))
            _savedMediaFingerprints[pair.Key] = pair.Value;
    }

    private async Task UpdateMediaFingerprintsInBackgroundAsync()
    {
        // Begin outside the SaveAsync call stack and its storage transaction.
        await Task.Yield();
        try
        {
            do
            {
                if (_disposed) return;
                _fingerprintScanRequested = false;
                bool force = _forceFingerprintScanRequested;
                _forceFingerprintScanRequested = false;
                Scene scene = Scene;
                Uri[] before = MissingMediaService.GetFileUris(scene);
                await CaptureMediaFingerprintsAsync(scene, force);
                if (_disposed) return;
                Uri[] after = MissingMediaService.GetFileUris(scene);
                if (!before.ToHashSet().SetEquals(after)) _fingerprintScanRequested = true;

                using var write = await EditorService.BeginProjectFileWriteAsync(_autoSaveCancellation.Token);
                if (_disposed || EditorService.IsWorktreeMutationActive || scene.Uri is not { IsFile: true } uri) return;
                // A save or edit can request another scan while hashing or waiting
                // for admission. Keep its save intent until the final stable scan.
                if (_fingerprintScanRequested) continue;
                MergeSavedMediaFingerprints(scene);
                var saved = _savedMediaFingerprints
                    .ToDictionary(pair => UriHelper.ToSerializedUri(new Uri(pair.Key), uri).ToString(), pair => pair.Value);
                // Patch advisory metadata only; never serialize unsaved scene or element edits.
                bool changed = CoreSerializer.UpdateStoredMetadata(uri, scene.Id, nameof(Scene.MediaFingerprints),
                    saved.Count == 0 ? null : JsonSerializer.SerializeToNode(saved));
                bool finishSaveSnapshot = _finishFingerprintSaveSnapshot;
                _finishFingerprintSaveSnapshot = false;
                if (changed && finishSaveSnapshot && EditorService.ProjectVersionControlSession is { } session)
                    await session.NotifySavedAsync(write, _autoSaveCancellation.Token);
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

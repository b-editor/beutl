using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Services;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

public sealed partial class ProxiesTabViewModel
{
    private void ReportActionStatus(string message, NotificationType type)
    {
        StatusMessage.Value = message;
        Notify(new Notification(Strings.Proxies, message, type));
    }

    public async Task GenerateAsync(ProxyClipViewModel clip)
    {
        if (_queue == null)
        {
            ReportActionStatus(Strings.ProxyQueueUnavailable, NotificationType.Warning);
            return;
        }

        await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, ForegroundGenerationPriority);
        RefreshJobs();
    }

    public async Task RegenerateAsync(ProxyClipViewModel clip)
    {
        await GenerateAsync(clip);
    }

    public void Delete(ProxyClipViewModel clip)
    {
        CancelMatchingJobs(clip);
        int failed = TryDeleteEntry(clip.EntrySource ?? clip.Source, clip.Preset.Value) ? 0 : 1;
        ReportDeleteFailures(failed);
        Refresh();
    }

    // Two shapes of a real failure are surfaced, both typically a sharing violation while the preview
    // decodes that proxy: Delete returns false with the entry still present, or Delete returns true (index
    // removed) yet its best-effort file delete left the .mp4 on disk. Delete returning false for an
    // already-gone entry is a benign race (eviction / another instance), not surfaced.
    private bool TryDeleteEntry(ProxyFingerprint source, ProxyPreset preset)
    {
        if (_store is not { } store)
            return true;

        string? proxyPath = ResolveProxyFilePath(store, source, preset);

        if (store.Delete(source, preset))
            return proxyPath is null || !File.Exists(proxyPath);

        return store.TryGet(source, preset) is null;
    }

    // The proxy file's absolute path from its store entry, captured before Delete removes the entry so a
    // surviving orphan can be detected afterward. Null when no entry exists to resolve.
    private static string? ResolveProxyFilePath(IProxyStore store, ProxyFingerprint source, ProxyPreset preset)
    {
        if (store.TryGet(source, preset) is not { } entry)
            return null;

        return Path.Combine(store.StoreRootPath, entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar));
    }

    private void ReportDeleteFailures(int failed)
    {
        if (failed <= 0)
            return;

        string message = failed == 1
            ? Strings.ProxyDeleteFailedSingular
            : string.Format(CultureInfo.CurrentCulture, Strings.ProxyDeleteFailedPluralFormat, failed);
        ReportActionStatus(message, NotificationType.Error);
    }

    // A queued/running generation would Register the proxy again on success and silently undo the
    // delete; cancel it first. A stale row's job may be keyed on the old EntrySource fingerprint while
    // the current Source differs (the media file changed), so cancel both.
    private void CancelMatchingJobs(ProxyClipViewModel clip)
    {
        CancelMatchingJobs(clip.Source, clip.Preset.Value);
        if (clip.EntrySource is { } entrySource && entrySource != clip.Source)
            CancelMatchingJobs(entrySource, clip.Preset.Value);
    }

    private void CancelMatchingJobs(ProxyFingerprint source, ProxyPreset preset)
    {
        if (_queue == null)
            return;

        foreach (ProxyJob job in _queue.Pending())
        {
            if (job.Source == source && job.Preset == preset)
                _queue.Cancel(job.JobId);
        }
    }

    public void CancelJob(Guid jobId)
    {
        _queue?.Cancel(jobId);
        RefreshJobs();
    }

    internal void CancelJob(ProxyClipViewModel clip)
    {
        if (clip.JobId is not { } jobId)
            return;

        CancelJob(jobId);
    }

    internal void RefreshClip(ProxyClipViewModel clip)
    {
        clip.UpdateEntry(FindEntry(clip.Source, clip.Preset.Value));
        RefreshJobs();
        UpdateClipSummary();
    }

    internal void OnPresetChanged(ProxyClipViewModel clip, ProxyPreset oldPreset, ProxyPreset newPreset)
    {
        // Moving the dropdown off a preset whose job is still Queued/Running would otherwise
        // orphan that job: it keeps the sole serial slot and produces a proxy for a preset the
        // user no longer selected. Each source maps to a single row here, so cancelling the job
        // for (this source, old preset) cannot affect another visible clip.
        if (oldPreset != newPreset)
            CancelJobForSourcePreset(clip.Source, oldPreset);

        RefreshClip(clip);
    }

    private void CancelJobForSourcePreset(ProxyFingerprint source, ProxyPreset preset)
    {
        if (_queue == null)
            return;

        foreach (ProxyJob job in _queue.Pending())
        {
            if (job.Preset == preset && job.Source.Equals(source))
                _queue.Cancel(job.JobId);
        }
    }

    private async Task GenerateAllAsync()
    {
        if (_queue == null)
        {
            ReportActionStatus(Strings.ProxyQueueUnavailable, NotificationType.Warning);
            return;
        }

        ProxyClipViewModel[] eligible = [.. Clips.Where(IsEligibleForBulkGeneration)];
        if (eligible.Length == 0)
        {
            // An all-light project would otherwise no-op silently on an explicit action; tell the
            // user nothing met the heaviness floor and point them at per-clip generate.
            if (Clips.Count > 0)
                ReportActionStatus(Strings.ProxyBulkNoEligibleClips, NotificationType.Information);

            return;
        }

        foreach (ProxyClipViewModel clip in eligible)
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, BulkGenerationPriority);
        }

        RefreshJobs();
    }

    private bool IsEligibleForBulkGeneration(ProxyClipViewModel clip)
    {
        if (TryGetSourcePixelCount(clip.Source) is { } pixelCount)
            return pixelCount >= MinBulkSourcePixelCount;

        return clip.Source.FileSizeBytes >= MinBulkSourceFileBytes;
    }

    private long? TryGetSourcePixelCount(ProxyFingerprint source)
    {
        if (_store == null)
            return null;

        foreach (ProxyEntry entry in _store.Enumerate())
        {
            // Match on path + size, not the full fingerprint: an in-place replace changes the size,
            // so old dimensions can't drive eligibility for a heavier file; a benign mtime-only
            // touch keeps the size, so valid dimensions are still used (rather than falling back to
            // the coarse file-size floor). Only same-path/same-size entries describe this file.
            if (entry.Source.AbsolutePath != source.AbsolutePath
                || entry.Source.FileSizeBytes != source.FileSizeBytes)
            {
                continue;
            }

            PixelSize frameSize = entry.OriginalLogicalFrameSize;
            if (frameSize.Width > 0 && frameSize.Height > 0)
                return (long)frameSize.Width * frameSize.Height;
        }

        return null;
    }

    private async Task GenerateSelectedAsync()
    {
        if (_queue == null)
        {
            ReportActionStatus(Strings.ProxyQueueUnavailable, NotificationType.Warning);
            return;
        }

        foreach (ProxyClipViewModel clip in Clips.Where(static c => c.IsSelected.Value).ToArray())
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, ForegroundGenerationPriority);
        }

        RefreshJobs();
    }

    private async Task RegenerateSelectedAsync()
    {
        if (_queue == null)
        {
            ReportActionStatus(Strings.ProxyQueueUnavailable, NotificationType.Warning);
            return;
        }

        foreach (ProxyClipViewModel clip in Clips.Where(static c => c.IsSelected.Value).ToArray())
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, ForegroundGenerationPriority);
        }

        Refresh();
    }

    private void DeleteSelected()
    {
        int failed = 0;
        foreach (ProxyClipViewModel clip in Clips.Where(static c => c.IsSelected.Value).ToArray())
        {
            CancelMatchingJobs(clip);
            if (!TryDeleteEntry(clip.EntrySource ?? clip.Source, clip.Preset.Value))
                failed++;
        }

        ReportDeleteFailures(failed);
        Refresh();
    }

    private async Task DeleteAllForProjectAsync()
    {
        if (_store == null)
            return;

        (ProxyEntry[] entries, ProxyJob[] projectJobs) = CollectProjectProxies();
        if (entries.Length == 0 && projectJobs.Length == 0)
            return;

        if (!await ConfirmDeleteAllForProjectAsync(entries.Length))
            return;

        // Re-snapshot after the dialog: a project job can finish while it is open and register a new
        // proxy the pre-dialog snapshot would miss, leaving it behind after the user confirms.
        (entries, projectJobs) = CollectProjectProxies();

        foreach (ProxyJob job in projectJobs)
        {
            _queue?.Cancel(job.JobId);
        }

        int failed = 0;
        foreach (ProxyEntry entry in entries)
        {
            if (!TryDeleteEntry(entry.Source, entry.Preset))
                failed++;
        }

        ReportDeleteFailures(failed);
        Refresh();
    }

    private (ProxyEntry[] Entries, ProxyJob[] Jobs) CollectProjectProxies()
    {
        HashSet<string> projectPaths = [.. Clips.Select(static c => c.Source.AbsolutePath)];
        ProxyEntry[] entries = _store == null
            ? []
            : [.. _store.Enumerate().Where(entry => projectPaths.Contains(entry.Source.AbsolutePath))];
        ProxyJob[] jobs = _queue == null
            ? []
            : [.. _queue.Pending().Where(job => projectPaths.Contains(job.Source.AbsolutePath))];
        return (entries, jobs);
    }

    private static async Task<bool> ShowDeleteAllForProjectConfirmationAsync(int entryCount)
    {
        var dialog = new FAContentDialog
        {
            Title = Strings.ProxyDeleteProjectProxies,
            Content = string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyDeleteProjectProxiesConfirmationFormat,
                entryCount),
            PrimaryButtonText = Strings.Yes,
            CloseButtonText = Strings.No,
            DefaultButton = FAContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == FAContentDialogResult.Primary;
    }
}

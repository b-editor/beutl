using System.Diagnostics.CodeAnalysis;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Services;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

public sealed partial class ProxiesTabViewModel
{
    private void ReportActionStatus(string message, NotificationType type)
    {
        Notify(new Notification(Strings.Proxies, message, type));
    }

    [MemberNotNullWhen(true, nameof(_queue))]
    private bool EnsureQueueAvailable()
    {
        if (_queue != null)
            return true;

        ReportActionStatus(Strings.ProxyQueueUnavailable, NotificationType.Warning);
        return false;
    }

    public async Task GenerateAsync(ProxyClipViewModel clip)
    {
        if (!EnsureQueueAvailable())
            return;

        await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, ForegroundGenerationPriority);
        RefreshJobs();
    }

    public void Delete(ProxyClipViewModel clip)
    {
        int failed = DeleteClipProxy(clip) ? 0 : 1;
        ReportDeleteFailures(failed);
        Refresh();
    }

    private bool DeleteClipProxy(ProxyClipViewModel clip)
    {
        CancelMatchingJobs(clip);
        return TryDeleteEntry(clip.EntrySource ?? clip.Source, clip.Preset.Value);
    }

    // Delete reports whether the store removed the proxy entry, not whether its .mp4 could be unlinked in
    // the same call. A preview reader pins the file, so the store deliberately retires it and reclaims it
    // when the last pin is released; checking File.Exists here turned that normal deferred cleanup into an
    // "in use" failure even though the entry was already gone, and the second click then "succeeded".
    // Delete returning false for an already-gone entry is a benign race (eviction / another instance).
    private bool TryDeleteEntry(ProxyFingerprint source, ProxyPreset preset)
    {
        if (_store is not { } store)
            return true;

        return store.Delete(source, preset) || store.TryGet(source, preset) is null;
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

    private void CancelJob(Guid jobId)
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
        // A job still queued or running for the old preset would hold the serial slot for a preset no
        // longer selected; each source has one row, so cancelling it affects no other clip.
        if (oldPreset != newPreset)
            CancelMatchingJobs(clip.Source, oldPreset);

        RefreshClip(clip);
    }

    private async Task GenerateAllAsync()
    {
        if (!EnsureQueueAvailable())
            return;

        ProxyClipViewModel[] clips = [.. Clips];
        bool[] eligibleResults = await Task.WhenAll(clips.Select(IsEligibleForBulkGenerationAsync));
        if (_isDisposed)
            return;

        ProxyClipViewModel[] eligible = [.. clips.Where((_, index) => eligibleResults[index])];
        if (eligible.Length == 0)
        {
            // An all-light project would otherwise no-op silently on an explicit action; tell the
            // user nothing met the heaviness floor and point them at per-clip generate.
            if (clips.Length > 0)
                ReportActionStatus(Strings.ProxyBulkNoEligibleClips, NotificationType.Information);

            return;
        }

        foreach (ProxyClipViewModel clip in eligible)
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, BulkGenerationPriority);
        }

        RefreshJobs();
    }

    private async Task<bool> IsEligibleForBulkGenerationAsync(ProxyClipViewModel clip)
    {
        if (TryGetSourcePixelCount(clip.Source) is { } pixelCount)
            return pixelCount >= MinBulkSourcePixelCount;

        if (clip.Source.FileSizeBytes >= MinBulkSourceFileBytes)
            return true;

        // A never-proxied clip has no stored dimensions, so the size floor alone would misclassify a
        // short high-resolution clip as light. Probe the source before deciding.
        if (await ProbeSourceFrameSizeAsync(clip.Source.SourcePath).ConfigureAwait(false) is { } frameSize)
            return (long)frameSize.Width * frameSize.Height >= MinBulkSourcePixelCount;

        return false;
    }

    // Test seam: the unit-test environment has no decoders for the synthetic source files, so tests
    // that exercise the eligibility decision inject a frame size instead of opening the media.
    internal Func<string, Task<PixelSize?>> ProbeSourceFrameSizeAsync { get; set; }
        = ProbeSourceFrameSizeFromMetadataAsync;

    private static async Task<PixelSize?> ProbeSourceFrameSizeFromMetadataAsync(string path)
    {
        FileThumbnailService service = FileThumbnailService.Instance;
        if (!service.CanGetMediaInfo(path))
            return null;

        MediaFileInfo? info = await service.GetMediaInfoAsync(path).ConfigureAwait(false);
        return info is { Width: { } width, Height: { } height } && width > 0 && height > 0
            ? new PixelSize(width, height)
            : null;
    }

    private long? TryGetSourcePixelCount(ProxyFingerprint source)
    {
        if (_store == null)
            return null;

        foreach (ProxyEntry entry in _store.Enumerate())
        {
            // Match path and size: a replaced file changes size, so its old dimensions must not decide
            // eligibility, while an mtime-only touch keeps them.
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

    private ProxyClipViewModel[] SelectedClips() => Clips.Where(static c => c.IsSelected.Value).ToArray();

    private async Task GenerateSelectedAsync(bool refreshClips)
    {
        if (!EnsureQueueAvailable())
            return;

        foreach (ProxyClipViewModel clip in SelectedClips())
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, ForegroundGenerationPriority);
        }

        if (refreshClips)
            Refresh();
        else
            RefreshJobs();
    }

    private void DeleteSelected()
    {
        int failed = 0;
        foreach (ProxyClipViewModel clip in SelectedClips())
        {
            if (!DeleteClipProxy(clip))
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

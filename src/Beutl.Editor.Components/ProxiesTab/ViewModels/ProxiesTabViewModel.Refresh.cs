using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

public sealed partial class ProxiesTabViewModel
{
    private void OnSceneEdited(object? sender, EventArgs e) => ScheduleRefresh();

    private void OnProjectItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSceneSubscriptions();
        ScheduleRefresh();
    }

    private void RefreshSceneSubscriptions()
    {
        var subscriptions = new CompositeDisposable();
        foreach (Scene scene in EnumerateProjectScenes())
        {
            Scene captured = scene;
            captured.Edited += OnSceneEdited;
            subscriptions.Add(Disposable.Create(() => captured.Edited -= OnSceneEdited));
        }

        _sceneEditedSubscriptions.Disposable = subscriptions;
    }

    private void OnProxyConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is CorePropertyChangedEventArgs<int> presetArgs
            && presetArgs.Property == ProxyStoreConfig.DefaultPresetProperty)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                Dispatcher.UIThread.Post(() => ApplyDefaultPresetChange(presetArgs.OldValue, presetArgs.NewValue));
            else
                ApplyDefaultPresetChange(presetArgs.OldValue, presetArgs.NewValue);

            return;
        }

        // A cap-only change shifts the store summary (StoreCapText / usage), which no other signal on
        // this tab reflects until an unrelated store / job / scene refresh; refresh it here.
        if (e is CorePropertyChangedEventArgs<long> capArgs
            && capArgs.Property == ProxyStoreConfig.MaxTotalBytesProperty)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                Dispatcher.UIThread.Post(UpdateStoreSummaryIfLive);
            else
                UpdateStoreSummaryIfLive();
        }
    }

    private void UpdateStoreSummaryIfLive()
    {
        if (!_isDisposed)
            UpdateStoreSummary();
    }

    // Rows that merely showed the previous default follow a Settings change, so Generate honors the
    // setting the UI now shows; rows the user explicitly set to another preset keep their choice.
    private void ApplyDefaultPresetChange(int oldValue, int newValue)
    {
        if (_isDisposed)
            return;

        ProxyPreset oldPreset = ToPreset(oldValue);
        ProxyPreset newPreset = ToPreset(newValue);
        if (oldPreset == newPreset)
            return;

        foreach (ProxyClipViewModel clip in Clips)
        {
            if (clip.IsFollowingDefault)
                clip.ApplyDefaultPreset(newPreset);
        }
    }

    // Collapse a burst of store/job events (e.g. a "Generate all" completing N clips raises ~2N
    // events) into one full rebuild per UI tick; the heavy Refresh walks the whole project graph and
    // stats every source, so running it per event scales the timeline stall with clip count.
    private void ScheduleRefresh()
    {
        if (_isDisposed)
            return;

        if (Interlocked.Exchange(ref _refreshScheduled, 1) == 1)
            return;

        RefreshScheduler(() =>
        {
            Interlocked.Exchange(ref _refreshScheduled, 0);
            if (!_isDisposed)
                Refresh();
        });
    }

    private void Refresh()
    {
        if (_isDisposed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                    Refresh();
            });
            return;
        }

        // Only a deliberate user choice survives a rebuild verbatim; a background proxy finishing
        // (Registered/StateChanged) or a terminal job triggers Refresh, and an explicit pick must not be
        // reverted to following. A row that was merely pinned to an existing proxy re-derives its state
        // below so a since-deleted/evicted proxy no longer leaves it stuck off the default. Key on the
        // canonical Source.AbsolutePath (what enumeration dedupes on), not clip.Path (a raw LocalPath):
        // the same file spelled differently (case / symlink) must still re-associate its prior state.
        Dictionary<string, (ProxyPreset Preset, bool Explicit)> priorChoiceByKey = Clips.ToDictionary(
            static clip => clip.Source.AbsolutePath,
            static clip => (clip.Preset.Value, clip.IsExplicitlyChosen));
        HashSet<string> selectedKeys =
            [.. Clips.Where(static clip => clip.IsSelected.Value).Select(static clip => clip.Source.AbsolutePath)];

        ClearClips();
        foreach ((string path, ProxyFingerprint fingerprint) in EnumerateProjectVideoSources())
        {
            string key = fingerprint.AbsolutePath;
            ProxyPreset preset;
            bool followingDefault;
            bool explicitlyChosen;
            if (priorChoiceByKey.TryGetValue(key, out (ProxyPreset Preset, bool Explicit) prior) && prior.Explicit)
            {
                preset = prior.Preset;
                followingDefault = false;
                explicitlyChosen = true;
            }
            else
            {
                (preset, followingDefault) = ResolveInitialPreset(fingerprint);
                explicitlyChosen = false;
            }

            ProxyEntry? entry = FindEntry(fingerprint, preset);
            var clip = new ProxyClipViewModel(this, path, fingerprint, preset, entry, followingDefault, explicitlyChosen);
            if (selectedKeys.Contains(key))
                clip.IsSelected.Value = true;
            Clips.Add(clip);
        }

        RefreshJobs();
        UpdateStoreSummary();
        UpdateClipSummary();
    }

    private void RefreshJobs()
    {
        if (_isDisposed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                    RefreshJobs();
            });
            return;
        }

        ProxyJob[] pendingJobs = [.. _queue?.Pending() ?? []];
        foreach (ProxyClipViewModel clip in Clips)
        {
            clip.UpdateJob(pendingJobs.FirstOrDefault(job => IsMatchingJob(job, clip)));
        }

        JobSummary.Value = pendingJobs.Length == 0
            ? Strings.ProxyQueueIdle
            : pendingJobs.Length == 1
                ? Strings.ProxyQueuedJobSingular
                : string.Format(CultureInfo.CurrentCulture, Strings.ProxyQueuedJobPlural, pendingJobs.Length);
    }

    /// <summary>
    /// The authoritative enumeration of the video sources referenced by the current project.
    /// Its result is the single source of truth for both the per-project usage total
    /// (<see cref="UpdateStoreSummary"/>) and the delete-all action
    /// (<see cref="DeleteAllForProjectAsync"/>) — both consume it via <see cref="Clips"/>.
    /// Any new element type that can hold a <see cref="VideoSource"/> MUST be walked here,
    /// otherwise its sources are silently excluded from usage accounting and delete-all.
    /// </summary>
    private IEnumerable<(string Path, ProxyFingerprint Fingerprint)> EnumerateProjectVideoSources()
    {
        HashSet<string> seenPaths = new(StringComparer.Ordinal);
        HashSet<Scene> seenScenes = new(ReferenceEqualityComparer.Instance);
        HashSet<(Scene, CompositionTarget?)> visitedRefScenes = [];
        ProxyEntry[] storeEntries = [.. _store?.Enumerate() ?? []];
        ProxyPreset preferredPreset = ToPreset(_config.DefaultPreset);

        // The totals and the delete action are labelled project-wide, so scan every scene in the
        // open project (not just the edited one); a clip used only by another scene must count too.
        foreach (Scene scene in EnumerateProjectScenes())
        {
            if (!seenScenes.Add(scene))
                continue;

            foreach (Element element in scene.Children)
            {
                foreach (VideoSource source in ProxySourceEnumerator.EnumerateVideoSources(element, visitedRefScenes))
                {
                    if (TryGetVideoSource(source, storeEntries, seenPaths, preferredPreset, out var item))
                        yield return item;
                }
            }
        }
    }

    private IEnumerable<Scene> EnumerateProjectScenes()
    {
        if (_scene.FindHierarchicalParent<Project>() is { } project)
        {
            foreach (Scene scene in project.Items.OfType<Scene>())
                yield return scene;
        }
        else
        {
            yield return _scene;
        }
    }

    private void UpdateStoreSummary()
    {
        if (_store == null)
        {
            ProjectUsageText.Value = Strings.ProxyUnavailable;
            StoreUsageText.Value = Strings.ProxyUnavailable;
            StoreCapText.Value = Strings.ProxyUnavailable;
            StoreSummary.Value = Strings.ProxyStoreUnavailable;
            return;
        }

        HashSet<string> paths = [.. Clips.Select(static c => c.Source.AbsolutePath)];
        long projectBytes = _store.GetTotalBytes(paths);
        long totalBytes = _store.GetTotalBytes();
        ProjectUsageText.Value = FormatBytes(projectBytes);
        StoreUsageText.Value = FormatBytes(totalBytes);
        StoreCapText.Value = FormatBytes(_storeCapInfo?.MaxTotalBytes ?? _config.MaxTotalBytes);
        StoreSummary.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxyStoreSummaryFormat,
            ProjectUsageText.Value,
            StoreUsageText.Value,
            StoreCapText.Value);
    }

    internal void UpdateClipSummary()
    {
        int ready = Clips.Count(static c => c.IsReady.Value);
        int stale = Clips.Count(static c => c.IsStale.Value);
        int failed = Clips.Count(static c => c.IsFailed.Value);
        int missing = Clips.Count(static c => c.IsMissing.Value);
        int selected = Clips.Count(static c => c.IsSelected.Value);

        HasClips.Value = Clips.Count > 0;
        HasSelection.Value = selected > 0;
        ClipCountText.Value = Clips.Count == 1
            ? Strings.ProxyClipCountSingular
            : string.Format(CultureInfo.CurrentCulture, Strings.ProxyClipCountPlural, Clips.Count);

        string stateSummary = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxyClipSummaryFormat,
            Clips.Count,
            ready,
            stale,
            failed,
            missing);
        ClipSummary.Value = stateSummary;
        SelectionSummary.Value = selected == 1
            ? Strings.ProxySelectedSingular
            : string.Format(CultureInfo.CurrentCulture, Strings.ProxySelectedPlural, selected);
    }

    private ProxyEntry? FindEntry(ProxyFingerprint source, ProxyPreset preset)
    {
        if (_store == null)
            return null;

        if (_store.TryGet(source, preset) is { } exact)
            return exact;

        return _store.Enumerate()
            .Where(entry => entry.Preset == preset)
            .FirstOrDefault(entry => entry.Source.AbsolutePath == source.AbsolutePath);
    }

    // The preset a freshly listed row starts on, and whether that row tracks the global default. A row
    // pinned to an already-generated proxy (at the default preset or any other) does not follow the
    // default; only a row that fell through to the default with no generated proxy does.
    private (ProxyPreset Preset, bool FollowingDefault) ResolveInitialPreset(ProxyFingerprint source)
    {
        ProxyPreset defaultPreset = ToPreset(_config.DefaultPreset);
        if (IsGenerated(FindEntry(source, defaultPreset)))
            return (defaultPreset, false);

        foreach (ProxyPreset preset in s_presetOrder)
        {
            if (IsGenerated(FindEntry(source, preset)))
                return (preset, false);
        }

        return (defaultPreset, true);
    }

    private static bool IsGenerated(ProxyEntry? entry)
    {
        return entry?.State is ProxyState.Ready or ProxyState.Stale;
    }

    private void OnStoreChanged(object? sender, ProxyStoreChangedEventArgs e)
    {
        if (_isDisposed)
            return;

        // A Touched event (preview resolving a proxy) only bumps LastUsedUtc; rebuilding the clip
        // list on it would silently clear the user's bulk-action selection during normal playback.
        if (e.Kind == ProxyStoreChangeKind.Touched)
            return;

        ScheduleRefresh();
    }

    private void OnJobChanged(object? sender, ProxyJobChangedEventArgs e)
    {
        if (_isDisposed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                    OnJobChanged(sender, e);
            });
            return;
        }

        StatusMessage.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxyJobStatusWithMessageFormat,
            Path.GetFileName(e.Job.Source.AbsolutePath),
            GetJobStatusText(e.Job.Status));
        RefreshJobs();
        if (e.Kind is ProxyJobChangeKind.Succeeded
            or ProxyJobChangeKind.Failed
            or ProxyJobChangeKind.Canceled
            or ProxyJobChangeKind.Skipped)
        {
            ScheduleRefresh();
        }
    }

    private static ProxyPreset ToPreset(int value)
    {
        return Enum.IsDefined(typeof(ProxyPreset), value)
            ? (ProxyPreset)value
            : ProxyPreset.Quarter;
    }

    private static bool IsMatchingJob(ProxyJob job, ProxyClipViewModel clip)
        => job.Preset == clip.Preset.Value && job.Source.Equals(clip.Source);
}

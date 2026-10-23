using System.Collections.Specialized;
using Avalonia.Threading;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor.Components.FileBrowserTab.Services;
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

    private void OnDefaultPresetChanged(object? sender, PropertyValueChangedEventArgs<int> e)
        => RunOnUIIfLive(() => ApplyDefaultPresetChange(e.OldValue, e.NewValue));

    // A cap-only change shifts the store summary (StoreCapText / usage), which no other signal on
    // this tab reflects until an unrelated store / job / scene refresh; refresh it here.
    private void OnMaxTotalBytesChanged(object? sender, PropertyValueChangedEventArgs<long> e)
        => RunOnUIIfLive(UpdateStoreSummary);

    // Rows that merely showed the previous default follow a Settings change, so Generate honors the
    // setting the UI now shows; rows the user explicitly set to another preset keep their choice.
    private void ApplyDefaultPresetChange(int oldValue, int newValue)
    {
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
            Refresh();
        });
    }

    private void Refresh() => RunOnUIIfLive(() =>
    {
        // Only an explicit user choice survives a rebuild; key on Source.AbsolutePath so the same file
        // spelled differently keeps its state.
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
    });

    // Runs the action now on the UI thread, else posts it there; either way only while the tab is live.
    private void RunOnUIIfLive(Action action)
    {
        if (_isDisposed)
            return;

        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            PostIfLive(action);
    }

    // Re-enters on the UI thread unless the tab is disposed by the time the dispatcher runs it.
    private void PostIfLive(Action action)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isDisposed)
                action();
        });
    }

    private void RefreshJobs() => RunOnUIIfLive(() =>
    {
        ProxyJob[] pendingJobs = [.. _queue?.Pending() ?? []];
        foreach (ProxyClipViewModel clip in Clips)
        {
            clip.UpdateJob(pendingJobs.FirstOrDefault(job => IsMatchingJob(job, clip)));
        }
    });

    /// <summary>
    /// Enumerates the project's video sources; usage totals and delete-all both rely on it, so any
    /// element type holding a <see cref="VideoSource"/> must be walked here.
    /// </summary>
    private IEnumerable<(string Path, ProxyFingerprint Fingerprint)> EnumerateProjectVideoSources()
    {
        HashSet<string> seenPaths = new(StringComparer.Ordinal);
        HashSet<Scene> seenScenes = new(ReferenceEqualityComparer.Instance);
        HashSet<(Scene, CompositionTarget?)> visitedRefScenes = [];
        ProxyEntry[] storeEntries = [.. _store?.Enumerate() ?? []];
        ProxyPreset preferredPreset = ToPreset(_config.DefaultPreset.Value);

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
                    if (ProxyClipSourceResolver.TryGetVideoSource(source, storeEntries, seenPaths, preferredPreset, out var item))
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
            return;
        }

        HashSet<string> paths = [.. Clips.Select(static c => c.Source.AbsolutePath)];
        long projectBytes = _store.GetTotalBytes(paths);
        long totalBytes = _store.GetTotalBytes();
        ProjectUsageText.Value = MediaFileInfo.FormatFileSize(projectBytes);
        StoreUsageText.Value = MediaFileInfo.FormatFileSize(totalBytes);
        StoreCapText.Value = MediaFileInfo.FormatFileSize(_storeCapInfo?.MaxTotalBytes ?? _config.MaxTotalBytes.Value);
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
        ProxyPreset defaultPreset = ToPreset(_config.DefaultPreset.Value);
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
        // A Touched event (preview resolving a proxy) only bumps LastUsedUtc; rebuilding the clip
        // list on it would silently clear the user's bulk-action selection during normal playback.
        if (e.Kind == ProxyStoreChangeKind.Touched)
            return;

        ScheduleRefresh();
    }

    private void OnJobChanged(object? sender, ProxyJobChangedEventArgs e) => RunOnUIIfLive(() =>
    {
        RefreshJobs();
        if (e.Kind is ProxyJobChangeKind.Succeeded
            or ProxyJobChangeKind.Failed
            or ProxyJobChangeKind.Canceled
            or ProxyJobChangeKind.Skipped)
        {
            ScheduleRefresh();
        }
    });

    private static ProxyPreset ToPreset(int value)
    {
        return Enum.IsDefined(typeof(ProxyPreset), value)
            ? (ProxyPreset)value
            : ProxyPreset.Quarter;
    }

    private static bool IsMatchingJob(ProxyJob job, ProxyClipViewModel clip)
        => job.Preset == clip.Preset.Value && job.Source.Equals(clip.Source);
}

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

public sealed partial class ProxiesTabViewModel : IDisposable, IToolContext
{
    private static readonly ProxyPreset[] s_presetOrder =
    [
        ProxyPreset.Half,
        ProxyPreset.Quarter,
        ProxyPreset.Eighth,
    ];

    // Heaviness floor for the bulk "generate all" action (SC-001 targets >= 4K / >= 60 Mbps
    // media). Coarse heuristics that should become user-configurable and metadata-driven
    // later. Original resolution is only known once a proxy entry exists, so never-proxied
    // clips fall back to the file-size floor as a rough bitrate x duration proxy.
    private const long MinBulkSourcePixelCount = 1920L * 1080L;
    private const long MinBulkSourceFileBytes = 32L * 1024L * 1024L;

    // Explicit per-clip / per-selection generate & regenerate is foreground work: a clip the editor
    // needs now must jump ahead of the background "Generate all" sweep, which stays at the queue
    // default so equal-priority bulk jobs keep arrival order.
    private const int ForegroundGenerationPriority = 1;
    private const int BulkGenerationPriority = 0;

    // Matches ProxyResolver.DensityTolerance so the tab's offline ranking clamps identically.
    private const float DensityTolerance = 1e-6f;

    private readonly CompositeDisposable _disposables = [];
    private readonly SerialDisposable _sceneEditedSubscriptions = new();
    private readonly Scene _scene;
    private readonly IProxyStore? _store;
    private readonly IProxyJobQueue? _queue;
    private readonly IProxyStoreCapInfo? _storeCapInfo;
    private readonly ProxyStoreConfig _config;
    private bool _isDisposed;
    private int _refreshScheduled;

    public ProxiesTabViewModel(IEditorContext editorContext)
    {
        _scene = editorContext.GetService<Scene>()!;
        _store = editorContext.GetService<IProxyStore>();
        _queue = editorContext.GetService<IProxyJobQueue>();
        _storeCapInfo = editorContext.GetService<IProxyStoreCapInfo>();
        _config = GlobalConfiguration.Instance.ProxyStoreConfig;

        ClipSummary = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        ClipCountText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        HasClips = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        HasSelection = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        SelectionSummary = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        JobSummary = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        ProjectUsageText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StoreUsageText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StoreCapText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StoreSummary = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StatusMessage = new ReactiveProperty<string>(Strings.ProxyReady)
            .DisposeWith(_disposables);

        GenerateSelectedCommand = new AsyncReactiveCommand()
            .WithSubscribe(GenerateSelectedAsync)
            .DisposeWith(_disposables);
        RegenerateSelectedCommand = new AsyncReactiveCommand()
            .WithSubscribe(RegenerateSelectedAsync)
            .DisposeWith(_disposables);
        DeleteSelectedCommand = new ReactiveCommand()
            .WithSubscribe(DeleteSelected)
            .DisposeWith(_disposables);
        GenerateAllCommand = new AsyncReactiveCommand()
            .WithSubscribe(GenerateAllAsync)
            .DisposeWith(_disposables);
        DeleteAllForProjectCommand = new AsyncReactiveCommand()
            .WithSubscribe(DeleteAllForProjectAsync)
            .DisposeWith(_disposables);
        RefreshCommand = new ReactiveCommand()
            .WithSubscribe(Refresh)
            .DisposeWith(_disposables);

        if (_store != null)
        {
            _store.Changed += OnStoreChanged;
            Disposable.Create(() => _store.Changed -= OnStoreChanged)
                .DisposeWith(_disposables);
        }

        if (_queue != null)
        {
            _queue.JobChanged += OnJobChanged;
            Disposable.Create(() => _queue.JobChanged -= OnJobChanged)
                .DisposeWith(_disposables);
        }

        // Media can be added, removed, or replaced while the tab is open; Scene.Edited fires for
        // both structural child changes and forwarded element edits, so Generate All / Delete never
        // act on a stale clip list. ScheduleRefresh coalesces an edit burst into one rebuild.
        _sceneEditedSubscriptions.DisposeWith(_disposables);
        RefreshSceneSubscriptions();

        // Project-wide totals / Generate All / Delete scan every project scene, so a media edit
        // in another open scene must also refresh; watch the project's scene set for add/remove.
        if (_scene.FindHierarchicalParent<Project>() is { } project)
        {
            project.Items.CollectionChanged += OnProjectItemsChanged;
            Disposable.Create(() => project.Items.CollectionChanged -= OnProjectItemsChanged)
                .DisposeWith(_disposables);
        }

        _config.PropertyChanged += OnProxyConfigPropertyChanged;
        Disposable.Create(() => _config.PropertyChanged -= OnProxyConfigPropertyChanged)
            .DisposeWith(_disposables);

        Refresh();
    }

    public ObservableCollection<ProxyClipViewModel> Clips { get; } = [];

    public ReactiveProperty<string> ClipSummary { get; }

    public ReactiveProperty<string> ClipCountText { get; }

    public ReactivePropertySlim<bool> HasClips { get; }

    public ReactivePropertySlim<bool> HasSelection { get; }

    public ReactiveProperty<string> SelectionSummary { get; }

    public ReactiveProperty<string> JobSummary { get; }

    public ReactiveProperty<string> ProjectUsageText { get; }

    public ReactiveProperty<string> StoreUsageText { get; }

    public ReactiveProperty<string> StoreCapText { get; }

    public ReactiveProperty<string> StoreSummary { get; }

    public ReactiveProperty<string> StatusMessage { get; }

    public AsyncReactiveCommand GenerateSelectedCommand { get; }

    public AsyncReactiveCommand RegenerateSelectedCommand { get; }

    public ReactiveCommand DeleteSelectedCommand { get; }

    public AsyncReactiveCommand GenerateAllCommand { get; }

    public AsyncReactiveCommand DeleteAllForProjectCommand { get; }

    public ReactiveCommand RefreshCommand { get; }

    /// <summary>
    /// Confirms the destructive "delete all proxies for this project" action. The proxy store
    /// is a machine-wide shared cache (FR-011), so the entries removed here may also be relied
    /// on by other projects that reference the same source files; those projects must then
    /// regenerate them. The argument is the number of cached proxy entries that would be
    /// deleted. The default shows a confirmation dialog; tests substitute it to drive the
    /// accept and decline paths without a UI. Returns <see langword="true"/> to proceed.
    /// </summary>
    public Func<int, Task<bool>> ConfirmDeleteAllForProjectAsync { get; set; }
        = ShowDeleteAllForProjectConfirmationAsync;

    public ToolTabExtension Extension => ProxiesTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.Proxies);

    internal Action<Notification> Notify { get; set; } = NotificationService.Show;

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        ClearClips();
        _disposables.Dispose();
    }

    public void WriteToJson(System.Text.Json.Nodes.JsonObject json)
    {
    }

    public void ReadFromJson(System.Text.Json.Nodes.JsonObject json)
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    // Test seam: the unit-test environment has no pumping dispatcher, so tests inject an
    // immediate scheduler to exercise the coalesced event-driven rebuild synchronously.
    internal Action<Action> RefreshScheduler { get; set; } = static action => Dispatcher.UIThread.Post(action);

    private static bool TryGetVideoSource(
        VideoSource? source,
        IReadOnlyList<ProxyEntry> storeEntries,
        HashSet<string> seenPaths,
        ProxyPreset preferredPreset,
        out (string Path, ProxyFingerprint Fingerprint) item)
    {
        if (source is not { HasUri: true } || source.Uri is not { IsFile: true } uri)
        {
            item = default;
            return false;
        }

        string path = uri.LocalPath;
        if (ProxyFingerprint.TryFromFile(path, out ProxyFingerprint fingerprint))
        {
            if (!seenPaths.Add(fingerprint.AbsolutePath))
            {
                item = default;
                return false;
            }

            item = (path, fingerprint);
            return true;
        }

        string normalizedPath = NormalizeSourcePath(path);
        ProxyEntry? existing = SelectOfflineEntry(
            storeEntries.Where(entry => entry.Source.AbsolutePath == normalizedPath),
            preferredPreset);
        if (existing == null || !seenPaths.Add(existing.Source.AbsolutePath))
        {
            item = default;
            return false;
        }

        item = (path, existing.Source);
        return true;
    }

    // Pick the offline row's entry the same way preview decoding will. Among path-matching Ready
    // entries, mirror ProxyResolver.SelectBest: prefer the densest whose supply density is within the
    // preferred-preset cap, else the densest overall — so the row binds to the fingerprint playback
    // actually decodes. Only when no Ready proxy exists does it fall back to stale/failed metadata
    // (ranked by state) so the offline clip still surfaces a row.
    private static ProxyEntry? SelectOfflineEntry(IEnumerable<ProxyEntry> pathMatches, ProxyPreset preferredPreset)
    {
        List<ProxyEntry> candidates = [.. pathMatches];
        if (candidates.Count == 0)
            return null;

        // Choose the newest source version across ALL candidates (any state), then confine the whole
        // selection to that source — mirroring ProxyResolver.ResolveByPath. A newer Failed/Stale entry for
        // a replaced source must outrank an older Ready proxy of a stale fingerprint, so the row reflects
        // the current source's state instead of binding delete/regenerate to content preview won't decode.
        // Precompute newest generation per source once (a linear group) rather than re-scanning inside
        // the sort comparer, which would be O(n²) on a path with many accumulated versions/presets.
        Dictionary<ProxyFingerprint, DateTime> newestBySource = candidates
            .GroupBy(e => e.Source)
            .ToDictionary(g => g.Key, g => g.Max(e => e.GeneratedAtUtc));
        ProxyFingerprint newest = candidates
            .OrderByDescending(e => newestBySource[e.Source])
            .ThenByDescending(e => e.Source.MtimeUtc)
            .First().Source;
        List<ProxyEntry> fromNewest = [.. candidates.Where(e => e.Source == newest)];

        float cap = ProxyPresetDefinitions.Get(preferredPreset).Scale;
        ProxyEntry? cappedWinner = null;
        float cappedDensity = -1f;
        ProxyEntry? densestWinner = null;
        float densestDensity = -1f;
        foreach (ProxyEntry entry in fromNewest)
        {
            if (entry.State != ProxyState.Ready)
                continue;

            float density = SupplyDensityOf(entry);
            if (density <= cap + DensityTolerance && density > cappedDensity)
            {
                cappedWinner = entry;
                cappedDensity = density;
            }

            if (density > densestDensity)
            {
                densestWinner = entry;
                densestDensity = density;
            }
        }

        return cappedWinner ?? densestWinner ?? fromNewest
            .OrderBy(entry => OfflineEntryRank(entry.State))
            .ThenByDescending(entry => (long)entry.ProxyDecodedFrameSize.Width * entry.ProxyDecodedFrameSize.Height)
            .ThenByDescending(entry => entry.GeneratedAtUtc)
            .FirstOrDefault();
    }

    // Mirrors ProxyResolution.SupplyDensity (long-edge ratio) so the tab ranks Ready entries exactly as
    // the resolver does; ProxyEntry already stores both frame sizes.
    private static float SupplyDensityOf(ProxyEntry entry)
    {
        int originalLongEdge = Math.Max(entry.OriginalLogicalFrameSize.Width, entry.OriginalLogicalFrameSize.Height);
        int proxyLongEdge = Math.Max(entry.ProxyDecodedFrameSize.Width, entry.ProxyDecodedFrameSize.Height);
        return originalLongEdge == 0 || proxyLongEdge == 0
            ? 1f
            : (float)proxyLongEdge / originalLongEdge;
    }

    // Ready first (a usable stand-in), then Stale, then in-progress, then failed/absent last. Mirrors
    // ProxyResolver's offline preference so the tab and the preview agree on which entry wins.
    private static int OfflineEntryRank(ProxyState state) => state switch
    {
        ProxyState.Ready => 0,
        ProxyState.Stale => 1,
        ProxyState.Partial => 2,
        ProxyState.Generating => 3,
        ProxyState.Failed => 4,
        _ => 5,
    };

    private static string NormalizeSourcePath(string path)
    {
        // Resolve a symlink to the target path the store entry was keyed on (FromFile resolves the
        // link at registration). A moved/deleted target leaves the link resolvable via
        // returnFinalTarget:false, so a broken symlink still matches its stored entry instead of
        // dropping the clip from the tab. Case folding must match ProxyFingerprint.NormalizeAbsolutePath.
        string fullPath = ResolveLinkTarget(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? fullPath.ToUpperInvariant()
            : fullPath;
    }

    private static string ResolveLinkTarget(string fullPath)
    {
        try
        {
            return new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: false)?.FullName ?? fullPath;
        }
        catch
        {
            return fullPath;
        }
    }

    private void ClearClips()
    {
        foreach (ProxyClipViewModel clip in Clips)
        {
            clip.Dispose();
        }

        Clips.Clear();
    }
}

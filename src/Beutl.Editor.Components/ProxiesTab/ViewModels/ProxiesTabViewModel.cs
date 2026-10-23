using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media.Proxy;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Services;
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

    // Heaviness floor for the bulk "generate all" action. Coarse heuristics that should become
    // user-configurable and metadata-driven later. Original resolution is only known once a proxy entry exists, so never-proxied
    // clips fall back to the file-size floor as a rough bitrate x duration proxy.
    private const long MinBulkSourcePixelCount = 1920L * 1080L;
    private const long MinBulkSourceFileBytes = 32L * 1024L * 1024L;

    // Explicit per-clip / per-selection generate & regenerate is foreground work: a clip the editor
    // needs now must jump ahead of the background "Generate all" sweep, which stays at the queue
    // default so equal-priority bulk jobs keep arrival order.
    private const int ForegroundGenerationPriority = 1;
    private const int BulkGenerationPriority = 0;

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
        ProjectUsageText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StoreUsageText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StoreCapText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);

        GenerateSelectedCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => GenerateSelectedAsync(refreshClips: false))
            .DisposeWith(_disposables);
        RegenerateSelectedCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => GenerateSelectedAsync(refreshClips: true))
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

        _config.DefaultPreset.ValueChanged += OnDefaultPresetChanged;
        Disposable.Create(() => _config.DefaultPreset.ValueChanged -= OnDefaultPresetChanged)
            .DisposeWith(_disposables);
        _config.MaxTotalBytes.ValueChanged += OnMaxTotalBytesChanged;
        Disposable.Create(() => _config.MaxTotalBytes.ValueChanged -= OnMaxTotalBytesChanged)
            .DisposeWith(_disposables);

        Refresh();
    }

    public ObservableCollection<ProxyClipViewModel> Clips { get; } = [];

    public ReactiveProperty<string> ClipSummary { get; }

    public ReactiveProperty<string> ClipCountText { get; }

    public ReactivePropertySlim<bool> HasClips { get; }

    public ReactivePropertySlim<bool> HasSelection { get; }

    public ReactiveProperty<string> SelectionSummary { get; }

    public ReactiveProperty<string> ProjectUsageText { get; }

    public ReactiveProperty<string> StoreUsageText { get; }

    public ReactiveProperty<string> StoreCapText { get; }

    public AsyncReactiveCommand GenerateSelectedCommand { get; }

    public AsyncReactiveCommand RegenerateSelectedCommand { get; }

    public ReactiveCommand DeleteSelectedCommand { get; }

    public AsyncReactiveCommand GenerateAllCommand { get; }

    public AsyncReactiveCommand DeleteAllForProjectCommand { get; }

    public ReactiveCommand RefreshCommand { get; }

    /// <summary>
    /// Confirms deleting the given number of the project's proxies, which other projects may share.
    /// Returns <see langword="true"/> to proceed.
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

    private void ClearClips()
    {
        foreach (ProxyClipViewModel clip in Clips)
        {
            clip.Dispose();
        }

        Clips.Clear();
    }
}

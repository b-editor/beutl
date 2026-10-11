using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Avalonia.Threading;

using Beutl.Animation.Easings;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.NodeGraph;
using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.Editor.Components.LibraryTab.ViewModels;

public sealed class LibraryTabViewModel : IDisposable, IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly SemaphoreSlim _asyncLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CancellationToken _lifetimeToken;
    private int _disposed;
    private int _searchVersion;
    private int _refreshQueued;
    private string? _query;

    public LibraryTabViewModel(IEditorContext editorContext)
    {
        _ = editorContext;
        _lifetimeToken = _lifetimeCancellation.Token;

        // Subscribed before the items are read, so that nothing registered in between is missed.
        LibraryService.Current.ItemsChanged += OnLibraryItemsChanged;
        _disposables.Add(Disposable.Create(() => LibraryService.Current.ItemsChanged -= OnLibraryItemsChanged));

        IReadOnlyList<LibraryItem> libItems = LibraryService.Current.Items;
        LibraryItems = new CoreList<LibraryItemViewModel>(libItems.Count);
        LibraryItems.AddRange(libItems.Select(x => LibraryItemViewModel.CreateFromLibraryItem(x)));

        IList<GraphNodeRegistry.BaseRegistryItem> nodes = GraphNodeRegistry.GetRegistered();
        Nodes = new List<LibraryItemViewModel>(nodes.Count);
        Nodes.AddRange(nodes.Select(x => LibraryItemViewModel.CreateFromGraphNodeRegistryItem(x)));

        AllItems = new(CreateAllItems(LibraryService.Current._totalCount + GraphNodeRegistry.s_totalCount));
    }

    public ReactiveCollection<Easing> Easings { get; } =
    [
        new BackEaseIn(),
        new BackEaseInOut(),
        new BackEaseOut(),
        new BounceEaseIn(),
        new BounceEaseInOut(),
        new BounceEaseOut(),
        new CircularEaseIn(),
        new CircularEaseInOut(),
        new CircularEaseOut(),
        new CubicEaseIn(),
        new CubicEaseInOut(),
        new CubicEaseOut(),
        new ElasticEaseIn(),
        new ElasticEaseInOut(),
        new ElasticEaseOut(),
        new ExponentialEaseIn(),
        new ExponentialEaseInOut(),
        new ExponentialEaseOut(),
        new QuadraticEaseIn(),
        new QuadraticEaseInOut(),
        new QuadraticEaseOut(),
        new QuarticEaseIn(),
        new QuarticEaseInOut(),
        new QuarticEaseOut(),
        new QuinticEaseIn(),
        new QuinticEaseInOut(),
        new QuinticEaseOut(),
        new SineEaseIn(),
        new SineEaseInOut(),
        new SineEaseOut(),
        new LinearEasing(),
        new HoldEasing(),
    ];

    public CoreList<LibraryItemViewModel> LibraryItems { get; }

    public List<LibraryItemViewModel> Nodes { get; }

    public CoreList<KeyValuePair<int, LibraryItemViewModel>> AllItems { get; }

    public ReactiveCollection<KeyValuePair<int, LibraryItemViewModel>> SearchResult { get; } = [];


    public int SelectedTab { get; set; } = 2;

    [SuppressMessage("Performance", "CA1822:メンバーを static に設定します")]
    public CoreDictionary<string, LibraryTabDisplayMode> LibraryTabDisplayModes
        => GlobalConfiguration.Instance.EditorConfig.LibraryTabDisplayModes;

    private List<KeyValuePair<int, LibraryItemViewModel>> CreateAllItems(int capacity = 0)
    {
        var allItems = new List<KeyValuePair<int, LibraryItemViewModel>>(capacity);
        AddAllItems(allItems, LibraryItems);
        AddAllItems(allItems, Nodes);
        return allItems;
    }

    private static void AddAllItems(List<KeyValuePair<int, LibraryItemViewModel>> allItems,
        IEnumerable<LibraryItemViewModel> items)
    {
        foreach (LibraryItemViewModel innerItem in items)
        {
            allItems.Add(new(0, innerItem));
            AddAllItems(allItems, innerItem.Children);
        }
    }

    // Raised on the thread that registered or unregistered the items.
    private void OnLibraryItemsChanged(object? sender, EventArgs e)
    {
        // Coalesced, since an extension registers its items one at a time.
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0)
            return;

        Dispatcher.UIThread.Post(RefreshLibraryItems, DispatcherPriority.Background);
    }

    private void RefreshLibraryItems()
    {
        Volatile.Write(ref _refreshQueued, 0);
        if (Volatile.Read(ref _disposed) != 0)
            return;

        SyncItems(LibraryItems, LibraryService.Current.Items, null);
        AllItems.Replace(CreateAllItems(AllItems.Count));
        if (_query is { } query)
            _ = SearchCore(query, CancellationToken.None, recordUsage: false);
    }

    // Brings `viewModels` in line with `items`. The view models of the items that stay are kept, with their
    // children brought in line the same way, so that their nodes in the tree stay expanded.
    private static void SyncItems(CoreList<LibraryItemViewModel> viewModels, IReadOnlyList<LibraryItem> items,
        string? parentFullName)
    {
        var existing = new Dictionary<LibraryItem, LibraryItemViewModel>();
        foreach (LibraryItemViewModel viewModel in viewModels)
        {
            if (viewModel.Data is LibraryItem item)
                existing[item] = viewModel;
        }

        var next = new LibraryItemViewModel[items.Count];
        for (int i = 0; i < next.Length; i++)
        {
            LibraryItem item = items[i];
            if (existing.TryGetValue(item, out LibraryItemViewModel? viewModel))
            {
                if (item is GroupLibraryItem group)
                    SyncItems(viewModel.Children, group.Items, viewModel.FullDisplayName);
            }
            else
            {
                viewModel = LibraryItemViewModel.CreateFromLibraryItem(item, parentFullName);
            }

            next[i] = viewModel;
        }

        for (int i = viewModels.Count - 1; i >= 0; i--)
        {
            if (Array.IndexOf(next, viewModels[i]) < 0)
                viewModels.RemoveAt(i);
        }

        // Registering appends and unregistering removes, so the view models that stay are already in order.
        for (int i = 0; i < next.Length; i++)
        {
            if (i == viewModels.Count || !ReferenceEquals(viewModels[i], next[i]))
                viewModels.Insert(i, next[i]);
        }
    }

    // The search box was emptied, so a library change no longer searches again.
    public void ClearSearch()
    {
        _query = null;
        // A search still running, such as one a library change started, must not fill the results again.
        Interlocked.Increment(ref _searchVersion);
        SearchResult.Clear();
    }

    public Task Search(string str, CancellationToken cancellationToken)
    {
        return SearchCore(str, cancellationToken, recordUsage: true);
    }

    // A search that only follows a library change is not a search by the user, so it records no usage.
    private async Task SearchCore(string str, CancellationToken cancellationToken, bool recordUsage)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        // Searched again when the library changes, so that the results show what is registered now.
        _query = str;

        UsageTelemetry? usage = UsageTelemetry.Current;
        long epoch = 0;
        bool collect = recordUsage && usage?.TryGetCollectionEpoch(out epoch) == true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        CancellationToken token = cancellation.Token;
        try
        {
            await _asyncLock.WaitAsync(token);
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            return;
        }

        int searchVersion = Interlocked.Increment(ref _searchVersion);
        try
        {
            token.ThrowIfCancellationRequested();
            PublishSearchResult([], searchVersion, cancellationToken);
            KeyValuePair<int, LibraryItemViewModel>[] items = AllItems.ToArray();
            KeyValuePair<int, LibraryItemViewModel>[] results = await Task.Run(() =>
            {
                var matches = new List<KeyValuePair<int, LibraryItemViewModel>>();
                Regex[] regices = RegexHelper.CreateRegexes(str);
                foreach (KeyValuePair<int, LibraryItemViewModel> item in items)
                {
                    token.ThrowIfCancellationRequested();
                    int score = item.Value.Match(regices);
                    token.ThrowIfCancellationRequested();
                    if (score > 0)
                    {
                        matches.Add(new(score, item.Value));
                    }
                }

                return matches.OrderByDescending(x => x.Key).ToArray();
            }, token);
            token.ThrowIfCancellationRequested();
            PublishSearchResult(results, searchVersion, cancellationToken);
            if (collect) usage!.Record("tool.command", "Library", "Search", epoch: epoch);
        }
        catch (OperationCanceledException)
        {
            PublishSearchResult([], searchVersion, CancellationToken.None);
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    private void PublishSearchResult(IReadOnlyList<KeyValuePair<int, LibraryItemViewModel>> results,
        int searchVersion, CancellationToken cancellationToken)
    {
        void Publish()
        {
            if (IsStale())
                return;

            SearchResult.Clear();
            foreach (KeyValuePair<int, LibraryItemViewModel> item in results)
            {
                if (IsStale())
                    return;

                SearchResult.Add(item);
            }
        }

        bool IsStale()
        {
            return Volatile.Read(ref _disposed) != 0 || cancellationToken.IsCancellationRequested
                || searchVersion != Volatile.Read(ref _searchVersion);
        }

        if (Dispatcher.UIThread.CheckAccess())
            Publish();
        else
            Dispatcher.UIThread.Post(Publish);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _disposables.Dispose();
        Easings.Clear();
        LibraryItems.Clear();
        Nodes.Clear();
        AllItems.Clear();
        SearchResult.Clear();
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public ToolTabExtension Extension => LibraryTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.Library);
}

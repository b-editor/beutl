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

    public LibraryTabViewModel(IEditorContext editorContext)
    {
        _ = editorContext;
        _lifetimeToken = _lifetimeCancellation.Token;

        IReadOnlyList<LibraryItem> libItems = LibraryService.Current.Items;
        LibraryItems = new List<LibraryItemViewModel>(libItems.Count);
        LibraryItems.AddRange(libItems.Select(x => LibraryItemViewModel.CreateFromLibraryItem(x)));

        IList<GraphNodeRegistry.BaseRegistryItem> nodes = GraphNodeRegistry.GetRegistered();
        Nodes = new List<LibraryItemViewModel>(nodes.Count);
        Nodes.AddRange(nodes.Select(x => LibraryItemViewModel.CreateFromGraphNodeRegistryItem(x)));

        AllItems = new(LibraryService.Current._totalCount + GraphNodeRegistry.s_totalCount);
        AddAllItems(LibraryItems);
        AddAllItems(Nodes);
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

    public List<LibraryItemViewModel> LibraryItems { get; }

    public List<LibraryItemViewModel> Nodes { get; }

    public List<KeyValuePair<int, LibraryItemViewModel>> AllItems { get; }

    public ReactiveCollection<KeyValuePair<int, LibraryItemViewModel>> SearchResult { get; } = [];


    public int SelectedTab { get; set; } = 2;

    [SuppressMessage("Performance", "CA1822:メンバーを static に設定します")]
    public CoreDictionary<string, LibraryTabDisplayMode> LibraryTabDisplayModes
        => GlobalConfiguration.Instance.EditorConfig.LibraryTabDisplayModes;

    private void AddAllItems(List<LibraryItemViewModel> items)
    {
        foreach (LibraryItemViewModel innerItem in items)
        {
            AllItems.Add(new(0, innerItem));
            AddAllItems(innerItem.Children);
        }
    }

    public async Task Search(string str, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        UsageTelemetry? usage = UsageTelemetry.Current;
        long epoch = 0;
        bool collect = usage?.TryGetCollectionEpoch(out epoch) == true;
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

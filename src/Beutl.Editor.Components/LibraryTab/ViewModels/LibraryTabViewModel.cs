using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Beutl.Animation.Easings;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.NodeTree;
using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.Editor.Components.LibraryTab.ViewModels;

public sealed class LibraryTabViewModel : IDisposable, IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly SemaphoreSlim _asyncLock = new(1, 1);

    public LibraryTabViewModel(IEditorContext editorContext)
    {
        _ = editorContext;

        IReadOnlyList<LibraryItem> libItems = LibraryService.Current.Items;
        LibraryItems = new List<LibraryItemViewModel>(libItems.Count);
        LibraryItems.AddRange(libItems.Select(x => LibraryItemViewModel.CreateFromOperatorRegistryItem(x)));

        IList<NodeRegistry.BaseRegistryItem> nodes = NodeRegistry.GetRegistered();
        Nodes = new List<LibraryItemViewModel>(nodes.Count);
        Nodes.AddRange(nodes.Select(x => LibraryItemViewModel.CreateFromNodeRegistryItem(x)));
        Nodes = new List<LibraryItemViewModel>();

        AllItems = new(LibraryService.Current._totalCount + NodeRegistry.s_totalCount);
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
        await _asyncLock.WaitAsync(cancellationToken);
        try
        {
            SearchResult.ClearOnScheduler();
            await Task.Run(() =>
            {
                Regex[] regices = RegexHelper.CreateRegexes(str);
                for (int i = 0; i < AllItems.Count; i++)
                {
                    KeyValuePair<int, LibraryItemViewModel> item = AllItems[i];
                    int score = item.Value.Match(regices);
                    if (score > 0)
                    {
                        SearchResult.OrderedAddDescendingOnScheduler(new(score, item.Value), x => x.Key);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SearchResult.ClearOnScheduler();
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    public void Dispose()
    {
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

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactiveProperty<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.LeftUpperTop);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public string Header => Strings.Library;
}

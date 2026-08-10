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

    public LibraryTabViewModel(IEditorContext editorContext)
    {
        _ = editorContext;

        IReadOnlyList<LibraryItem> libItems = LibraryService.Current.Items;
        LibraryItems = new List<LibraryItemViewModel>(libItems.Count);
        LibraryItems.AddRange(libItems.Select(x => LibraryItemViewModel.CreateFromLibraryItem(x)));

        IList<GraphNodeRegistry.BaseRegistryItem> nodes = GraphNodeRegistry.GetRegistered();
        Nodes = new List<LibraryItemViewModel>(nodes.Count);
        Nodes.AddRange(nodes.Select(x => LibraryItemViewModel.CreateFromGraphNodeRegistryItem(x)));

        AllItems = new(LibraryService.Current._totalCount + GraphNodeRegistry.s_totalCount);
        AddAllItems(LibraryItems);
        AddAllItems(Nodes);

        RebuildMaterials();
        InstalledMaterialService.Instance.Changed += OnInstalledMaterialsChanged;
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

    public ReactiveCollection<MaterialItemViewModel> Materials { get; } = [];

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
        InstalledMaterialService.Instance.Changed -= OnInstalledMaterialsChanged;
        _disposables.Dispose();
        Easings.Clear();
        LibraryItems.Clear();
        Nodes.Clear();
        AllItems.Clear();
        SearchResult.Clear();
        Materials.Clear();
    }

    private void OnInstalledMaterialsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RebuildMaterials);
    }

    private void RebuildMaterials()
    {
        Materials.Clear();

        foreach (IGrouping<string, InstalledMaterial> group in InstalledMaterialService.Instance.GetItems()
                     .GroupBy(x => x.PackageName, StringComparer.OrdinalIgnoreCase))
        {
            // A file sitting directly in {home}/materials belongs to no package and is
            // listed at the root instead of under a group.
            MaterialItemViewModel? package = null;
            if (!string.IsNullOrEmpty(group.Key))
            {
                package = new MaterialItemViewModel { DisplayName = group.Key };
                Materials.Add(package);
            }

            foreach (InstalledMaterial material in group)
            {
                var item = new MaterialItemViewModel
                {
                    DisplayName = material.Name,
                    FilePath = material.FilePath,
                    Description = material.FilePath,
                    Kind = material.Kind
                };

                if (package != null)
                {
                    package.Children.Add(item);
                }
                else
                {
                    Materials.Add(item);
                }
            }
        }
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

    public string Header => Strings.Library;
}

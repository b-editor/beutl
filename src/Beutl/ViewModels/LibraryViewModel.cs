using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Beutl.Animation.Easings;
using Beutl.Configuration;
using Beutl.NodeTree;
using Beutl.Services;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public class LibraryItemViewModel
{
    public required string DisplayName { get; init; }

    public required string FullDisplayName { get; init; }

    public string? Description { get; init; }

    public object? Data { get; init; }

    public string? Type { get; init; }

    public List<LibraryItemViewModel> Children { get; } = [];

    public static LibraryItemViewModel CreateFromNodeRegistryItem(NodeRegistry.BaseRegistryItem registryItem, string? parentFullName = null)
    {
        string? description = null;
        object? data = null;
        string? typeName = null;

        if (registryItem is NodeRegistry.RegistryItem draggable)
        {
            DisplayAttribute? att = draggable.Type.GetCustomAttribute<DisplayAttribute>();
            description = att?.GetDescription();
            data = draggable;
            typeName = draggable.Type.Name;
        }

        var obj = new LibraryItemViewModel()
        {
            DisplayName = registryItem.DisplayName,
            Description = description,
            Data = data,
            Type = Strings.NodeTree,
            FullDisplayName = parentFullName != null
                ? $"{parentFullName} / {registryItem.DisplayName}"
                : registryItem.DisplayName
        };

        if (registryItem is NodeRegistry.GroupableRegistryItem group)
        {
            obj.Children.AddRange(group.Items.Select(x => CreateFromNodeRegistryItem(x, obj.FullDisplayName)));
        }

        return obj;
    }

    public static LibraryItemViewModel CreateFromOperatorRegistryItem(LibraryItem registryItem, string? parentFullName = null)
    {
        var obj = new LibraryItemViewModel()
        {
            DisplayName = registryItem.DisplayName,
            Description = registryItem.Description,
            Data = registryItem,
            Type = CreateTypeString(registryItem),
            FullDisplayName = parentFullName != null
                ? $"{parentFullName} / {registryItem.DisplayName}"
                : registryItem.DisplayName
        };

        if (registryItem is GroupLibraryItem group)
        {
            obj.Children.AddRange(group.Items.Select(x => CreateFromOperatorRegistryItem(x, obj.FullDisplayName)));
        }

        return obj;
    }

    public IEnumerable<(string, Type)> TryDragDrop()
    {
        if (Data is LibraryItem libitem)
        {
            if (libitem is SingleTypeLibraryItem single)
                yield return (single.Format, single.ImplementationType);

            if (libitem is MultipleTypeLibraryItem multi)
            {
                foreach ((string s, Type t) in multi.Types)
                {
                    yield return (s, t);
                }
            }
        }
        else if (Data is NodeRegistry.RegistryItem regitem)
        {
            yield return (KnownLibraryItemFormats.Node, regitem.Type);
        }
    }

    public bool CanDragDrop()
    {
        return Data is SingleTypeLibraryItem or MultipleTypeLibraryItem or NodeRegistry.RegistryItem;
    }

    public int Match(Regex[] regexes)
    {
        // 配点は適当
        int result = 0;
        if (RegexHelper.IsMatch(regexes, DisplayName))
        {
            result += 100;
        }
        if (Description != null && RegexHelper.IsMatch(regexes, Description))
        {
            result += 50;
        }
        if (Type != null && RegexHelper.IsMatch(regexes, Type))
        {
            result++;
        }
        if (FullDisplayName != null && RegexHelper.IsMatch(regexes, FullDisplayName))
        {
            result++;
        }

        if (Data is SingleTypeLibraryItem single)
        {
            if (RegexHelper.IsMatch(regexes, single.ImplementationType.Name))
            {
                result += 75;
            }
        }
        else if (Data is MultipleTypeLibraryItem multi)
        {
            foreach (KeyValuePair<string, Type> item in multi.Types)
            {
                if (RegexHelper.IsMatch(regexes, item.Value.Name))
                {
                    result += 75;
                    break;
                }
            }
        }
        else if (Data is NodeRegistry.RegistryItem regitem)
        {
            if (RegexHelper.IsMatch(regexes, regitem.Type.Name))
            {
                result += 75;
            }
        }

        return result;
    }

    private static string CreateTypeString(LibraryItem item)
    {
        static string FormatToString(string str)
        {
            return str switch
            {
                // Todo: localize
                KnownLibraryItemFormats.Transform => Strings.Transform,
                KnownLibraryItemFormats.Sound => "Sound",
                KnownLibraryItemFormats.Geometry => "Geometry",
                KnownLibraryItemFormats.Drawable => "Drawable",
                KnownLibraryItemFormats.Brush => "Brush",
                KnownLibraryItemFormats.Easing => Strings.Easing,
                KnownLibraryItemFormats.FilterEffect => "FilterEffect",
                KnownLibraryItemFormats.Node => "Node",
                KnownLibraryItemFormats.SoundEffect => "Node",
                KnownLibraryItemFormats.SourceOperator => Strings.SourceOperators,
                _ => string.Empty,
            };
        }

        if (item is GroupLibraryItem)
        {
            return Strings.Group;
        }
        else if (item is SingleTypeLibraryItem single)
        {
            return FormatToString(single.Format);
        }
        else if (item is MultipleTypeLibraryItem multi)
        {
            string[] array = multi.Types.Keys
                .Select(FormatToString)
                .Where(v => !string.IsNullOrEmpty(v))
                .ToArray();
            Array.Sort(array);

            var sb = new StringBuilder(multi.Types.Count * 12);
            for (int i = 0; i < array.Length - 1; i++)
            {
                sb.Append(array[i]);
                sb.Append(" | ");
            }

            if (array.Length > 0)
                sb.Append(array[^1]);

            return sb.ToString();
        }
        else
        {
            return string.Empty;
        }
    }
}

public sealed class LibraryViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly Nito.AsyncEx.AsyncLock _asyncLock = new();

    public LibraryViewModel(EditViewModel editViewModel)
    {
        _ = editViewModel;

        IReadOnlyList<LibraryItem> libItems = LibraryService.Current.Items;
        LibraryItems = new List<LibraryItemViewModel>(libItems.Count);
        LibraryItems.AddRange(libItems.Select(x => LibraryItemViewModel.CreateFromOperatorRegistryItem(x)));

        IList<NodeRegistry.BaseRegistryItem> nodes = NodeRegistry.GetRegistered();
        Nodes = new List<LibraryItemViewModel>(nodes.Count);
        Nodes.AddRange(nodes.Select(x => LibraryItemViewModel.CreateFromNodeRegistryItem(x)));

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
        using (await _asyncLock.LockAsync(cancellationToken))
        {
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
}

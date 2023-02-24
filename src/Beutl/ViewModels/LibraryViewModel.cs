using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Beutl.NodeTree;
using Beutl.Operation;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public class LibraryItemViewModel
{
    public required string DisplayName { get; init; }

    public required string FullDisplayName { get; init; }

    public string? Description { get; init; }

    public IBrush? Brush { get; init; }

    public string? DataFormat { get; init; }

    public object? Data { get; init; }

    public string? Type { get; init; }

    public string? TypeName { get; init; }

    public List<LibraryItemViewModel> Children { get; } = new();

    public static LibraryItemViewModel CreateFromNodeRegistryItem(NodeRegistry.BaseRegistryItem registryItem, string? parentFullName = null)
    {
        string? description = null;
        string? format = null;
        object? data = null;
        string? typeName = null;

        if (registryItem is NodeRegistry.RegistryItem draggable)
        {
            DisplayAttribute? att = draggable.Type.GetCustomAttribute<DisplayAttribute>();
            description = att?.GetDescription();
            format = "Node";
            data = draggable;
            typeName = draggable.Type.Name;
        }

        var obj = new LibraryItemViewModel()
        {
            DisplayName = registryItem.DisplayName,
            Description = description,
            Brush = new ImmutableSolidColorBrush(registryItem.AccentColor.ToAvalonia()),
            DataFormat = format,
            Data = data,
            Type = Strings.NodeTree,
            TypeName = typeName,
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

    public static LibraryItemViewModel CreateFromOperatorRegistryItem(OperatorRegistry.BaseRegistryItem registryItem, string? parentFullName = null)
    {
        string? description = null;
        string? format = null;
        object? data = null;
        string? typeName = null;

        if (registryItem is OperatorRegistry.RegistryItem draggable)
        {
            DisplayAttribute? att = draggable.Type.GetCustomAttribute<DisplayAttribute>();
            description = att?.GetDescription();
            format = "SourceOperator";
            data = draggable;
            typeName = draggable.Type.Name;
        }

        var obj = new LibraryItemViewModel()
        {
            DisplayName = registryItem.DisplayName,
            Description = description,
            Brush = new ImmutableSolidColorBrush(registryItem.AccentColor.ToAvalonia()),
            DataFormat = format,
            Data = data,
            Type = Strings.SourceOperators,
            TypeName = typeName,
            FullDisplayName = parentFullName != null
                ? $"{parentFullName} / {registryItem.DisplayName}"
                : registryItem.DisplayName
        };

        if (registryItem is OperatorRegistry.GroupableRegistryItem group)
        {
            obj.Children.AddRange(group.Items.Select(x => CreateFromOperatorRegistryItem(x, obj.FullDisplayName)));
        }

        return obj;
    }

    public bool TryDragDrop([NotNullWhen(true)] out string? format, [NotNullWhen(true)] out object? data)
    {
        if (DataFormat != null && Data != null)
        {
            data = Data;
            format = DataFormat;
            return true;
        }
        else
        {
            data = null;
            format = null;
            return false;
        }
    }

    public bool CanDragDrop()
    {
        return DataFormat != null && Data != null;
    }

    public int Match(Regex[] regexes)
    {
        int result = 0;
        if (RegexHelper.IsMatch(regexes, DisplayName))
        {
            result += 100;
        }
        if (TypeName != null && RegexHelper.IsMatch(regexes, TypeName))
        {
            result += 75;
        }
        if (Description != null && RegexHelper.IsMatch(regexes, Description))
        {
            result += 50;
        }
        if (DataFormat != null && RegexHelper.IsMatch(regexes, DataFormat))
        {
            result++;
        }
        if (Type != null && RegexHelper.IsMatch(regexes, Type))
        {
            result++;
        }
        if (FullDisplayName != null && RegexHelper.IsMatch(regexes, FullDisplayName))
        {
            result++;
        }

        return result;
    }
}

public class LibraryViewModel
{
    private readonly EditViewModel _editViewModel;
    private readonly Nito.AsyncEx.AsyncLock _asyncLock = new();

    public LibraryViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        IList<OperatorRegistry.BaseRegistryItem> ops = OperatorRegistry.GetRegistered();
        Operators = new List<LibraryItemViewModel>(ops.Count);
        Operators.AddRange(ops.Select(x => LibraryItemViewModel.CreateFromOperatorRegistryItem(x)));

        IList<NodeRegistry.BaseRegistryItem> nodes = NodeRegistry.GetRegistered();
        Nodes = new List<LibraryItemViewModel>(nodes.Count);
        Nodes.AddRange(nodes.Select(x => LibraryItemViewModel.CreateFromNodeRegistryItem(x)));

        AllItems = new(OperatorRegistry.s_totalCount + NodeRegistry.s_totalCount);
        AddAllItems(Operators);
        AddAllItems(Nodes);
    }

    public List<LibraryItemViewModel> Operators { get; }

    public List<LibraryItemViewModel> Nodes { get; }

    public List<KeyValuePair<int, LibraryItemViewModel>> AllItems { get; }

    public ReactiveCollection<KeyValuePair<int, LibraryItemViewModel>> SearchResult { get; } = new();

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
                    Regex[] regices = RegexHelper.CreateRegices(str);
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
}

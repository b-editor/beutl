using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;

using Avalonia.Input;

using Beutl.Editor.Components.Helpers;
using Beutl.NodeGraph;
using Beutl.Services;

namespace Beutl.Editor.Components.LibraryTab.ViewModels;

public class LibraryItemViewModel
{
    public required string DisplayName { get; init; }

    public required string FullDisplayName { get; init; }

    public string? Description { get; init; }

    public object? Data { get; init; }

    public string? Type { get; init; }

    public CoreList<LibraryItemViewModel> Children { get; } = [];

    public static LibraryItemViewModel CreateFromGraphNodeRegistryItem(GraphNodeRegistry.BaseRegistryItem registryItem,
        string? parentFullName = null)
    {
        string? description = null;
        object? data = null;
        string? typeName = null;

        if (registryItem is GraphNodeRegistry.RegistryItem draggable)
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
            Type = Strings.NodeGraph,
            FullDisplayName = ComposeFullName(parentFullName, registryItem.DisplayName)
        };

        if (registryItem is GraphNodeRegistry.GroupableRegistryItem group)
        {
            obj.Children.AddRange(group.Items.Select(x => CreateFromGraphNodeRegistryItem(x, obj.FullDisplayName)));
        }

        return obj;
    }

    public static LibraryItemViewModel CreateFromLibraryItem(LibraryItem registryItem,
        string? parentFullName = null)
    {
        var obj = new LibraryItemViewModel()
        {
            DisplayName = registryItem.DisplayName,
            Description = registryItem.Description,
            Data = registryItem,
            Type = CreateTypeString(registryItem),
            FullDisplayName = ComposeFullName(parentFullName, registryItem.DisplayName)
        };

        if (registryItem is GroupLibraryItem group)
        {
            obj.Children.AddRange(group.Items.Select(x => CreateFromLibraryItem(x, obj.FullDisplayName)));
        }

        return obj;
    }

    private static string ComposeFullName(string? parentFullName, string displayName)
    {
        return parentFullName != null
            ? $"{parentFullName} / {displayName}"
            : displayName;
    }

    public IEnumerable<(DataFormat<string>, Type)> TryDragDrop()
    {
        if (Data is LibraryItem libitem)
        {
            if (libitem is SingleTypeLibraryItem single)
                yield return (DataFormat.CreateStringApplicationFormat(single.Format), single.ImplementationType);

            if (libitem is MultipleTypeLibraryItem multi)
            {
                foreach ((string s, Type t) in multi.Types)
                {
                    yield return (DataFormat.CreateStringApplicationFormat(s), t);
                }
            }
        }
        else if (Data is GraphNodeRegistry.RegistryItem regitem)
        {
            yield return (BeutlDataFormats.GraphNode, regitem.Type);
        }
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

        if (RegexHelper.IsMatch(regexes, FullDisplayName))
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
        else if (Data is GraphNodeRegistry.RegistryItem regitem)
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
                KnownLibraryItemFormats.Transform => GraphicsStrings.Transform,
                KnownLibraryItemFormats.Sound => "Sound",
                KnownLibraryItemFormats.Geometry => "Geometry",
                KnownLibraryItemFormats.Drawable => GraphicsStrings.Drawable,
                KnownLibraryItemFormats.Brush => GraphicsStrings.Brush,
                KnownLibraryItemFormats.Easing => Strings.Easing,
                KnownLibraryItemFormats.FilterEffect => GraphicsStrings.FilterEffect,
                KnownLibraryItemFormats.GraphNode => Strings.NodeGraph,
                KnownLibraryItemFormats.AudioEffect => AudioStrings.AudioEffect,
                KnownLibraryItemFormats.EngineObject => Strings.Object,
                KnownLibraryItemFormats.ClipTransition => GraphicsStrings.ClipTransition,
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
            return string.Join(" | ", array);
        }
        else
        {
            return string.Empty;
        }
    }
}

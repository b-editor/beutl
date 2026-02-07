using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Avalonia.Input;

using Beutl.Editor.Components.Helpers;
using Beutl.NodeTree;
using Beutl.Services;

namespace Beutl.Editor.Components.LibraryTab.ViewModels;

public class LibraryItemViewModel
{
    public required string DisplayName { get; init; }

    public required string FullDisplayName { get; init; }

    public string? Description { get; init; }

    public object? Data { get; init; }

    public string? Type { get; init; }

    public List<LibraryItemViewModel> Children { get; } = [];

    public static LibraryItemViewModel CreateFromNodeRegistryItem(NodeRegistry.BaseRegistryItem registryItem,
        string? parentFullName = null)
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

    public static LibraryItemViewModel CreateFromOperatorRegistryItem(LibraryItem registryItem,
        string? parentFullName = null)
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

    public IEnumerable<(DataFormat<string>, Type)> TryDragDrop()
    {
        if (Data is LibraryItem libitem)
        {
            if (libitem is SingleTypeLibraryItem single)
                yield return (GetKnownDataFormat(single.Format), single.ImplementationType);

            if (libitem is MultipleTypeLibraryItem multi)
            {
                foreach ((string s, Type t) in multi.Types)
                {
                    yield return (GetKnownDataFormat(s), t);
                }
            }
        }
        else if (Data is NodeRegistry.RegistryItem regitem)
        {
            yield return (BeutlDataFormats.Node, regitem.Type);
        }
    }

    private static DataFormat<string> GetKnownDataFormat(string format)
    {
        return format switch
        {
            KnownLibraryItemFormats.Easing => BeutlDataFormats.Easing,
            KnownLibraryItemFormats.Transform => BeutlDataFormats.Transform,
            KnownLibraryItemFormats.Sound => BeutlDataFormats.Sound,
            KnownLibraryItemFormats.Geometry => BeutlDataFormats.Geometry,
            KnownLibraryItemFormats.Drawable => BeutlDataFormats.Drawable,
            KnownLibraryItemFormats.Brush => BeutlDataFormats.Brush,
            KnownLibraryItemFormats.FilterEffect => BeutlDataFormats.FilterEffect,
            KnownLibraryItemFormats.Node => BeutlDataFormats.Node,
            KnownLibraryItemFormats.AudioEffect => BeutlDataFormats.AudioEffect,
            KnownLibraryItemFormats.SourceOperator => BeutlDataFormats.SourceOperator,
            _ => DataFormat.CreateStringApplicationFormat(format)
        };
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
                KnownLibraryItemFormats.AudioEffect => "Node",
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

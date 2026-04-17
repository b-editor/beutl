using Beutl.Editor.Components.Helpers;
using Beutl.PropertyAdapters;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

internal static class GroupedEditorHelper
{
    public static bool TryPasteJson<TItem>(
        string json,
        BaseEditorViewModel<TItem?> vm,
        ReactivePropertySlim<bool> isExpanded,
        IList<TItem>? groupChildren)
        where TItem : class, ICoreObject
    {
        if (!CoreObjectClipboard.TryDeserializeJson<TItem>(json, out var pasted)) return false;

        isExpanded.Value = true;
        if (groupChildren != null)
        {
            groupChildren.Add(pasted);
        }
        else if (vm.EditingKeyFrame.Value is { } kf)
        {
            kf.Value = pasted;
        }
        else if (vm.PropertyAdapter is ListItemAccessorImpl<TItem> listItemAccessor)
        {
            listItemAccessor.List.Insert(listItemAccessor.Index, pasted);
        }
        else
        {
            vm.PropertyAdapter.SetValue(pasted);
        }

        vm.Commit(CommandNames.PasteObject);
        return true;
    }

    public static bool ApplyTemplate<TItem>(
        ObjectTemplateItem template,
        BaseEditorViewModel<TItem?> vm,
        ReactivePropertySlim<bool> isExpanded,
        bool isGroup,
        Action<TItem> addItem,
        Action<TItem> changeItem)
        where TItem : class
    {
        if (template.CreateInstance() is not TItem instance) return false;
        isExpanded.Value = true;
        if (isGroup)
            addItem(instance);
        else
            changeItem(instance);
        vm.Commit(CommandNames.ApplyTemplate);
        return true;
    }
}

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
        // Adding to a group or inserting into a list is an ordinary edit. Replacing the value is how
        // content that could not be loaded is repaired, so SetValue checks the value it replaces.
        bool inserts = groupChildren != null
                       || (vm.EditingKeyFrame.Value is null && vm.PropertyAdapter is ListItemAccessorImpl<TItem>);
        if (!(inserts ? vm.IsElementEditable : vm.CanReplaceContent)) return false;
        if (!CoreObjectClipboard.TryDeserializeJson<TItem>(json, out var pasted)) return false;

        isExpanded.Value = true;
        if (groupChildren != null)
        {
            groupChildren.Add(pasted);
        }
        else if (vm.EditingKeyFrame.Value is { } kf)
        {
            vm.SetValue(kf.Value, pasted, CommandNames.PasteObject);
            return true;
        }
        else if (vm.PropertyAdapter is ListItemAccessorImpl<TItem> listItemAccessor)
        {
            listItemAccessor.List.Insert(listItemAccessor.Index, pasted);
        }
        else
        {
            vm.SetValue(vm.PropertyAdapter.GetValue(), pasted, CommandNames.PasteObject);
            return true;
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
        // A group adds the template, an ordinary edit; otherwise changeItem replaces the value.
        if (!(isGroup ? vm.IsElementEditable : vm.CanReplaceContent)) return false;
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

using Beutl.Language;
using Beutl.NodeGraph.Nodes;
using Beutl.Services;
using static Beutl.NodeGraph.GraphNodeRegistry;

namespace Beutl.Editor.Services;

internal static class NodeMenuCatalog
{
    public static IList<BaseRegistryItem> GetItems(LibraryService? library)
    {
        IList<BaseRegistryItem> items = GetRegistered();
        if (items.OfType<GroupableRegistryItem>().FirstOrDefault(group => group.Id == NodesRegistrar.FilterEffectGroupId)
            is { } effects)
        {
            GroupFilterEffects(effects, library);
        }

        return items;
    }

    private static void GroupFilterEffects(GroupableRegistryItem effects, LibraryService? library)
    {
        Dictionary<LibraryItem, LibraryEffectCategory?> categories = library == null
            ? []
            : LibraryEffectCategories.Collect(library.Items, KnownLibraryItemFormats.FilterEffect);
        GroupableRegistryItem[] sections = categories.Values.OfType<LibraryEffectCategory>()
            .DistinctBy(category => category.Key)
            .Select(category => new GroupableRegistryItem(category.Title, effects.AccentColor) { Id = category.Id })
            .ToArray();
        var sectionsByKey = sections.ToDictionary(section => LibraryCategoryKey.Create(section.Id, section.DisplayName));
        var other = new GroupableRegistryItem(NodeGraphStrings.OtherEffects, effects.AccentColor)
        {
            Id = NodesRegistrar.FilterEffectGroupId + ".Other"
        };

        // The registry returns a detached tree. Move the effect wrappers out of their old positions in it.
        effects.Items.RemoveAll(MoveEffectNode);
        effects.Items.InsertRange(0, sections.Where(section => section.Items.Count > 0));
        if (other.Items.Count > 0)
            effects.Items.Add(other);

        bool MoveEffectNode(BaseRegistryItem item)
        {
            if (item is GroupableRegistryItem group)
            {
                group.Items.RemoveAll(MoveEffectNode);
                return group.Items.Count == 0;
            }

            if (item is not RegistryItem node || GetEffectType(node.Type) is not { } effectType)
                return false;

            LibraryItem? effect = library?.FindItem(effectType);
            LibraryEffectCategory? category = effect == null ? null : categories.GetValueOrDefault(effect);
            GroupableRegistryItem section = category != null ? sectionsByKey[category.Key] : other;
            section.Items.Add(node);
            return true;
        }
    }

    private static Type? GetEffectType(Type nodeType)
    {
        for (Type? type = nodeType; type != null; type = type.BaseType)
        {
            if (type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(FilterEffectNode<>))
                return type.GenericTypeArguments[0];
        }

        return null;
    }
}

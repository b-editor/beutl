using Beutl.NodeGraph.Nodes.Group;
using Beutl.Serialization;

namespace Beutl.Editor.Services;

public static class GroupNodeTemplateExtensions
{
    /// <summary>
    /// A new group node built from the template, with identifiers of its own: regenerated as
    /// elements are when they are duplicated, so references inside it follow the copy.
    /// </summary>
    public static GroupNode? Instantiate(this GroupNodeTemplates templates, GroupNodeTemplate template)
    {
        ArgumentNullException.ThrowIfNull(templates);
        if (templates.Load(template) is not { } saved)
            return null;

        ObjectRegenerator.Regenerate(saved, typeof(GroupNode), out ICoreSerializable copy);
        var group = (GroupNode)copy;
        GroupNodeTemplates.RenewRequestKeys(group);
        return group;
    }
}

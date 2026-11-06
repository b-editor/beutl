using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;

namespace Beutl.Editor.Components.Helpers;

// Adds to the group a property holds. A value that is no group goes into a new group first, so nothing is replaced.
public static class AddToGroupHelper
{
    public static void Add(IProperty<FilterEffect?> property, FilterEffect item)
    {
        GetOrCreateGroup<FilterEffect, FilterEffectGroup>(property, group => group.Children).Children.Add(item);
    }

    public static void Add(IProperty<Transform?> property, Transform item)
    {
        TransformGroup group = GetOrCreateGroup<Transform, TransformGroup>(property, group => group.Children);
        group.Children.Insert(CanonicalTransformLayout.GetInsertionIndex(group.Children, item), item);
    }

    private static TGroup GetOrCreateGroup<T, TGroup>(IProperty<T?> property, Func<TGroup, IListProperty<T>> childrenOf)
        where T : EngineObject
        where TGroup : T, new()
    {
        if (property.Value is TGroup existingGroup) return existingGroup;

        T? existing = property.Value;
        var group = new TGroup();
        // The group takes the value's place first. Added while the property still owned it, the value would stay in
        // the group's children but lose its parent when the property released it.
        property.Value = group;
        if (existing != null)
        {
            childrenOf(group).Add(existing);
        }

        return group;
    }
}

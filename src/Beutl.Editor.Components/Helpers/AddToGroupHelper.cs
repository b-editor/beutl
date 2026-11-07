using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;

namespace Beutl.Editor.Components.Helpers;

// Adds to the group a property holds. A value that is no group goes into a new group first, so nothing is replaced.
public static class AddToGroupHelper
{
    public static void Add(IProperty<FilterEffect?> property, FilterEffect item)
    {
        FilterEffectGroup group = EngineObjectGroups.GetOrCreateGroup<FilterEffect, FilterEffectGroup>(
            property.Value, group => property.Value = group, group => group.Children, out _);
        group.Children.Add(item);
    }

    public static void Add(IProperty<Transform?> property, Transform item)
    {
        TransformGroup group = EngineObjectGroups.GetOrCreateGroup<Transform, TransformGroup>(
            property.Value, group => property.Value = group, group => group.Children, out _);
        group.Children.Insert(CanonicalTransformLayout.GetInsertionIndex(group.Children, item), item);
    }
}

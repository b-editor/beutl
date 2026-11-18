using Beutl.Engine;

namespace Beutl.Graphics.Effects;

// Which filter effects act on what one effect gives: the later ones in each group around it, and the effects of the
// drawables around the drawable it belongs to, which act on what that drawable draws. A group among them comes with
// everything in it, as it draws each of its children as an effect of its own.
internal static class FilterEffectOrder
{
    public static HashSet<EngineObject> After(FilterEffect effect)
    {
        var after = new HashSet<EngineObject>();
        IHierarchical current = effect;
        while (current.HierarchicalParent is { } parent)
        {
            switch (parent)
            {
                case FilterEffectGroup group when current is FilterEffect child:
                    for (int i = group.Children.IndexOf(child) + 1; i > 0 && i < group.Children.Count; i++)
                        AddWithChildren(after, group.Children[i]);
                    break;

                // A drawable's own effect holds the chain; around a drawable inside it, it acts on that drawable too.
                case Drawable drawable when drawable.FilterEffect.Value is { } outer && !ReferenceEquals(outer, current):
                    AddWithChildren(after, outer);
                    break;
            }

            current = parent;
        }

        return after;
    }

    private static void AddWithChildren(HashSet<EngineObject> after, FilterEffect effect)
    {
        after.Add(effect);
        if (effect is FilterEffectGroup group)
        {
            foreach (FilterEffect child in group.Children)
                AddWithChildren(after, child);
        }
    }
}

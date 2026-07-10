using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Group), ResourceType = typeof(GraphicsStrings))]
public sealed partial class FilterEffectGroup : FilterEffect
{
    public FilterEffectGroup()
    {
        ScanProperties<FilterEffectGroup>();
    }

    public IListProperty<FilterEffect> Children { get; } = Property.CreateList<FilterEffect>();

    // Describe each child into the same builder, mirroring how ApplyTo concatenates them into one context. Without
    // this a bridged group would hide its migrated children behind a single opaque pass and they could never fuse.
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        // Bracket each child's descriptors with its index so the compiler can attribute passes to children (C10
        // provenance), enabling the pass-prefix output cache to reuse a stable leading run of children. A disabled
        // child appends nothing but keeps its provenance index i (never renumbered), so indices stay aligned to the
        // group's child list the prefix cache tracks.
        for (int i = 0; i < r.Children.Count; i++)
        {
            FilterEffect.Resource item = r.Children[i];
            if (!item.IsEnabled)
                continue;

            using (builder.BeginChildScope(i))
            {
                item.GetOriginal().Describe(builder, item);
            }
        }
    }
}

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

/// <summary>
/// Elements kept together outside a scene, with the groups among them: what a template saved from several
/// elements holds.
/// </summary>
/// <remarks>
/// Not a <see cref="Hierarchical"/>, so its elements can join a scene without leaving it first.
/// </remarks>
[Display(Name = nameof(Strings.MultipleElements), ResourceType = typeof(Strings))]
public sealed partial class ElementSet : CoreObject
{
    public ElementSet()
    {
        ScanProperties<ElementSet>();
    }

    public IListProperty<Element> Elements { get; } = new ListProperty<Element> { AffectsRender = false };

    // Serialize and Deserialize write and read the groups themselves, as Scene does.
    [NotAutoSerialized]
    public IListProperty<ImmutableHashSet<Guid>> Groups { get; } = new ListProperty<ImmutableHashSet<Guid>> { AffectsRender = false };

    // The frame the elements were laid out in, or default when it is not known.
    public IProperty<PixelSize> FrameSize { get; } = Property.CreateValue<PixelSize>(default);

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);

        context.SetValue(nameof(Groups), ElementGroups.Write(Groups));
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        var elementIds = Elements.Select(element => element.Id.Value).ToHashSet();
        Groups.Clear();
        foreach (ImmutableHashSet<Guid> ids in ElementGroups.Read(context.GetValue<string[]>(nameof(Groups)), elementIds))
        {
            Groups.Add(ids);
        }
    }
}

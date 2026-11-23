using Beutl.Animation;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

// The keyframes and expressions of an element's objects, nested objects, separated components and node ports included,
// found by walking that element alone.
internal static class ElementAnimations
{
    // A property the timeline can show an inline layer for, as the "Animation" menu of a clip lists it.
    internal sealed record Entry(string Label, IAnimatablePropertyAdapter Adapter);

    // The keyframe times from the element's start, sorted and each once, and whether any property follows an
    // expression. watch hears of every animation that has keyframes.
    internal static ElementAnimationMarks ReadMarks(Element element, Action<IKeyFrameAnimation>? watch = null)
    {
        MediaTime start = element.Start.Value;
        var times = new List<MediaTime>();
        bool hasKeyFrames = false;
        bool hasExpression = Walk(element, (animation, _, _) =>
        {
            hasKeyFrames = true;
            watch?.Invoke(animation);
            MediaTime origin = AnimationOperations.GetTimeOrigin(animation);
            foreach (IKeyFrame key in animation.KeyFrames)
            {
                times.Add(origin + key.KeyTime.Value - start);
            }
        });

        times.Sort();
        int count = 0;
        for (int i = 0; i < times.Count; i++)
        {
            if (count == 0 || times[i] != times[count - 1])
                times[count++] = times[i];
        }

        times.RemoveRange(count, times.Count - count);
        return new ElementAnimationMarks(hasKeyFrames, hasExpression, [.. times]);
    }

    // The properties with keyframes, in the order the property tab shows them. Two properties that stand as one are one
    // entry, as their inline layer is.
    internal static List<Entry> ReadEntries(Element element)
    {
        var entries = new List<Entry>();
        var pairs = new HashSet<PropertyPair>();
        Walk(element, (animation, property, adapter) =>
        {
            if (adapter != null)
            {
                entries.Add(new Entry(Label(animation, adapter.DisplayName), adapter));
                return;
            }

            if (PropertyPair.Find(property) is { } pair)
            {
                if (!pair.IsSeparated.Value)
                {
                    if (pairs.Add(pair))
                        entries.Add(new Entry(Label(animation, pair.DisplayName), pair));
                    return;
                }

                int index = IndexOf(pair.Properties, property!);
                if (index >= 0)
                {
                    IAnimatablePropertyAdapter member = pair.Adapters[index];
                    entries.Add(new Entry(Label(animation, member.DisplayName), member));
                    return;
                }
            }

            if (property!.GetOwnerObject() is { } owner
                && PropertyAdapterFactory.CreateAdapter(property, owner) is IAnimatablePropertyAdapter created)
            {
                entries.Add(new Entry(Label(animation, created.DisplayName), created));
            }
        });

        return entries;
    }

    // Calls found for each animation with keyframes once, with the property that holds it or, for a node's port, the
    // adapter that does, and returns whether a property has an expression the user wrote.
    private static bool Walk(Element element, Action<IKeyFrameAnimation, IProperty?, IAnimatablePropertyAdapter?> found)
    {
        var walker = new Walker(found);
        foreach (EngineObject obj in element.Objects)
        {
            walker.Visit(obj);
        }

        return walker.HasExpression;
    }

    // Goes below each object along ChildObjects once, through an engine object's properties in the order its panel
    // shows them, then the rest. Keyframes are not walked into, as nothing below them holds an animation.
    private sealed class Walker(Action<IKeyFrameAnimation, IProperty?, IAnimatablePropertyAdapter?> found)
    {
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<IKeyFrameAnimation> _animations = new(ReferenceEqualityComparer.Instance);

        public bool HasExpression { get; private set; }

        public void Visit(object obj)
        {
            if (obj is IKeyFrameAnimation || (!obj.GetType().IsValueType && !_visited.Add(obj))) return;

            switch (obj)
            {
                case IExpressionProperty property:
                    if (property.GetKeyFrameAnimation() is { } animation && _animations.Add(animation))
                        found(animation, property, null);

                    HasExpression |= HasUserExpression(property);
                    break;
                case IAnimatablePropertyAdapter { Animation: { } adapterAnimation } adapter
                    when _animations.Add(adapterAnimation):
                    found(adapterAnimation, null, adapter);
                    break;
            }

            if (obj is EngineObject engineObject)
            {
                foreach (IProperty property in engineObject.GetDisplayProperties())
                {
                    Visit(property);
                }
            }

            foreach (object child in ChildObjects.Of(obj))
            {
                Visit(child);
            }
        }
    }

    // An expression the user wrote, which the value follows over time. A reference to another object and a node's
    // connection are not one.
    private static bool HasUserExpression(IExpressionProperty property)
    {
        if (property.IsReference) return false;

        for (IExpression? expression = property.Expression; expression != null; expression = expression.Replaced)
        {
            if (expression is IPropertyAnimation) return false;
            if (expression is not (INodePortExpression or IReferenceExpression)) return true;
        }

        return false;
    }

    // "Object › Property", with the property whose components were separated before a component, and a node's name
    // for one of its ports.
    private static string Label(IKeyFrameAnimation animation, string propertyName)
    {
        var parts = new List<string>(3) { propertyName };
        object? owner = animation.HierarchicalParent;
        if (owner is IComponentAnimation components && components.HierarchicalParent is CoreObject holder)
        {
            if (holder.Properties.FirstOrDefault(p => ReferenceEquals(p.GetPropertyAnimation(), components)) is { } separated)
                parts.Insert(0, Property.GetLocalizedName(separated));
            owner = holder;
        }

        if (owner is INodeMember member)
            owner = member.HierarchicalParent ?? owner;

        if (owner is EngineObject obj)
        {
            parts.Insert(0, string.IsNullOrWhiteSpace(obj.Name.Value)
                ? TypeDisplayHelpers.GetLocalizedName(obj.GetType())
                : obj.Name.Value);
        }

        return string.Join(" › ", parts);
    }

    private static int IndexOf(IReadOnlyList<IProperty> properties, IProperty property)
    {
        for (int i = 0; i < properties.Count; i++)
        {
            if (ReferenceEquals(properties[i], property)) return i;
        }

        return -1;
    }
}

// What a clip shows of its objects' animations.
public sealed class ElementAnimationMarks(bool hasKeyFrames, bool hasExpression, MediaTime[] times)
    : IEquatable<ElementAnimationMarks>
{
    public static ElementAnimationMarks None { get; } = new(false, false, []);

    // Whether a property has keyframes.
    public bool HasKeyFrames { get; } = hasKeyFrames;

    // Whether a property follows an expression the user wrote.
    public bool HasExpression { get; } = hasExpression;

    public bool HasAny => HasKeyFrames || HasExpression;

    // The keyframe times from the element's start, sorted and each once.
    public IReadOnlyList<MediaTime> Times { get; } = times;

    public bool Equals(ElementAnimationMarks? other)
        => other is not null
           && HasKeyFrames == other.HasKeyFrames
           && HasExpression == other.HasExpression
           && Times.SequenceEqual(other.Times);

    public override bool Equals(object? obj) => Equals(obj as ElementAnimationMarks);

    public override int GetHashCode() => HashCode.Combine(HasKeyFrames, HasExpression, Times.Count);
}

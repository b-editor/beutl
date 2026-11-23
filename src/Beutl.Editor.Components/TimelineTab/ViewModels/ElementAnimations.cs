using System.Collections;
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
    // The keyframe times from the element's start, sorted and each once, and whether any property follows an
    // expression. watch hears of every animation that has keyframes.
    internal static ElementAnimationMarks ReadMarks(Element element, Action<IKeyFrameAnimation>? watch = null)
    {
        MediaTime start = element.Start.Value;
        var times = new List<MediaTime>();
        bool hasKeyFrames = false;
        var walker = new Walker(animation =>
        {
            hasKeyFrames = true;
            watch?.Invoke(animation);
            MediaTime origin = AnimationOperations.GetTimeOrigin(animation);
            foreach (IKeyFrame key in animation.KeyFrames)
            {
                times.Add(origin + key.KeyTime.Value - start);
            }
        });
        foreach (EngineObject obj in element.Objects)
        {
            walker.Visit(obj);
        }

        times.Sort();
        int count = 0;
        for (int i = 0; i < times.Count; i++)
        {
            if (count == 0 || times[i] != times[count - 1])
                times[count++] = times[i];
        }

        times.RemoveRange(count, times.Count - count);
        return new ElementAnimationMarks(hasKeyFrames, walker.HasExpression, [.. times]);
    }

    // The rows of the clip's "Animation" menu: the element's objects, and below each the properties with keyframes, nested
    // as the graph editor's tree nests them. A property that holds an object has that object's properties directly
    // below it, two properties that stand as one are one row, and an object or property without keyframes below it has
    // no row.
    internal static List<AnimationMenuItem> ReadMenu(Element element)
    {
        var roots = new List<AnimationMenuItem>();
        var builder = new MenuBuilder();
        foreach (EngineObject obj in element.Objects)
        {
            builder.AppendValue(obj, roots);
        }

        return roots;
    }

    private sealed class MenuBuilder
    {
        private readonly HashSet<object> _ancestors = new(ReferenceEqualityComparer.Instance);
        // A property can be reached on two paths, such as a node's nested port and the object its parent port holds;
        // it has a toggle on the first only.
        private readonly HashSet<object> _listed = new(ReferenceEqualityComparer.Instance);

        public void AppendValue(object? value, List<AnimationMenuItem> result)
        {
            value = ChildObjects.Unwrap(value);
            if (value is null or string or IKeyFrameAnimation || !_ancestors.Add(value)) return;

            try
            {
                switch (value)
                {
                    // A node's port is a property's row too, over the value its adapter holds.
                    case INodeMember { Property: { } adapter }:
                        AddIfAnimated(result, ValueItem(adapter.DisplayName,
                            adapter is IAnimatablePropertyAdapter { Animation: not null } animated ? animated : null,
                            adapter.GetEngineProperty(), adapter.GetValue()));
                        break;
                    case EngineObject obj:
                        var item = new AnimationMenuItem(string.IsNullOrWhiteSpace(obj.Name.Value)
                            ? TypeDisplayHelpers.GetLocalizedName(obj.GetType())
                            : obj.Name.Value);
                        AppendObjectChildren(obj, item.Children);
                        AddIfAnimated(result, item);
                        break;
                    case CoreObject core:
                        AppendCoreChildren(core, result);
                        break;
                    case IEnumerable list:
                        foreach (object? child in list)
                        {
                            AppendValue(child, result);
                        }

                        break;
                }
            }
            finally
            {
                _ancestors.Remove(value);
            }
        }

        private void AppendObjectChildren(EngineObject obj, List<AnimationMenuItem> result)
        {
            foreach (IProperty property in obj.GetDisplayProperties())
            {
                // The items of a group stand in its place.
                if (property is IListProperty { Name: "Children" } list && typeof(EngineObject).IsAssignableFrom(list.ElementType))
                {
                    AppendValue(list, result);
                    continue;
                }

                if (PropertyPair.Find(property) is { } pair)
                {
                    // The row of the first property stands for both.
                    if (ReferenceEquals(property, pair.Properties[0]))
                        AddIfAnimated(result, PairItem(pair));
                    continue;
                }

                AddIfAnimated(result, ValueItem(Property.GetLocalizedName(property),
                    property.GetKeyFrameAnimation() != null
                        ? PropertyAdapterFactory.CreateAdapter(property, obj) as IAnimatablePropertyAdapter
                        : null,
                    property, property.Value));
            }

            AppendCoreChildren(obj, result);
        }

        private void AppendCoreChildren(CoreObject obj, List<AnimationMenuItem> result)
        {
            foreach (IProperty field in obj.Properties.Where(obj.IsBaseField))
            {
                if (field.ValueType.IsValueType || field.ValueType == typeof(string)) continue;
                AppendValue(field.Value, result);
            }
        }

        // The row of a property or a node's port: its toggle while it has keyframes, the toggles of its separated
        // components, and the rows of the object it holds.
        private AnimationMenuItem ValueItem(string label, IAnimatablePropertyAdapter? toggle, IProperty? property, object? value)
        {
            var item = new AnimationMenuItem(label, Listed(toggle));

            // Each separated component has keyframes of its own.
            if (property?.GetPropertyAnimation() is IComponentAnimation components and CoreObject holder)
            {
                foreach (IProperty component in components.Components)
                {
                    if (component.GetKeyFrameAnimation() != null
                        && Listed(PropertyAdapterFactory.CreateAdapter(component, holder) as IAnimatablePropertyAdapter)
                            is { } adapter)
                    {
                        item.Children.Add(new AnimationMenuItem(adapter.DisplayName, adapter));
                    }
                }

                return item;
            }

            // A reference holds another object, whose keyframes are not this element's.
            if (property is IExpressionProperty { IsReference: true }) return item;

            value = ChildObjects.Unwrap(value);
            if (value is EngineObject nested and not INodeMember && _ancestors.Add(nested))
            {
                try
                {
                    // A property that holds an object (a fill, a transform) is that object's row.
                    AppendObjectChildren(nested, item.Children);
                }
                finally
                {
                    _ancestors.Remove(nested);
                }
            }
            else
            {
                AppendValue(value, item.Children);
            }

            return item;
        }

        // One row while the properties stand as one, and a row for each with keyframes below the pair's row otherwise.
        private AnimationMenuItem PairItem(PropertyPair pair)
        {
            if (!pair.IsSeparated.Value)
                return new AnimationMenuItem(pair.DisplayName, pair.Animation != null ? Listed(pair) : null);

            var item = new AnimationMenuItem(pair.DisplayName);
            foreach (IAnimatablePropertyAdapter adapter in pair.Adapters)
            {
                if (adapter.Animation != null && Listed(adapter) is { } member)
                    item.Children.Add(new AnimationMenuItem(member.DisplayName, member));
            }

            return item;
        }

        // The property's toggle, or null when another row already has one for it.
        private IAnimatablePropertyAdapter? Listed(IAnimatablePropertyAdapter? adapter)
            => adapter != null && _listed.Add(adapter.GetEngineProperty() ?? (object)adapter) ? adapter : null;

        private static void AddIfAnimated(List<AnimationMenuItem> result, AnimationMenuItem item)
        {
            if (item.Adapter != null || item.Children.Count > 0)
                result.Add(item);
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

    // Goes below each object along ChildObjects once, through an engine object's properties in the order its panel
    // shows them, then the rest, and calls found for each animation with keyframes once. Keyframes are not walked into,
    // as nothing below them holds an animation.
    private sealed class Walker(Action<IKeyFrameAnimation> found)
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
                        found(animation);

                    HasExpression |= HasUserExpression(property);
                    break;
                // A node's port keeps its keyframes in its adapter rather than in a property.
                case IAnimatablePropertyAdapter { Animation: { } adapterAnimation } when _animations.Add(adapterAnimation):
                    found(adapterAnimation);
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
}

// A row of a clip's "Animation" menu: a property whose inline layer it opens, an object or property whose rows are
// below it, or both.
internal sealed class AnimationMenuItem(string label, IAnimatablePropertyAdapter? adapter = null)
{
    public string Label { get; } = label;

    // The property whose inline layer the row opens, or null for a row that only holds others.
    public IAnimatablePropertyAdapter? Adapter { get; } = adapter;

    public List<AnimationMenuItem> Children { get; } = [];

    // The properties of this row and every row below it.
    public IEnumerable<IAnimatablePropertyAdapter> Adapters
        => Children.SelectMany(child => child.Adapters).Prepend(Adapter).OfType<IAnimatablePropertyAdapter>();
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

using System.Collections;
using Beutl.Animation;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

// Keyframes of an element's objects, including nested objects, separated components, and node ports.
internal static class ElementAnimations
{
    // The distinct keyframe times relative to the element's start, in order. Watch empty animations too so the
    // marks update when their first key is added.
    internal static IReadOnlyList<MediaTime> ReadMarkTimes(Element element, Action<IKeyFrameAnimation> watch)
    {
        MediaTime start = element.Start.Value;
        var times = new List<MediaTime>();
        var walker = new Walker(animation =>
        {
            watch(animation);
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
        return [.. times];
    }

    // The animation menu lists each property with keyframes in property-editor order, with its object and property
    // path in the label. A joined property pair shares a row.
    internal static List<AnimationMenuItem> ReadMenu(Element element)
    {
        var builder = new MenuBuilder();
        foreach (EngineObject obj in element.Objects)
        {
            builder.AppendValue(obj, "");
        }

        return builder.Items;
    }

    private sealed class MenuBuilder
    {
        private readonly HashSet<object> _ancestors = new(ReferenceEqualityComparer.Instance);
        // A node's nested port and the object its parent port holds can lead to the same property.
        private readonly HashSet<object> _listed = new(ReferenceEqualityComparer.Instance);

        public List<AnimationMenuItem> Items { get; } = [];

        public void AppendValue(object? value, string parentPath)
        {
            value = ChildObjects.Unwrap(value);
            if (value is null or string or IKeyFrameAnimation || !_ancestors.Add(value)) return;

            try
            {
                switch (value)
                {
                    case INodeMember { Property: { } adapter }:
                        AppendProperty(AppendPath(parentPath, adapter.DisplayName),
                            adapter is IAnimatablePropertyAdapter animated && HasKeys(animated.Animation) ? animated : null,
                            adapter.GetEngineProperty(), adapter.GetValue());
                        break;
                    case EngineObject obj:
                        string name = string.IsNullOrWhiteSpace(obj.Name.Value)
                            ? TypeDisplayHelpers.GetLocalizedName(obj.GetType())
                            : obj.Name.Value;
                        AppendObjectChildren(obj, AppendPath(parentPath, name));
                        break;
                    case CoreObject core:
                        AppendCoreChildren(core, parentPath);
                        break;
                    case IEnumerable list:
                        foreach (object? child in list)
                        {
                            AppendValue(child, parentPath);
                        }

                        break;
                }
            }
            finally
            {
                _ancestors.Remove(value);
            }
        }

        private void AppendObjectChildren(EngineObject obj, string path)
        {
            foreach (IProperty property in obj.GetDisplayProperties())
            {
                // The items of a group stand in its place.
                if (property is IListProperty { Name: "Children" } list && typeof(EngineObject).IsAssignableFrom(list.ElementType))
                {
                    AppendValue(list, path);
                    continue;
                }

                if (PropertyPair.Find(property) is { } pair)
                {
                    if (ReferenceEquals(property, pair.Properties[0]))
                        AppendPair(path, pair);
                    continue;
                }

                AppendProperty(AppendPath(path, Property.GetLocalizedName(property)),
                    HasKeys(property.GetKeyFrameAnimation())
                        ? PropertyAdapterFactory.CreateAdapter(property, obj) as IAnimatablePropertyAdapter
                        : null,
                    property, property.Value);
            }

            AppendCoreChildren(obj, path);
        }

        private void AppendCoreChildren(CoreObject obj, string path)
        {
            foreach (IProperty field in obj.Properties.Where(obj.IsBaseField))
            {
                if (field.ValueType.IsValueType || field.ValueType == typeof(string)) continue;
                AppendValue(field.Value, path);
            }
        }

        private void AppendProperty(string path, IAnimatablePropertyAdapter? toggle, IProperty? property, object? value)
        {
            AddItem(path, toggle);

            if (property?.GetPropertyAnimation() is IComponentAnimation components and CoreObject holder)
            {
                foreach (IProperty component in components.Components)
                {
                    if (HasKeys(component.GetKeyFrameAnimation())
                        && PropertyAdapterFactory.CreateAdapter(component, holder) is IAnimatablePropertyAdapter adapter)
                    {
                        AddItem(AppendPath(path, adapter.DisplayName), adapter);
                    }
                }

                return;
            }

            // A reference holds another object, whose keyframes are not this element's.
            if (property is IExpressionProperty { IsReference: true }) return;

            value = ChildObjects.Unwrap(value);
            if (value is EngineObject nested and not INodeMember && _ancestors.Add(nested))
            {
                try
                {
                    AppendObjectChildren(nested, path);
                }
                finally
                {
                    _ancestors.Remove(nested);
                }
            }
            else
            {
                AppendValue(value, path);
            }
        }

        private void AppendPair(string parentPath, PropertyPair pair)
        {
            string path = AppendPath(parentPath, pair.DisplayName);
            if (!pair.IsSeparated.Value)
            {
                if (HasKeys(pair.Animation)) AddItem(path, pair);
                return;
            }

            foreach (IAnimatablePropertyAdapter adapter in pair.Adapters)
            {
                if (HasKeys(adapter.Animation)) AddItem(AppendPath(path, adapter.DisplayName), adapter);
            }
        }

        private void AddItem(string path, IAnimatablePropertyAdapter? adapter)
        {
            if (adapter != null && _listed.Add(adapter.GetEngineProperty() ?? (object)adapter))
                Items.Add(new AnimationMenuItem(path, adapter));
        }

        private static string AppendPath(string path, string name) => path.Length == 0 ? name : $"{path} › {name}";
    }

    // Removing the last key leaves the animation in place, which neither marks nor the menu count.
    private static bool HasKeys(IKeyFrameAnimation? animation) => animation is { KeyFrames.Count: > 0 };

    // Goes below each object along ChildObjects once, through an engine object's properties in the order its panel
    // shows them, then the rest, and calls found for each animation once, with keys or without. Keyframes are not
    // walked into, as nothing below them holds an animation.
    private sealed class Walker(Action<IKeyFrameAnimation> found)
    {
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<IKeyFrameAnimation> _animations = new(ReferenceEqualityComparer.Instance);

        public void Visit(object obj)
        {
            if (obj is IKeyFrameAnimation || (!obj.GetType().IsValueType && !_visited.Add(obj))) return;

            switch (obj)
            {
                case IExpressionProperty property:
                    if (property.GetKeyFrameAnimation() is { } animation && _animations.Add(animation))
                        found(animation);
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

internal sealed record AnimationMenuItem(string Label, IAnimatablePropertyAdapter Adapter);

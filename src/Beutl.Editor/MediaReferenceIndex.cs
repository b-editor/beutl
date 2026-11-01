using Beutl.Editor.Operations;
using Beutl.Graphics3D.Models;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor;

// A media value a scene saves, with the element that holds it.
internal readonly record struct MediaReference(object Value, Element? Element);

// The media values a scene saves, kept by element so that a query after an edit walks only the elements the edit
// changed rather than the whole scene. It learns of edits from the scene's change operations, so it must see every
// one of them. A source relinked in place needs no operation: the URI is read from the source at each query. Use
// it on the thread that edits the scene.
internal sealed class MediaReferenceIndex(Scene scene)
{
    private readonly Dictionary<Element, MediaReference[]> _elements = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Element> _stale = new(ReferenceEqualityComparer.Instance);
    private bool _allStale = true;

    // Element walks so far, for tests.
    internal int ScannedElements { get; private set; }

    public void Invalidate(ChangeOperation operation)
    {
        switch (operation)
        {
            case IUpdatePropertyValueOperation update:
                Invalidate(update.Object);
                break;
            case ICollectionChangeOperation collection:
                Invalidate(collection.Object);
                // Elements that joined or left the scene. Any other item belongs to the collection's element.
                foreach (Element element in collection.Items.OfType<Element>()) _stale.Add(element);
                break;
            case UpdateNodeMemberOperation member:
                Invalidate(member.NodeMember);
                break;
            case UpdateSplineEasingOperation:
                // An easing curve holds no media.
                break;
            default:
                _allStale = true;
                break;
        }
    }

    // Only elements hold media, so an edit of the scene outside them changes none. An object whose parents do not
    // lead to the scene, such as the animation of a node port, cannot be placed, so every element is walked again.
    private void Invalidate(object target)
    {
        for (object? current = target; current != null; current = (current as IHierarchical)?.HierarchicalParent)
        {
            if (current is Element element)
            {
                _stale.Add(element);
                return;
            }

            if (ReferenceEquals(current, scene)) return;
        }

        _allStale = true;
    }

    // The values a walk of the whole scene reports, in its order: a font family, or a source with a file URI. One
    // that several elements share is reported once, with the first of them.
    public IReadOnlyList<MediaReference> GetReferences()
    {
        Refresh();
        var references = new List<MediaReference>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (Element element in scene.Children)
        {
            foreach (MediaReference reference in _elements[element])
            {
                if ((reference.Value is FontFamily || MissingMediaService.GetUri(reference.Value) is { IsFile: true })
                    && seen.Add(reference.Value))
                {
                    references.Add(reference);
                }
            }
        }

        return references;
    }

    private void Refresh()
    {
        if (_allStale) _elements.Clear();
        foreach (Element element in _stale) _elements.Remove(element);
        _allStale = false;
        _stale.Clear();

        foreach (Element element in scene.Children)
        {
            if (!_elements.ContainsKey(element)) _elements.Add(element, Scan(element));
        }

        // Every element of the scene has an entry now, so any extra one is an element that left it.
        if (_elements.Count > scene.Children.Count)
        {
            var children = new HashSet<Element>(scene.Children, ReferenceEqualityComparer.Instance);
            foreach (Element element in _elements.Keys.Where(element => !children.Contains(element)).ToArray())
                _elements.Remove(element);
        }
    }

    // Keeps every source, with or without a file URI, since a relink changes its URI in place.
    private MediaReference[] Scan(Element element)
    {
        ScannedElements++;
        var references = new List<MediaReference>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CoreSerializer.Visit(element, visit =>
        {
            if (visit.Value is FontFamily or MediaSource or ModelSource && seen.Add(visit.Value))
                references.Add(new MediaReference(visit.Value, FindElement(visit)));
            return false;
        });
        return [.. references];
    }

    private static Element? FindElement(CoreSerializationVisit visit)
    {
        for (CoreSerializationVisit? parent = visit.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Value is Element element) return element;
        }

        return null;
    }
}

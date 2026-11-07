using Beutl.Editor.Operations;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

// The elements of a scene that its change operations have touched since the last Clear, or all of them when one of the
// operations cannot be placed. Starts with all of them. Use it on the thread that edits the scene.
internal sealed class EditedElements(Scene scene)
{
    private readonly HashSet<Element> _elements = new(ReferenceEqualityComparer.Instance);

    public bool All { get; private set; } = true;

    public bool Contains(Element element) => All || _elements.Contains(element);

    public void Add(ChangeOperation operation)
    {
        (object? target, IEnumerable<object?> items) = ChangeOperationTargets.Find(operation);
        // An operation that names no target cannot be placed, which Add takes as all of them.
        Add(target);
        // Elements that joined or left the scene. Any other item belongs to the collection's element.
        foreach (Element element in items.OfType<Element>()) _elements.Add(element);
    }

    // For what an element saves that changes without an operation.
    public void AddOwnerOf(object target) => Add(target);

    public void AddAll() => All = true;

    public void Clear()
    {
        All = false;
        _elements.Clear();
    }

    // Only elements are saved apart from the scene, so an edit of the scene outside them touches none. An object whose
    // parents do not lead to the scene, such as the animation of a node port, cannot be placed.
    private void Add(object? target)
    {
        for (object? current = target; current != null; current = (current as IHierarchical)?.HierarchicalParent)
        {
            if (current is Element element)
            {
                _elements.Add(element);
                return;
            }

            if (ReferenceEquals(current, scene)) return;
        }

        All = true;
    }
}

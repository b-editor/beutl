using System.Collections;

namespace Beutl.ProjectSystem;

// A scene's Exclude patterns in the order they are saved. A pattern is found by its spelling or by its file name
// without scanning the others, so removing or restoring many elements stays linear.
internal sealed class ElementPatternSet : IReadOnlyCollection<string>
{
    private readonly LinkedList<string> _patterns = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.Ordinal);
    // Two spellings of one element file have the same file name, ignoring case.
    private readonly Dictionary<string, List<string>> _byFileName = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _patterns.Count;

    public bool Add(string pattern)
    {
        if (_nodes.ContainsKey(pattern)) return false;

        _nodes.Add(pattern, _patterns.AddLast(pattern));
        string fileName = Path.GetFileName(pattern);
        if (!_byFileName.TryGetValue(fileName, out List<string>? patterns))
            _byFileName.Add(fileName, patterns = []);
        patterns.Add(pattern);
        return true;
    }

    public bool Remove(string pattern)
    {
        if (!_nodes.Remove(pattern, out LinkedListNode<string>? node)) return false;

        _patterns.Remove(node);
        string fileName = Path.GetFileName(pattern);
        List<string> patterns = _byFileName[fileName];
        patterns.Remove(pattern);
        if (patterns.Count == 0) _byFileName.Remove(fileName);
        return true;
    }

    // Looks only at the patterns with this file name, ignoring case.
    public void RemoveAll(string fileName, Predicate<string> match)
    {
        if (!_byFileName.TryGetValue(fileName, out List<string>? patterns)) return;

        foreach (string pattern in patterns.FindAll(match))
        {
            Remove(pattern);
        }
    }

    public void RemoveAll(Predicate<string> match)
    {
        for (LinkedListNode<string>? node = _patterns.First; node != null;)
        {
            LinkedListNode<string>? next = node.Next;
            if (match(node.Value)) Remove(node.Value);
            node = next;
        }
    }

    public void Clear()
    {
        _patterns.Clear();
        _nodes.Clear();
        _byFileName.Clear();
    }

    public IEnumerator<string> GetEnumerator() => _patterns.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

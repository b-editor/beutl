using Beutl.Collections.Pooled;
using Beutl.Media;

namespace Beutl.ProjectSystem;

// Time and layer lookups shared by playback, sampling and editing. Range changes update balanced
// interval trees in O(log N); collection changes rebuild once on demand. Queries hold the lock only
// while collecting candidates, never while evaluating plugin objects.
internal sealed class SceneElementIndex
{
    private readonly object _sync = new();
    private readonly Tree _all;
    private readonly Dictionary<int, Tree> _layers;
    private readonly Dictionary<Element, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private HashSet<Element>? _transitionParticipants;

    public SceneElementIndex(IEnumerable<Element> elements)
    {
        Entry[] entries = elements.Select((element, order) =>
            new Entry(element, element.Range, element.ZIndex.Value, order)).ToArray();
        foreach (Entry entry in entries) _entries.Add(entry.Element, entry);
        _layers = entries.GroupBy(entry => entry.Layer)
            .ToDictionary(group => group.Key, group => new Tree(group.ToArray()));
        _all = new Tree(entries);
    }

    public void Update(Element element)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(element, out Entry old)) return;
            var entry = new Entry(element, element.Range, element.ZIndex.Value, old.Order);
            if (entry == old) return;
            _all.Remove(old);
            Tree oldLayer = _layers[old.Layer];
            oldLayer.Remove(old);
            if (old.Layer != entry.Layer && oldLayer.IsEmpty) _layers.Remove(old.Layer);
            _all.Add(entry);
            if (!_layers.TryGetValue(entry.Layer, out Tree? layer))
                _layers.Add(entry.Layer, layer = new Tree([]));
            layer.Add(entry);
            _entries[element] = entry;
            _transitionParticipants = null;
        }
    }

    public void InvalidateTransitions()
    {
        lock (_sync) _transitionParticipants = null;
    }

    public HashSet<Element> GetTransitionParticipants()
    {
        lock (_sync)
            return _transitionParticipants ??= ElementTransitions.CollectParticipants(_entries.Keys, this);
    }

    public void Query(MediaTime start, MediaTime end, PooledList<Element> result, int? layer = null)
    {
        lock (_sync)
        {
            if (layer is null) _all.Query(start, end, result);
            else if (_layers.TryGetValue(layer.Value, out Tree? tree)) tree.Query(start, end, result);
        }
    }

    public bool Intersects(int layer, TimeRange range)
    {
        lock (_sync)
            return !range.IsEmpty && _layers.TryGetValue(layer, out Tree? tree) && tree.Intersects(range);
    }

    public (Element? Before, Element? After) Neighbors(int layer, TimeRange range)
    {
        lock (_sync)
            return _layers.TryGetValue(layer, out Tree? tree) ? tree.Neighbors(range) : (null, null);
    }

    public int CompareSceneOrder(Element left, Element right)
    {
        lock (_sync) return _entries[left].Order.CompareTo(_entries[right].Order);
    }

    public void Sort(PooledList<Element> elements)
    {
        lock (_sync)
        {
            // OrderedAdd inserted before equal keys: equal-layer clips run in reverse scene order.
            elements.Span.Sort((left, right) =>
            {
                Entry a = _entries[left], b = _entries[right];
                int layer = a.Layer.CompareTo(b.Layer);
                return layer != 0 ? layer : b.Order.CompareTo(a.Order);
            });
        }
    }

    private readonly record struct Entry(Element Element, TimeRange Range, int Layer, int Order);

    private sealed class Tree
    {
        private Node? _root;
        private Entry[]? _ends;

        public bool IsEmpty => _root is null;

        public Tree(Entry[] entries)
        {
            Array.Sort(entries, Compare);
            _root = Build(entries, 0, entries.Length);
        }

        private static int Compare(Entry a, Entry b)
        {
            int start = a.Range.Start.CompareTo(b.Range.Start);
            return start != 0 ? start : a.Order.CompareTo(b.Order);
        }

        private sealed class Node(Entry entry)
        {
            public Entry Entry = entry;
            public Node? Left;
            public Node? Right;
            public int Height = 1;
            public MediaTime MaxEnd = entry.Range.End;

            public void Refresh()
            {
                Height = 1 + Math.Max(Left?.Height ?? 0, Right?.Height ?? 0);
                MaxEnd = Entry.Range.End;
                if (Left is { } left && left.MaxEnd > MaxEnd) MaxEnd = left.MaxEnd;
                if (Right is { } right && right.MaxEnd > MaxEnd) MaxEnd = right.MaxEnd;
            }
        }

        private static Node? Build(Entry[] entries, int first, int end)
        {
            if (first >= end) return null;
            int middle = first + (end - first) / 2;
            var node = new Node(entries[middle])
            {
                Left = Build(entries, first, middle),
                Right = Build(entries, middle + 1, end),
            };
            node.Refresh();
            return node;
        }

        public void Add(Entry entry)
        {
            _root = Insert(_root, entry);
            _ends = null;
        }

        private static Node Insert(Node? node, Entry entry)
        {
            if (node is null) return new Node(entry);
            if (Compare(entry, node.Entry) < 0) node.Left = Insert(node.Left, entry);
            else node.Right = Insert(node.Right, entry);
            return Balance(node);
        }

        public void Remove(Entry entry)
        {
            _root = Remove(_root, entry);
            _ends = null;
        }

        private static Node? Remove(Node? node, Entry entry)
        {
            if (node is null) return null;
            int order = Compare(entry, node.Entry);
            if (order < 0) node.Left = Remove(node.Left, entry);
            else if (order > 0) node.Right = Remove(node.Right, entry);
            else
            {
                if (node.Left is null) return node.Right;
                if (node.Right is null) return node.Left;
                Node next = node.Right;
                while (next.Left is not null) next = next.Left;
                node.Entry = next.Entry;
                node.Right = Remove(node.Right, next.Entry);
            }
            return Balance(node);
        }

        private static Node Balance(Node node)
        {
            node.Refresh();
            int balance = (node.Left?.Height ?? 0) - (node.Right?.Height ?? 0);
            if (balance > 1)
            {
                if ((node.Left!.Left?.Height ?? 0) < (node.Left.Right?.Height ?? 0))
                    node.Left = RotateLeft(node.Left);
                return RotateRight(node);
            }
            if (balance < -1)
            {
                if ((node.Right!.Right?.Height ?? 0) < (node.Right.Left?.Height ?? 0))
                    node.Right = RotateRight(node.Right);
                return RotateLeft(node);
            }
            return node;
        }

        private static Node RotateLeft(Node node)
        {
            Node root = node.Right!;
            node.Right = root.Left;
            root.Left = node;
            node.Refresh();
            root.Refresh();
            return root;
        }

        private static Node RotateRight(Node node)
        {
            Node root = node.Left!;
            node.Left = root.Right;
            root.Right = node;
            node.Refresh();
            root.Refresh();
            return root;
        }

        // Inclusive candidates cover zero-length probes and transition adjacency tolerances.
        // Callers retain their exact Contains/Intersects/CoversGraphicsTime rules.
        public void Query(MediaTime start, MediaTime end, PooledList<Element> result) => Query(_root, start, end, result);

        private static void Query(Node? node, MediaTime start, MediaTime end, PooledList<Element> result)
        {
            if (node is null || node.MaxEnd < start) return;
            Query(node.Left, start, end, result);
            if (node.Entry.Range.Start > end) return;
            if (node.Entry.Range.End >= start) result.Add(node.Entry.Element);
            Query(node.Right, start, end, result);
        }

        public bool Intersects(TimeRange range) => Intersects(_root, range);

        private static bool Intersects(Node? node, TimeRange range)
            => node is not null && node.MaxEnd > range.Start
               && (Intersects(node.Left, range) || node.Entry.Range.Start < range.End
                   && (node.Entry.Range.Intersects(range) || Intersects(node.Right, range)));

        public (Element? Before, Element? After) Neighbors(TimeRange range)
        {
            // Equal boundaries choose the last element in scene order, as AccumulateNeighbor does.
            Node? node = _root;
            Element? before = null;
            while (node is not null)
            {
                if (node.Entry.Range.Start < range.Start)
                {
                    before = node.Entry.Element;
                    node = node.Right;
                }
                else node = node.Left;
            }
            Entry[] ends = _ends ??= Enumerate(_root).OrderBy(entry => entry.Range.End)
                .ThenByDescending(entry => entry.Order).ToArray();
            int low = 0, high = ends.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (ends[middle].Range.End <= range.End) low = middle + 1;
                else high = middle;
            }
            return (before, low < ends.Length ? ends[low].Element : null);
        }

        private static IEnumerable<Entry> Enumerate(Node? node)
        {
            if (node is null) yield break;
            foreach (Entry entry in Enumerate(node.Left)) yield return entry;
            yield return node.Entry;
            foreach (Entry entry in Enumerate(node.Right)) yield return entry;
        }
    }
}

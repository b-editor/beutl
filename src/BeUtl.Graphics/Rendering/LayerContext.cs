namespace BeUtl.Rendering;

public class LayerContext : ILayerContext
{
    public LayerNode? this[TimeSpan timeSpan] => Get(timeSpan);

    public LayerNode? First { get; private set; }

    public LayerNode? Last { get; private set; }

    public int Count { get; private set; }

    public TimeSpan Duration { get; private set; }

    public void AddAfter(LayerNode node, LayerNode newNode)
    {
        if (node.Parent != this || newNode.Parent != null)
            throw new Exception("このnodeは他のレイヤーに属しています。");

        LayerNode? next = node.Next;
        newNode.Previous = node;
        node.Next = newNode;
        newNode.Next = next;

        newNode.Parent = this;

        Calculate();

        if (newNode.Next == null)
        {
            Last = newNode;
        }
    }

    public void AddBefore(LayerNode node, LayerNode newNode)
    {
        if (node.Parent != this || newNode.Parent != null)
            throw new Exception("このnodeは他のレイヤーに属しています。");

        LayerNode? prev = node.Previous;
        newNode.Next = node;
        node.Previous = newNode;
        newNode.Previous = prev;

        newNode.Parent = this;

        Calculate();

        if (newNode.Previous == null)
        {
            First = newNode;
        }
    }

    public void AddFirst(LayerNode node)
    {
        if (node.Parent != this)
            throw new Exception("このnodeは他のレイヤーに属しています。");

        if (First != null)
        {
            First.Previous = node;
            node.Next = First;
        }

        Calculate();
        First = node;
    }

    public void AddLast(LayerNode node)
    {
        if (node.Parent != this)
            throw new Exception("このnodeは他のレイヤーに属しています。");

        if (Last != null)
        {
            Last.Next = node;
            node.Previous = Last;
        }

        Calculate();
        Last = node;
    }

    public void Remove(LayerNode node)
    {
        LayerNode? prev = node.Previous;
        LayerNode? next = node.Next;

        if (prev != null)
            prev.Next = next;
        if (next != null)
            next.Previous = prev;

        node.Previous = null;
        node.Next = null;
        node.Parent = null;

        Calculate();
    }

    public bool ContainsNode(LayerNode node)
    {
        LayerNode? item = First;
        while (true)
        {
            if (item == node)
                return true;
            else if (item == null)
                return false;

            item = item.Next;
        }
    }

    private LayerNode? Get(TimeSpan timeSpan)
    {
        if (timeSpan > Duration)
            return null;

        LayerNode? node = First;
        TimeSpan x = TimeSpan.Zero;

        while (node != null)
        {
            x += node.Offset;
            if (x <= timeSpan && timeSpan < x + node.Duration)
            {
                return node;
            }

            x += node.Duration;
            node = node.Next;
        }

        return null;
    }

    private void Calculate()
    {
        LayerNode? node = First;
        TimeSpan timeSpan = TimeSpan.Zero;
        int count = 0;

        while (node != null)
        {
            timeSpan += node.Offset;
            timeSpan += node.Duration;
            node = node.Next;
            count++;
        }

        Duration = timeSpan;
        Count = count;
    }
}

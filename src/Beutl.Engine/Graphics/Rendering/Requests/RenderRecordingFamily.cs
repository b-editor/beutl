namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderRecordingFamily
{
    private readonly List<RenderNode> _activeNodes = [];

    public Scope Enter(RenderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        // The stack never holds a node twice - this method is what keeps it so - hence the scan may run
        // from the top, where a recording cycle closes.
        int cycleStart = -1;
        for (int index = _activeNodes.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(_activeNodes[index], node))
            {
                cycleStart = index;
                break;
            }
        }

        if (cycleStart >= 0)
        {
            IEnumerable<string> cycle = _activeNodes
                .Skip(cycleStart)
                .Append(node)
                .Select(static item => item.GetType().FullName ?? item.GetType().Name);
            throw new InvalidOperationException(
                $"A render-node recording cycle was detected: {string.Join(" -> ", cycle)}.");
        }

        _activeNodes.Add(node);
        return new Scope(this, node);
    }

    private void Exit(RenderNode node)
    {
        int index = _activeNodes.Count - 1;
        if (index < 0 || !ReferenceEquals(_activeNodes[index], node))
            throw new InvalidOperationException("The active render-node recording stack is corrupted.");

        _activeNodes.RemoveAt(index);
    }

    /// <remarks>Mutable for idempotent disposal; callers must keep the value in one local rather than copy it.</remarks>
    public struct Scope : IDisposable
    {
        private RenderRecordingFamily? _owner;
        private readonly RenderNode? _node;

        internal Scope(RenderRecordingFamily owner, RenderNode node)
        {
            _owner = owner;
            _node = node;
        }

        public void Dispose()
        {
            RenderRecordingFamily? owner = _owner;
            _owner = null;
            if (owner is not null)
                owner.Exit(_node!);
        }
    }
}

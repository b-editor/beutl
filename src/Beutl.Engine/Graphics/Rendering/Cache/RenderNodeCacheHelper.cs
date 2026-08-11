using System.Text.Json.Serialization;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

internal static class RenderNodeCacheHelper
{
    internal static readonly ILogger _logger = Log.CreateLogger("RenderNodeCache");

    internal static RenderNodeCacheLifecycle BeginLifecycle(RenderNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return RenderNodeCacheLifecycle.Create(root);
    }

    /// <summary>Resets the cache of <paramref name="node"/> and of every node it owns.</summary>
    /// <remarks>
    /// Ownership, not <see cref="RenderNode.ChildNodes"/>: a merely referenced node is shared with other
    /// live entries, so tearing down one holder must not drop caches the others still rely on.
    /// </remarks>
    internal static void ClearOwnedCaches(RenderNode node)
    {
        node.Cache.Reset();

        if (node is not ContainerRenderNode containerNode) return;

        foreach (RenderNode item in containerNode.Children)
        {
            ClearOwnedCaches(item);
        }
    }
}

internal sealed class RenderNodeCacheLifecycle
{
    private readonly NodeSnapshot[] _nodes;
    private bool _completed;

    private RenderNodeCacheLifecycle(NodeSnapshot[] nodes)
    {
        _nodes = nodes;
    }

    internal static RenderNodeCacheLifecycle Create(RenderNode root)
    {
        var snapshots = new Dictionary<RenderNode, NodeSnapshot>(ReferenceEqualityComparer.Instance);
        Collect(root, snapshots);

        var ancestors = new HashSet<NodeSnapshot>();
        foreach (NodeSnapshot snapshot in snapshots.Values)
        {
            if (!snapshot.WasDirty)
                continue;

            MarkNodeAndAncestors(snapshot, ancestors);
        }

        foreach (NodeSnapshot snapshot in snapshots.Values)
        {
            if (snapshot.IsInvalidated && !snapshot.Node.IsDisposed)
            {
                snapshot.Node.Cache.Reset();
            }
        }

        return new RenderNodeCacheLifecycle([.. snapshots.Values]);
    }

    internal void CompleteSuccessfully(bool advanceWarmup)
    {
        if (_completed)
            throw new InvalidOperationException("A render-node cache lifecycle can complete only once.");

        var changedDuringRequest = new List<NodeSnapshot>();
        foreach (NodeSnapshot snapshot in _nodes)
        {
            if (snapshot.Node.HasChanges
                && (!snapshot.WasDirty || snapshot.Node.ChangeVersion != snapshot.ObservedChangeVersion))
            {
                changedDuringRequest.Add(snapshot);
            }
        }

        if (changedDuringRequest.Count != 0)
        {
            var ancestors = new HashSet<NodeSnapshot>();
            foreach (NodeSnapshot snapshot in changedDuringRequest)
            {
                MarkNodeAndAncestors(snapshot, ancestors);
            }

            foreach (NodeSnapshot snapshot in ancestors)
            {
                if (!snapshot.Node.IsDisposed)
                {
                    snapshot.Node.Cache.Reset();
                }
            }
        }

        foreach (NodeSnapshot snapshot in _nodes)
        {
            if (snapshot.WasDirty)
            {
                snapshot.Node.ClearChanges(snapshot.ObservedChangeVersion);
            }
        }

        if (advanceWarmup)
        {
            foreach (NodeSnapshot snapshot in _nodes)
            {
                if (!snapshot.IsInvalidated
                    && !snapshot.Node.IsDisposed
                    && !snapshot.Node.HasChanges
                    && snapshot.Node.ChangeVersion == snapshot.ObservedChangeVersion)
                {
                    snapshot.Node.Cache.RecordSuccessfulStableRequest();
                }
            }
        }

        _completed = true;
    }

    private static NodeSnapshot Collect(
        RenderNode node,
        IDictionary<RenderNode, NodeSnapshot> snapshots)
    {
        if (snapshots.TryGetValue(node, out NodeSnapshot? existing))
        {
            if (existing.IsVisiting)
            {
                throw new InvalidOperationException(
                    "A render-node ChildNodes cycle was detected while preparing cache lifecycle state.");
            }

            return existing;
        }

        var snapshot = new NodeSnapshot(node, node.HasChanges, node.ChangeVersion)
        {
            IsVisiting = true,
        };
        snapshots.Add(node, snapshot);
        try
        {
            ReadOnlySpan<RenderNode> children = node.ChildNodes;
            for (int i = 0; i < children.Length; i++)
            {
                RenderNode child = children[i];
                ArgumentNullException.ThrowIfNull(child);
                NodeSnapshot childSnapshot = Collect(child, snapshots);
                snapshot.Children.Add(childSnapshot);
                childSnapshot.Parents.Add(snapshot);
            }
        }
        finally
        {
            snapshot.IsVisiting = false;
        }

        return snapshot;
    }

    private static void MarkNodeAndAncestors(NodeSnapshot node, ISet<NodeSnapshot> visited)
    {
        if (!visited.Add(node))
            return;

        node.IsInvalidated = true;
        foreach (NodeSnapshot parent in node.Parents)
        {
            MarkNodeAndAncestors(parent, visited);
        }
    }

    private sealed class NodeSnapshot(
        RenderNode node,
        bool wasDirty,
        long observedChangeVersion)
    {
        public RenderNode Node { get; } = node;

        public bool WasDirty { get; } = wasDirty;

        public long ObservedChangeVersion { get; } = observedChangeVersion;

        public List<NodeSnapshot> Children { get; } = [];

        public List<NodeSnapshot> Parents { get; } = [];

        public bool IsVisiting { get; set; }

        public bool IsInvalidated { get; set; }
    }
}

[JsonSerializable(typeof(RenderCacheOptions))]
public record RenderCacheOptions(bool IsEnabled, RenderCacheRules Rules)
{
    public static readonly RenderCacheOptions Disabled = new(false, RenderCacheRules.Default);
    public static readonly RenderCacheOptions Enabled = new(true, RenderCacheRules.Default);
    public static readonly RenderCacheOptions Default = Disabled;

    public static RenderCacheOptions CreateFromGlobalConfiguration()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        return new RenderCacheOptions(
            config.IsNodeCacheEnabled,
            RenderCacheRules.Create(config.NodeCacheMaxPixels, config.NodeCacheMinPixels));
    }
}

public readonly record struct RenderCacheRules(int MaxPixels, int MinPixels)
{
    public static readonly RenderCacheRules Default = new(1000 * 1000, 1);

    // Normalize Min >= 1, Max >= Min so Match() is never trivially false.
    public static RenderCacheRules Create(int maxPixels, int minPixels)
    {
        int min = Math.Max(1, minPixels);
        int max = Math.Max(min, maxPixels);
        return new RenderCacheRules(max, min);
    }

    public bool Match(PixelSize size)
    {
        long count = (long)size.Width * size.Height;
        return MinPixels <= count && count <= MaxPixels;
    }

    public bool Match(long pixels)
    {
        return MinPixels <= pixels && pixels <= MaxPixels;
    }
}

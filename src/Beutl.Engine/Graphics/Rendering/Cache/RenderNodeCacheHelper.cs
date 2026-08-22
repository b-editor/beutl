using System.Runtime.CompilerServices;
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
        ResolveSignatures(snapshots.Values);

        var ancestors = new HashSet<NodeSnapshot>();
        foreach (NodeSnapshot snapshot in snapshots.Values)
        {
            // WasDirty alone cannot carry a shared child's change: HasChanges is one flag per node, and
            // whichever root completes first clears it for the others. The signature is root-independent.
            if (!snapshot.WasDirty && !HasStaleSignature(snapshot))
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

        RestampSignatures();

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

    private void RestampSignatures()
    {
        ResolveSignatures(_nodes, useCurrentChangeVersion: true);
        foreach (NodeSnapshot snapshot in _nodes)
        {
            if (!snapshot.Node.IsDisposed)
                snapshot.Node.Cache.DependencySignature = snapshot.Signature;
        }
    }

    private static bool HasStaleSignature(NodeSnapshot snapshot)
    {
        RenderNodeCache cache = snapshot.Node.Cache;
        return cache.IsCached
               && cache.DependencySignature != 0
               && cache.DependencySignature != snapshot.Signature;
    }

    private static void ResolveSignatures(
        IEnumerable<NodeSnapshot> all,
        bool useCurrentChangeVersion = false)
    {
        var resolved = new HashSet<NodeSnapshot>();
        var stack = new Stack<(NodeSnapshot Snapshot, bool ChildrenResolved)>();
        foreach (NodeSnapshot start in all)
        {
            stack.Push((start, false));
            while (stack.Count != 0)
            {
                (NodeSnapshot snapshot, bool childrenResolved) = stack.Pop();
                if (childrenResolved)
                {
                    long changeVersion = useCurrentChangeVersion
                        ? snapshot.Node.ChangeVersion
                        : snapshot.ObservedChangeVersion;
                    snapshot.Signature = ComputeSignature(snapshot, changeVersion);
                    continue;
                }

                if (!resolved.Add(snapshot))
                    continue;

                stack.Push((snapshot, true));
                foreach (NodeSnapshot child in snapshot.Children)
                    stack.Push((child, false));
            }
        }
    }

    // The child's runtime identity is mixed in alongside its signature: two freshly built children both sit at
    // change version 0, so a container that swaps one for the other would otherwise reproduce the parent's
    // previous signature exactly and keep serving the replaced child's cached pixels.
    private static long ComputeSignature(NodeSnapshot snapshot, long changeVersion)
    {
        unchecked
        {
            const ulong Basis = 14695981039346656037UL;
            ulong hash = Mix(Basis, (ulong)changeVersion);
            foreach (NodeSnapshot child in snapshot.Children)
            {
                hash = Mix(hash, (ulong)(uint)RuntimeHelpers.GetHashCode(child.Node));
                hash = Mix(hash, (ulong)child.Signature);
            }

            // 0 is the unstamped marker.
            return hash == 0 ? 1 : (long)hash;
        }
    }

    private static ulong Mix(ulong hash, ulong value)
    {
        unchecked
        {
            const ulong Prime = 1099511628211UL;
            for (int shift = 0; shift < 64; shift += 8)
            {
                hash ^= (value >> shift) & 0xFF;
                hash *= Prime;
            }

            return hash;
        }
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

        public long Signature { get; set; }

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

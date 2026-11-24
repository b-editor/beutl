using System.Runtime.InteropServices;

namespace Beutl.Graphics.Effects;

internal sealed partial class PuppetMesh
{
    private TriangleWarpRenderer.OutlineEdge[]? _outline;

    /// <summary>The edges that only one triangle uses, each with the third corner of that triangle.</summary>
    public TriangleWarpRenderer.OutlineEdge[] Outline => _outline ??= FindOutline(Indices);

    private static TriangleWarpRenderer.OutlineEdge[] FindOutline(ushort[] indices)
    {
        // An edge maps to its outline edge, or to null once a second triangle shares it.
        var edges = new Dictionary<(int, int), TriangleWarpRenderer.OutlineEdge?>(indices.Length);
        for (int i = 0; i < indices.Length; i += 3)
        {
            Add(indices[i], indices[i + 1], indices[i + 2]);
            Add(indices[i + 1], indices[i + 2], indices[i]);
            Add(indices[i + 2], indices[i], indices[i + 1]);
        }

        var outline = new List<TriangleWarpRenderer.OutlineEdge>();
        foreach (TriangleWarpRenderer.OutlineEdge? edge in edges.Values)
        {
            if (edge is { } single)
                outline.Add(single);
        }

        return outline.ToArray();

        void Add(int start, int end, int opposite)
        {
            ref TriangleWarpRenderer.OutlineEdge? edge = ref CollectionsMarshal.GetValueRefOrAddDefault(
                edges, (Math.Min(start, end), Math.Max(start, end)), out bool shared);
            edge = shared ? null : new TriangleWarpRenderer.OutlineEdge(start, end, opposite);
        }
    }
}

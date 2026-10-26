namespace Beutl.Graphics.Backend;

/// <summary>How a draw assembles vertices into primitives.</summary>
public enum PrimitiveTopology
{
    TriangleList,
    TriangleStrip,
    TriangleFan,
    LineList,
    LineStrip,
    PointList,
    PatchList,
    LineListAdjacency,
    LineStripAdjacency,
    TriangleListAdjacency,
    TriangleStripAdjacency,
}

/// <summary>The unsigned element format of an index buffer.</summary>
public enum IndexFormat
{
    UInt16,
    UInt32,
}

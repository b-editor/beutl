namespace Beutl.Graphics.Backend;

/// <summary>The 32-bit component type of a texel, vertex attribute or shader interface variable.</summary>
internal enum NumericType
{
    Float,
    SInt,
    UInt,
}

internal static class VertexFormatExtensions
{
    /// <summary>Gets the component type, or null for a value <see cref="VertexFormat"/> does not define.</summary>
    internal static NumericType? GetNumericType(this VertexFormat format) => format switch
    {
        VertexFormat.Float or VertexFormat.Float2 or VertexFormat.Float3 or VertexFormat.Float4 => NumericType.Float,
        VertexFormat.Int or VertexFormat.Int2 or VertexFormat.Int3 or VertexFormat.Int4 => NumericType.SInt,
        VertexFormat.UInt or VertexFormat.UInt2 or VertexFormat.UInt3 or VertexFormat.UInt4 => NumericType.UInt,
        _ => null,
    };

    internal static int ComponentCount(this VertexFormat format) => format switch
    {
        VertexFormat.Float or VertexFormat.Int or VertexFormat.UInt => 1,
        VertexFormat.Float2 or VertexFormat.Int2 or VertexFormat.UInt2 => 2,
        VertexFormat.Float3 or VertexFormat.Int3 or VertexFormat.UInt3 => 3,
        VertexFormat.Float4 or VertexFormat.Int4 or VertexFormat.UInt4 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}

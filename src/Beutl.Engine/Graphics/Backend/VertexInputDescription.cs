namespace Beutl.Graphics.Backend;

public struct VertexInputDescription
{
    public VertexBindingDescription[] Bindings { get; set; }

    public VertexAttributeDescription[] Attributes { get; set; }

    /// <summary>
    /// Gets an empty vertex input description (for fullscreen passes).
    /// </summary>
    public static VertexInputDescription Empty => new()
    {
        Bindings = [],
        Attributes = []
    };
}

public struct VertexBindingDescription
{
    public uint Binding { get; set; }

    /// <summary>
    /// The stride in bytes between consecutive vertices.
    /// </summary>
    public uint Stride { get; set; }

    public VertexInputRate InputRate { get; set; }
}

public struct VertexAttributeDescription
{
    public uint Location { get; set; }

    public uint Binding { get; set; }

    public VertexFormat Format { get; set; }

    /// <summary>
    /// The offset in bytes from the start of the vertex data.
    /// </summary>
    public uint Offset { get; set; }
}

public enum VertexInputRate
{
    Vertex = 0,
    Instance = 1
}

public enum VertexFormat
{
    /// <summary>
    /// Single 32-bit float.
    /// </summary>
    Float = 0,

    /// <summary>
    /// Two 32-bit floats (vec2).
    /// </summary>
    Float2 = 1,

    /// <summary>
    /// Three 32-bit floats (vec3).
    /// </summary>
    Float3 = 2,

    /// <summary>
    /// Four 32-bit floats (vec4).
    /// </summary>
    Float4 = 3,

    /// <summary>
    /// Single 32-bit signed integer.
    /// </summary>
    Int = 4,

    /// <summary>
    /// Two 32-bit signed integers (ivec2).
    /// </summary>
    Int2 = 5,

    /// <summary>
    /// Three 32-bit signed integers (ivec3).
    /// </summary>
    Int3 = 6,

    /// <summary>
    /// Four 32-bit signed integers (ivec4).
    /// </summary>
    Int4 = 7,

    /// <summary>
    /// Single 32-bit unsigned integer.
    /// </summary>
    UInt = 8,

    /// <summary>
    /// Two 32-bit unsigned integers (uvec2).
    /// </summary>
    UInt2 = 9,

    /// <summary>
    /// Three 32-bit unsigned integers (uvec3).
    /// </summary>
    UInt3 = 10,

    /// <summary>
    /// Four 32-bit unsigned integers (uvec4).
    /// </summary>
    UInt4 = 11
}

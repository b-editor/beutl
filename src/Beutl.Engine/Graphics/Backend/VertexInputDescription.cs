namespace Beutl.Graphics.Backend;

/// <summary>
/// Describes vertex input for a graphics pipeline.
/// </summary>
public struct VertexInputDescription
{
    /// <summary>
    /// Gets or sets the vertex binding descriptions.
    /// </summary>
    public VertexBindingDescription[] Bindings { get; set; }

    /// <summary>
    /// Gets or sets the vertex attribute descriptions.
    /// </summary>
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

/// <summary>
/// Describes a vertex buffer binding.
/// </summary>
public struct VertexBindingDescription
{
    /// <summary>
    /// The binding index.
    /// </summary>
    public uint Binding { get; set; }

    /// <summary>
    /// The stride in bytes between consecutive vertices.
    /// </summary>
    public uint Stride { get; set; }

    /// <summary>
    /// Whether the data is per-vertex or per-instance.
    /// </summary>
    public VertexInputRate InputRate { get; set; }
}

/// <summary>
/// Describes a vertex attribute within a vertex buffer.
/// </summary>
public struct VertexAttributeDescription
{
    /// <summary>
    /// The shader location for this attribute.
    /// </summary>
    public uint Location { get; set; }

    /// <summary>
    /// The binding index this attribute reads from.
    /// </summary>
    public uint Binding { get; set; }

    /// <summary>
    /// The format of the attribute data.
    /// </summary>
    public VertexFormat Format { get; set; }

    /// <summary>
    /// The offset in bytes from the start of the vertex data.
    /// </summary>
    public uint Offset { get; set; }
}

/// <summary>
/// Specifies the rate at which vertex attributes are read.
/// </summary>
public enum VertexInputRate
{
    /// <summary>
    /// Attributes are read per-vertex.
    /// </summary>
    Vertex = 0,

    /// <summary>
    /// Attributes are read per-instance.
    /// </summary>
    Instance = 1
}

/// <summary>
/// Specifies the format of vertex attribute data.
/// </summary>
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

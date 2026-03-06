namespace Beutl.Graphics.Backend;

/// <summary>
/// Describes the size of a descriptor pool.
/// </summary>
public struct DescriptorPoolSize
{
    /// <summary>
    /// Gets or sets the type of descriptor.
    /// </summary>
    public DescriptorType Type { get; set; }

    /// <summary>
    /// Gets or sets the number of descriptors of this type.
    /// </summary>
    public uint Count { get; set; }

    /// <summary>
    /// Creates a new descriptor pool size.
    /// </summary>
    public DescriptorPoolSize(DescriptorType type, uint count)
    {
        Type = type;
        Count = count;
    }
}

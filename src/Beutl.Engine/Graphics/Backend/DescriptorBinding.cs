namespace Beutl.Graphics.Backend;

/// <summary>
/// Describes a descriptor binding for a pipeline.
/// </summary>
public struct DescriptorBinding
{
    /// <summary>
    /// Gets or sets the binding index.
    /// </summary>
    public uint Binding { get; set; }

    /// <summary>
    /// Gets or sets the type of descriptor.
    /// </summary>
    public DescriptorType Type { get; set; }

    /// <summary>
    /// Gets or sets the number of descriptors in this binding.
    /// </summary>
    public uint Count { get; set; }

    /// <summary>
    /// Gets or sets the shader stages that can access this binding.
    /// </summary>
    public ShaderStage Stages { get; set; }

    /// <summary>
    /// Creates a new descriptor binding.
    /// </summary>
    public DescriptorBinding(uint binding, DescriptorType type, uint count, ShaderStage stages)
    {
        Binding = binding;
        Type = type;
        Count = count;
        Stages = stages;
    }
}

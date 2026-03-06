namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies the type of a descriptor binding.
/// </summary>
public enum DescriptorType
{
    /// <summary>
    /// A sampler descriptor.
    /// </summary>
    Sampler = 0,

    /// <summary>
    /// A combined image sampler descriptor.
    /// </summary>
    CombinedImageSampler = 1,

    /// <summary>
    /// A sampled image descriptor.
    /// </summary>
    SampledImage = 2,

    /// <summary>
    /// A storage image descriptor.
    /// </summary>
    StorageImage = 3,

    /// <summary>
    /// A uniform texel buffer descriptor.
    /// </summary>
    UniformTexelBuffer = 4,

    /// <summary>
    /// A storage texel buffer descriptor.
    /// </summary>
    StorageTexelBuffer = 5,

    /// <summary>
    /// A uniform buffer descriptor.
    /// </summary>
    UniformBuffer = 6,

    /// <summary>
    /// A storage buffer descriptor.
    /// </summary>
    StorageBuffer = 7,

    /// <summary>
    /// A dynamic uniform buffer descriptor.
    /// </summary>
    UniformBufferDynamic = 8,

    /// <summary>
    /// A dynamic storage buffer descriptor.
    /// </summary>
    StorageBufferDynamic = 9,

    /// <summary>
    /// An input attachment descriptor.
    /// </summary>
    InputAttachment = 10,
}

using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Converts between abstracted Backend flags and Vulkan-specific flags.
/// </summary>
internal static class VulkanFlagConverter
{
    public static BufferUsageFlags ToVulkan(BufferUsage usage)
    {
        BufferUsageFlags result = 0;

        if ((usage & BufferUsage.VertexBuffer) != 0)
            result |= BufferUsageFlags.VertexBufferBit;
        if ((usage & BufferUsage.IndexBuffer) != 0)
            result |= BufferUsageFlags.IndexBufferBit;
        if ((usage & BufferUsage.UniformBuffer) != 0)
            result |= BufferUsageFlags.UniformBufferBit;
        if ((usage & BufferUsage.StorageBuffer) != 0)
            result |= BufferUsageFlags.StorageBufferBit;
        if ((usage & BufferUsage.TransferSource) != 0)
            result |= BufferUsageFlags.TransferSrcBit;
        if ((usage & BufferUsage.TransferDestination) != 0)
            result |= BufferUsageFlags.TransferDstBit;
        if ((usage & BufferUsage.IndirectBuffer) != 0)
            result |= BufferUsageFlags.IndirectBufferBit;

        return result;
    }

    public static MemoryPropertyFlags ToVulkan(MemoryProperty memoryProperty)
    {
        MemoryPropertyFlags result = 0;

        if ((memoryProperty & MemoryProperty.DeviceLocal) != 0)
            result |= MemoryPropertyFlags.DeviceLocalBit;
        if ((memoryProperty & MemoryProperty.HostVisible) != 0)
            result |= MemoryPropertyFlags.HostVisibleBit;
        if ((memoryProperty & MemoryProperty.HostCoherent) != 0)
            result |= MemoryPropertyFlags.HostCoherentBit;
        if ((memoryProperty & MemoryProperty.HostCached) != 0)
            result |= MemoryPropertyFlags.HostCachedBit;
        if ((memoryProperty & MemoryProperty.LazilyAllocated) != 0)
            result |= MemoryPropertyFlags.LazilyAllocatedBit;

        return result;
    }

    public static Silk.NET.Vulkan.DescriptorType ToVulkan(Backend.DescriptorType type)
    {
        return type switch
        {
            Backend.DescriptorType.Sampler => Silk.NET.Vulkan.DescriptorType.Sampler,
            Backend.DescriptorType.CombinedImageSampler => Silk.NET.Vulkan.DescriptorType.CombinedImageSampler,
            Backend.DescriptorType.SampledImage => Silk.NET.Vulkan.DescriptorType.SampledImage,
            Backend.DescriptorType.StorageImage => Silk.NET.Vulkan.DescriptorType.StorageImage,
            Backend.DescriptorType.UniformTexelBuffer => Silk.NET.Vulkan.DescriptorType.UniformTexelBuffer,
            Backend.DescriptorType.StorageTexelBuffer => Silk.NET.Vulkan.DescriptorType.StorageTexelBuffer,
            Backend.DescriptorType.UniformBuffer => Silk.NET.Vulkan.DescriptorType.UniformBuffer,
            Backend.DescriptorType.StorageBuffer => Silk.NET.Vulkan.DescriptorType.StorageBuffer,
            Backend.DescriptorType.UniformBufferDynamic => Silk.NET.Vulkan.DescriptorType.UniformBufferDynamic,
            Backend.DescriptorType.StorageBufferDynamic => Silk.NET.Vulkan.DescriptorType.StorageBufferDynamic,
            Backend.DescriptorType.InputAttachment => Silk.NET.Vulkan.DescriptorType.InputAttachment,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    public static ShaderStageFlags ToVulkan(ShaderStage stage)
    {
        ShaderStageFlags result = 0;

        if ((stage & ShaderStage.Vertex) != 0)
            result |= ShaderStageFlags.VertexBit;
        if ((stage & ShaderStage.Fragment) != 0)
            result |= ShaderStageFlags.FragmentBit;
        if ((stage & ShaderStage.Compute) != 0)
            result |= ShaderStageFlags.ComputeBit;
        if ((stage & ShaderStage.Geometry) != 0)
            result |= ShaderStageFlags.GeometryBit;
        if ((stage & ShaderStage.TessellationControl) != 0)
            result |= ShaderStageFlags.TessellationControlBit;
        if ((stage & ShaderStage.TessellationEvaluation) != 0)
            result |= ShaderStageFlags.TessellationEvaluationBit;

        return result;
    }

    public static DescriptorSetLayoutBinding ToVulkan(DescriptorBinding binding)
    {
        return new DescriptorSetLayoutBinding
        {
            Binding = binding.Binding,
            DescriptorType = ToVulkan(binding.Type),
            DescriptorCount = binding.Count,
            StageFlags = ToVulkan(binding.Stages)
        };
    }

    public static Silk.NET.Vulkan.DescriptorPoolSize ToVulkan(Backend.DescriptorPoolSize poolSize)
    {
        return new Silk.NET.Vulkan.DescriptorPoolSize
        {
            Type = ToVulkan(poolSize.Type),
            DescriptorCount = poolSize.Count
        };
    }
}

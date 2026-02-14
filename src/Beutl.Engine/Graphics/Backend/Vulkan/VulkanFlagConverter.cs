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

    public static Silk.NET.Vulkan.FrontFace ToVulkan(Backend.FrontFace frontFace)
    {
        return frontFace switch
        {
            Backend.FrontFace.CounterClockwise => Silk.NET.Vulkan.FrontFace.CounterClockwise,
            Backend.FrontFace.Clockwise => Silk.NET.Vulkan.FrontFace.Clockwise,
            _ => Silk.NET.Vulkan.FrontFace.CounterClockwise
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

    public static CullModeFlags ToVulkan(CullMode cullMode)
    {
        return cullMode switch
        {
            CullMode.None => CullModeFlags.None,
            CullMode.Front => CullModeFlags.FrontBit,
            CullMode.Back => CullModeFlags.BackBit,
            _ => CullModeFlags.BackBit
        };
    }

    public static Format ToVulkan(VertexFormat format)
    {
        return format switch
        {
            VertexFormat.Float => Format.R32Sfloat,
            VertexFormat.Float2 => Format.R32G32Sfloat,
            VertexFormat.Float3 => Format.R32G32B32Sfloat,
            VertexFormat.Float4 => Format.R32G32B32A32Sfloat,
            VertexFormat.Int => Format.R32Sint,
            VertexFormat.Int2 => Format.R32G32Sint,
            VertexFormat.Int3 => Format.R32G32B32Sint,
            VertexFormat.Int4 => Format.R32G32B32A32Sint,
            VertexFormat.UInt => Format.R32Uint,
            VertexFormat.UInt2 => Format.R32G32Uint,
            VertexFormat.UInt3 => Format.R32G32B32Uint,
            VertexFormat.UInt4 => Format.R32G32B32A32Uint,
            _ => Format.R32G32B32Sfloat
        };
    }

    public static Silk.NET.Vulkan.VertexInputRate ToVulkan(Backend.VertexInputRate inputRate)
    {
        return inputRate switch
        {
            Backend.VertexInputRate.Vertex => Silk.NET.Vulkan.VertexInputRate.Vertex,
            Backend.VertexInputRate.Instance => Silk.NET.Vulkan.VertexInputRate.Instance,
            _ => Silk.NET.Vulkan.VertexInputRate.Vertex
        };
    }

    public static VulkanVertexInputDescription ToVulkan(Backend.VertexInputDescription description)
    {
        var bindings = new VertexInputBindingDescription[description.Bindings?.Length ?? 0];
        var attributes = new VertexInputAttributeDescription[description.Attributes?.Length ?? 0];

        for (int i = 0; i < bindings.Length; i++)
        {
            var binding = description.Bindings![i];
            bindings[i] = new VertexInputBindingDescription
            {
                Binding = binding.Binding,
                Stride = binding.Stride,
                InputRate = ToVulkan(binding.InputRate)
            };
        }

        for (int i = 0; i < attributes.Length; i++)
        {
            var attr = description.Attributes![i];
            attributes[i] = new VertexInputAttributeDescription
            {
                Location = attr.Location,
                Binding = attr.Binding,
                Format = ToVulkan(attr.Format),
                Offset = attr.Offset
            };
        }

        return new VulkanVertexInputDescription
        {
            Bindings = bindings,
            Attributes = attributes
        };
    }

    public static Silk.NET.Vulkan.BlendFactor ToVulkan(BlendFactor blendFactor)
    {
        return blendFactor switch
        {
            BlendFactor.Zero => Silk.NET.Vulkan.BlendFactor.Zero,
            BlendFactor.One => Silk.NET.Vulkan.BlendFactor.One,
            BlendFactor.SrcColor => Silk.NET.Vulkan.BlendFactor.SrcColor,
            BlendFactor.OneMinusSrcColor => Silk.NET.Vulkan.BlendFactor.OneMinusSrcColor,
            BlendFactor.DstColor => Silk.NET.Vulkan.BlendFactor.DstColor,
            BlendFactor.OneMinusDstColor => Silk.NET.Vulkan.BlendFactor.OneMinusDstColor,
            BlendFactor.SrcAlpha => Silk.NET.Vulkan.BlendFactor.SrcAlpha,
            BlendFactor.OneMinusSrcAlpha => Silk.NET.Vulkan.BlendFactor.OneMinusSrcAlpha,
            BlendFactor.DstAlpha => Silk.NET.Vulkan.BlendFactor.DstAlpha,
            BlendFactor.OneMinusDstAlpha => Silk.NET.Vulkan.BlendFactor.OneMinusDstAlpha,
            _ => Silk.NET.Vulkan.BlendFactor.One
        };
    }

    public static Silk.NET.Vulkan.BlendOp ToVulkan(BlendOp blendOp)
    {
        return blendOp switch
        {
            BlendOp.Add => Silk.NET.Vulkan.BlendOp.Add,
            BlendOp.Subtract => Silk.NET.Vulkan.BlendOp.Subtract,
            BlendOp.ReverseSubtract => Silk.NET.Vulkan.BlendOp.ReverseSubtract,
            BlendOp.Min => Silk.NET.Vulkan.BlendOp.Min,
            BlendOp.Max => Silk.NET.Vulkan.BlendOp.Max,
            _ => Silk.NET.Vulkan.BlendOp.Add
        };
    }
}

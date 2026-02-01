using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IDescriptorSet"/>.
/// </summary>
internal sealed unsafe class VulkanDescriptorSet : IDescriptorSet
{
    private readonly VulkanContext _context;
    private readonly DescriptorPool _descriptorPool;
    private readonly DescriptorSet _descriptorSet;
    private readonly DescriptorSetLayout _layout;
    private bool _disposed;

    public VulkanDescriptorSet(VulkanContext context, DescriptorSetLayout layout, Silk.NET.Vulkan.DescriptorPoolSize[] poolSizes)
    {
        _context = context;
        _layout = layout;

        var vk = context.Vk;
        var device = context.Device;

        // Create descriptor pool
        fixed (Silk.NET.Vulkan.DescriptorPoolSize* poolSizesPtr = poolSizes)
        {
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = 1,
                PoolSizeCount = (uint)poolSizes.Length,
                PPoolSizes = poolSizesPtr
            };

            DescriptorPool pool;
            var result = vk.CreateDescriptorPool(device, &poolInfo, null, &pool);
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Failed to create descriptor pool: {result}");
            }
            _descriptorPool = pool;
        }

        // Allocate descriptor set
        var layouts = stackalloc DescriptorSetLayout[] { layout };
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = layouts
        };

        DescriptorSet set;
        var allocResult = vk.AllocateDescriptorSets(device, &allocInfo, &set);
        if (allocResult != Result.Success)
        {
            vk.DestroyDescriptorPool(device, _descriptorPool, null);
            throw new InvalidOperationException($"Failed to allocate descriptor set: {allocResult}");
        }
        _descriptorSet = set;
    }

    public DescriptorSet Handle => _descriptorSet;

    public DescriptorSetLayout Layout => _layout;

    public void UpdateBuffer(int binding, IBuffer buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var vulkanBuffer = (VulkanBuffer)buffer;

        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = vulkanBuffer.Handle,
            Offset = 0,
            Range = vulkanBuffer.Size
        };

        var writeDescriptor = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = (uint)binding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = Silk.NET.Vulkan.DescriptorType.UniformBuffer,
            PBufferInfo = &bufferInfo
        };

        _context.Vk.UpdateDescriptorSets(_context.Device, 1, &writeDescriptor, 0, null);
    }

    public void UpdateTexture(int binding, ITexture2D texture, ISampler sampler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var vulkanTexture = (VulkanTexture2D)texture;
        var vulkanSampler = (VulkanSampler)sampler;

        var imageInfo = new DescriptorImageInfo
        {
            ImageView = vulkanTexture.ImageViewHandle,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            Sampler = vulkanSampler.Handle
        };

        var writeDescriptor = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = (uint)binding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = Silk.NET.Vulkan.DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };

        _context.Vk.UpdateDescriptorSets(_context.Device, 1, &writeDescriptor, 0, null);
    }

    public void UpdateTextureCube(int binding, ITextureCube texture, ISampler sampler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var vulkanTexture = (VulkanTextureCube)texture;
        var vulkanSampler = (VulkanSampler)sampler;

        var imageInfo = new DescriptorImageInfo
        {
            ImageView = vulkanTexture.ImageViewHandle,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            Sampler = vulkanSampler.Handle
        };

        var writeDescriptor = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = (uint)binding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = Silk.NET.Vulkan.DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };

        _context.Vk.UpdateDescriptorSets(_context.Device, 1, &writeDescriptor, 0, null);
    }

    public void UpdateTextureArray(int binding, ITextureArray texture, ISampler sampler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var vulkanTexture = (VulkanTextureArray)texture;
        var vulkanSampler = (VulkanSampler)sampler;

        var imageInfo = new DescriptorImageInfo
        {
            ImageView = vulkanTexture.ImageViewHandle,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            Sampler = vulkanSampler.Handle
        };

        var writeDescriptor = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = (uint)binding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = Silk.NET.Vulkan.DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };

        _context.Vk.UpdateDescriptorSets(_context.Device, 1, &writeDescriptor, 0, null);
    }

    public void UpdateTextureCubeArray(int binding, ITextureCubeArray texture, ISampler sampler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var vulkanTexture = (VulkanTextureCubeArray)texture;
        var vulkanSampler = (VulkanSampler)sampler;

        var imageInfo = new DescriptorImageInfo
        {
            ImageView = vulkanTexture.ImageViewHandle,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            Sampler = vulkanSampler.Handle
        };

        var writeDescriptor = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = (uint)binding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = Silk.NET.Vulkan.DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };

        _context.Vk.UpdateDescriptorSets(_context.Device, 1, &writeDescriptor, 0, null);
    }

    public void Bind()
    {
        // Binding is done through command buffer in VulkanPipeline3D
        // This method is kept for interface compatibility
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Descriptor sets are automatically freed when the pool is destroyed
        _context.Vk.DestroyDescriptorPool(_context.Device, _descriptorPool, null);
    }
}

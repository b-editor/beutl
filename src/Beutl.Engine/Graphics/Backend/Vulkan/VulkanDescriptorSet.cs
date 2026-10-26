using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed unsafe class VulkanDescriptorSet : IDescriptorSet, IVulkanContextResource
{
    private readonly VulkanContext _context;
    private readonly DescriptorPool _descriptorPool;
    private readonly DescriptorSet _descriptorSet;
    private readonly DescriptorSetLayout _layout;
    private readonly VulkanDescriptorBindingTable _bindings;
    private bool _disposed;

    public VulkanContext OwnerContext => _context;

    public VulkanDescriptorSet(
        VulkanContext context,
        DescriptorSetLayout layout,
        VulkanDescriptorBindingTable bindings,
        Silk.NET.Vulkan.DescriptorPoolSize[] poolSizes)
    {
        _context = context;
        _layout = layout;
        _bindings = bindings;

        var vk = context.Vk;
        var device = context.Device;

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

    /// <summary>The array element every write on this type targets.</summary>
    /// <remarks><see cref="IDescriptorSet"/> exposes no array element, so each write is the first one.</remarks>
    private const uint FirstArrayElement = 0;

    /// <summary>The number of descriptors every write on this type covers.</summary>
    private const uint SingleDescriptor = 1;

    public DescriptorSet Handle => _descriptorSet;

    public DescriptorSetLayout Layout => _layout;

    public void UpdateBuffer(int binding, IBuffer buffer)
        => UpdateBuffer(binding, buffer, Silk.NET.Vulkan.DescriptorType.UniformBuffer);

    internal void UpdateStorageBuffer(int binding, IBuffer buffer)
        => UpdateBuffer(binding, buffer, Silk.NET.Vulkan.DescriptorType.StorageBuffer);

    private void UpdateBuffer(int binding, IBuffer buffer, Silk.NET.Vulkan.DescriptorType type)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _bindings.ValidateWrite(binding, type, nameof(binding));
        VulkanBuffer vulkanBuffer = _context.RequireOwned<VulkanBuffer>(buffer, nameof(buffer));

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
            DstArrayElement = FirstArrayElement,
            DescriptorCount = SingleDescriptor,
            DescriptorType = type,
            PBufferInfo = &bufferInfo
        };

        _context.Vk.UpdateDescriptorSets(_context.Device, 1, &writeDescriptor, 0, null);
    }

    public void UpdateTexture(int binding, ITexture2D texture, ISampler sampler)
        => UpdateTexture(binding, texture, sampler, ImageLayout.ShaderReadOnlyOptimal);

    internal void UpdateTexture(int binding, ITexture2D texture, ISampler sampler, ImageLayout layout)
        => UpdateCombinedImageSampler(
            binding,
            _context.RequireOwned<VulkanTexture2D>(texture, nameof(texture)).ImageViewHandle,
            sampler, layout);

    internal void UpdateStorageImage(int binding, ITexture2D texture)
    {
        VulkanTexture2D image = _context.RequireOwned<VulkanTexture2D>(texture, nameof(texture));
        if ((image.Usage & ImageUsageFlags.StorageBit) == 0)
            throw new ArgumentException("The image was not created for storage access.", nameof(texture));
        UpdateImage(binding, image.ImageViewHandle, default, ImageLayout.General, Silk.NET.Vulkan.DescriptorType.StorageImage);
    }

    public void UpdateTextureCube(int binding, ITextureCube texture, ISampler sampler)
        => UpdateCombinedImageSampler(
            binding,
            _context.RequireOwned<VulkanTextureCube>(texture, nameof(texture)).ImageViewHandle,
            sampler);

    public void UpdateTextureArray(int binding, ITextureArray texture, ISampler sampler)
        => UpdateCombinedImageSampler(
            binding,
            _context.RequireOwned<VulkanTextureArray>(texture, nameof(texture)).ImageViewHandle,
            sampler);

    public void UpdateTextureCubeArray(int binding, ITextureCubeArray texture, ISampler sampler)
        => UpdateCombinedImageSampler(
            binding,
            _context.RequireOwned<VulkanTextureCubeArray>(texture, nameof(texture)).ImageViewHandle,
            sampler);

    private void UpdateCombinedImageSampler(int binding, ImageView imageView, ISampler sampler,
        ImageLayout layout = ImageLayout.ShaderReadOnlyOptimal)
    {
        VulkanSampler vulkanSampler = _context.RequireOwned<VulkanSampler>(sampler, nameof(sampler));
        UpdateImage(binding, imageView, vulkanSampler.Handle, layout, Silk.NET.Vulkan.DescriptorType.CombinedImageSampler);
    }

    private void UpdateImage(int binding, ImageView imageView, Sampler sampler, ImageLayout layout,
        Silk.NET.Vulkan.DescriptorType type)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bindings.ValidateWrite(binding, type, nameof(binding));

        var imageInfo = new DescriptorImageInfo
        {
            ImageView = imageView,
            ImageLayout = layout,
            Sampler = sampler
        };

        var writeDescriptor = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = (uint)binding,
            DstArrayElement = FirstArrayElement,
            DescriptorCount = SingleDescriptor,
            DescriptorType = type,
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
        DescriptorPool descriptorPool = _descriptorPool;
        _context.DeferRelease(() =>
            _context.Vk.DestroyDescriptorPool(_context.Device, descriptorPool, null));
    }
}

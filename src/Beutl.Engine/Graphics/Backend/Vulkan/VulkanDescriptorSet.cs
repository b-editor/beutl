using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IDescriptorSet"/>.
/// </summary>
/// <remarks>
/// Every operand is resolved through <see cref="VulkanContext.RequireOwned{TResource}"/> before its handle
/// reaches <c>vkUpdateDescriptorSets</c>. A Vulkan handle carries no device provenance, so writing one from
/// another context into this set is undefined behaviour the driver need not diagnose - not merely a
/// validation message - and a plain cast would let it through. The binding a write names is checked the same
/// way, against the <see cref="VulkanDescriptorBindingTable"/> of the layout the set was allocated from: the
/// layout handle carries none of its own declarations, so nothing below this line can tell that a write names
/// a binding the pipeline never declared.
/// </remarks>
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

    /// <summary>The array element every write on this type targets.</summary>
    /// <remarks><see cref="IDescriptorSet"/> exposes no array element, so each write is the first one.</remarks>
    private const uint FirstArrayElement = 0;

    /// <summary>The number of descriptors every write on this type covers.</summary>
    private const uint SingleDescriptor = 1;

    public DescriptorSet Handle => _descriptorSet;

    public DescriptorSetLayout Layout => _layout;

    public void UpdateBuffer(int binding, IBuffer buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _bindings.ValidateWrite(
            binding,
            Silk.NET.Vulkan.DescriptorType.UniformBuffer,
            FirstArrayElement,
            SingleDescriptor,
            nameof(binding));
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
            DescriptorType = Silk.NET.Vulkan.DescriptorType.UniformBuffer,
            PBufferInfo = &bufferInfo
        };

        _context.Vk.UpdateDescriptorSets(_context.Device, 1, &writeDescriptor, 0, null);
    }

    public void UpdateTexture(int binding, ITexture2D texture, ISampler sampler)
        => UpdateCombinedImageSampler(
            binding,
            _context.RequireOwned<VulkanTexture2D>(texture, nameof(texture)).ImageViewHandle,
            sampler);

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

    private void UpdateCombinedImageSampler(int binding, ImageView imageView, ISampler sampler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _bindings.ValidateWrite(
            binding,
            Silk.NET.Vulkan.DescriptorType.CombinedImageSampler,
            FirstArrayElement,
            SingleDescriptor,
            nameof(binding));
        VulkanSampler vulkanSampler = _context.RequireOwned<VulkanSampler>(sampler, nameof(sampler));

        var imageInfo = new DescriptorImageInfo
        {
            ImageView = imageView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            Sampler = vulkanSampler.Handle
        };

        var writeDescriptor = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = (uint)binding,
            DstArrayElement = FirstArrayElement,
            DescriptorCount = SingleDescriptor,
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
        DescriptorPool descriptorPool = _descriptorPool;
        _context.DeferRelease(() =>
            _context.Vk.DestroyDescriptorPool(_context.Device, descriptorPool, null));
    }
}

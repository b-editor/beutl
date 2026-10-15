using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// The image, view and per-layer layout tracking shared by the array, cube and cube-array textures, which
/// differ only in how many layers they hold, how their full view samples them and how a face maps to a layer.
/// </summary>
internal abstract unsafe class VulkanLayeredTexture : IVulkanContextResource
{
    private readonly DeviceMemory _memory;
    private readonly ImageLayout[] _layerLayouts;
    private bool _disposed;

    /// <param name="resourceName">Names the image in a thrown message, e.g. <c>"cube map image"</c>.</param>
    protected VulkanLayeredTexture(
        VulkanContext context,
        int width,
        int height,
        uint layerCount,
        TextureFormat format,
        ImageUsageFlags usage,
        ImageCreateFlags flags,
        ImageViewType viewType,
        string resourceName)
    {
        OwnerContext = context;
        LayerWidth = width;
        LayerHeight = height;
        Format = format;
        _layerLayouts = new ImageLayout[layerCount];

        var vk = context.Vk;
        var device = context.Device;

        ImageHandle = context.CreateTextureImage(
            format, width, height, layerCount, usage, flags, pNext: null, resourceName);

        _memory = context.AllocateAndBindImageMemory(ImageHandle, resourceName, out _);

        Result result = context.TryCreateImageView(
            ImageHandle, format, viewType, baseArrayLayer: 0, layerCount, out ImageView fullView);
        if (result != Result.Success)
        {
            vk.DestroyImage(device, ImageHandle, null);
            vk.FreeMemory(device, _memory, null);
            throw new InvalidOperationException($"Failed to create Vulkan {resourceName} view: {result}");
        }
        ImageViewHandle = fullView;

        // The full view samples every layer whether or not anything writes to it, so each starts shader-readable.
        // A constructor that throws leaves no instance to dispose, so a failed transition releases what was built.
        try
        {
            context.TransitionImageLayout(
                ImageHandle,
                ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal,
                format.GetAspectMask(),
                baseArrayLayer: 0,
                layerCount);
        }
        catch
        {
            ReleaseConstructedHandles(vk, device);
            throw;
        }

        Array.Fill(_layerLayouts, ImageLayout.ShaderReadOnlyOptimal);
    }

    public VulkanContext OwnerContext { get; }

    public TextureFormat Format { get; }

    public Silk.NET.Vulkan.Image ImageHandle { get; }

    public ImageView ImageViewHandle { get; }

    protected int LayerWidth { get; }

    protected int LayerHeight { get; }

    protected uint LayerCount => (uint)_layerLayouts.Length;

    protected static uint ThrowIfEmpty(uint arraySize)
    {
        if (arraySize == 0)
            throw new ArgumentException("Array size must be greater than 0", nameof(arraySize));

        return arraySize;
    }

    // Releases what the constructor created before a later step failed: the full view, the image and, last, its
    // memory.
    private void ReleaseConstructedHandles(Vk vk, Device device)
    {
        vk.DestroyImageView(device, ImageViewHandle, null);
        vk.DestroyImage(device, ImageHandle, null);
        vk.FreeMemory(device, _memory, null);
    }

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    protected void ValidateLayer(uint layerIndex)
    {
        if (layerIndex >= LayerCount)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
    }

    protected void TransitionLayer(uint layerIndex, ImageLayout newLayout)
    {
        ImageLayout oldLayout = _layerLayouts[layerIndex];
        if (oldLayout == newLayout)
            return;

        OwnerContext.TransitionImageLayout(
            ImageHandle,
            oldLayout,
            newLayout,
            Format.GetAspectMask(),
            baseArrayLayer: layerIndex,
            layerCount: 1);
        _layerLayouts[layerIndex] = newLayout;
    }

    /// <summary>The layout the given layer is tracked as being in.</summary>
    /// <remarks>
    /// Every allocated layer must be readable by a sampler even before anything writes to it, because the
    /// full view a shader binds covers all of them.
    /// </remarks>
    internal ImageLayout GetLayerLayout(uint layerIndex)
    {
        ThrowIfDisposed();
        ValidateLayer(layerIndex);

        return _layerLayouts[layerIndex];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ImageView imageView = ImageViewHandle;
        Silk.NET.Vulkan.Image image = ImageHandle;
        DeviceMemory memory = _memory;
        VulkanContext context = OwnerContext;
        context.DeferRelease(() =>
        {
            var vk = context.Vk;
            var device = context.Device;

            vk.DestroyImageView(device, imageView, null);
            vk.DestroyImage(device, image, null);
            vk.FreeMemory(device, memory, null);
        });
    }
}

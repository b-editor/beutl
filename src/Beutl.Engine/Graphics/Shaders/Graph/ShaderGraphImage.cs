using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;
using SkiaSharp;
using NativeImage = Silk.NET.Vulkan.Image;

namespace Beutl.Graphics.Shaders;

/// <summary>Owns image storage and its views. Layouts belong to subresources, not to view handles.</summary>
internal sealed unsafe class ShaderGraphImage : IDisposable
{
    private readonly VulkanContext _context;
    private readonly VulkanTexture2D? _borrowed;
    private readonly DeviceMemory _memory;
    private readonly ImageLayout[,] _layouts;
    private readonly Dictionary<(int Mip, int Mips, int Layer, int Layers, ImageKind Kind, bool Attachment), View> _views = [];
    private bool _disposed;

    internal ShaderGraphImage(VulkanContext context, ImageDescription description, ImageUsageFlags usage,
        VulkanTexture2D? borrowed = null)
    {
        _context = context;
        Description = description;
        Usage = usage;
        _borrowed = borrowed;
        _layouts = new ImageLayout[description.MipLevels, description.ArrayLayers];
        if (borrowed is not null)
        {
            Handle = borrowed.ImageHandle;
            return;
        }
        Validate(context, description, usage);
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = description.Kind == ImageKind.Image3D ? ImageType.Type3D : ImageType.Type2D,
            Format = description.Format.ToVulkanFormat(),
            Extent = new((uint)description.Width, (uint)description.Height, (uint)description.Depth),
            MipLevels = (uint)description.MipLevels, ArrayLayers = (uint)description.ArrayLayers,
            Samples = SampleCountFlags.Count1Bit, Tiling = ImageTiling.Optimal,
            Usage = usage, SharingMode = SharingMode.Exclusive,
            Flags = CubeFlags(description), InitialLayout = ImageLayout.Undefined,
        };
        NativeImage image;
        Result result = context.Vk.CreateImage(context.Device, &info, null, &image);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create graph image: {result}.");
        Handle = image;
        _memory = context.AllocateAndBindImageMemory(image, "graph image", out ulong bytes);
        AllocationSize = bytes;
    }

    internal NativeImage Handle { get; }
    internal ImageDescription Description { get; }
    internal ImageUsageFlags Usage { get; }
    internal ulong AllocationSize { get; }

    private static ImageCreateFlags CubeFlags(ImageDescription description)
        => description.Kind is ImageKind.Cube or ImageKind.CubeArray ? ImageCreateFlags.CreateCubeCompatibleBit : 0;

    internal static void Validate(VulkanContext context, ImageDescription description, ImageUsageFlags usage)
    {
        Result result = context.Vk.GetPhysicalDeviceImageFormatProperties(context.PhysicalDevice,
            description.Format.ToVulkanFormat(), description.Kind == ImageKind.Image3D ? ImageType.Type3D : ImageType.Type2D,
            ImageTiling.Optimal, usage, CubeFlags(description), out ImageFormatProperties properties);
        if (result != Result.Success || description.Width > properties.MaxExtent.Width
            || description.Height > properties.MaxExtent.Height || description.Depth > properties.MaxExtent.Depth
            || description.MipLevels > properties.MaxMipLevels || description.ArrayLayers > properties.MaxArrayLayers)
            throw new NotSupportedException($"The device does not support {description} with {usage}.");
    }

    internal VulkanTexture2D GetView(ShaderImage image, bool attachment = false)
    {
        var key = (image.BaseMipLevel, image.MipLevels, image.BaseArrayLayer, image.ArrayLayers, image.Kind, attachment);
        if (_views.TryGetValue(key, out View? existing))
            return existing;
        var range = Range(image);
        if (!attachment && image.Format.IsDepthFormat())
            range.AspectMask = ImageAspectFlags.DepthBit;
        var info = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo, Image = Handle, Format = image.Format.ToVulkanFormat(),
            ViewType = image.Kind switch
            {
                ImageKind.Image2D => ImageViewType.Type2D,
                ImageKind.Image2DArray => ImageViewType.Type2DArray,
                ImageKind.Image3D => ImageViewType.Type3D,
                ImageKind.Cube => ImageViewType.TypeCube,
                ImageKind.CubeArray => ImageViewType.TypeCubeArray,
                _ => throw new ArgumentOutOfRangeException(nameof(image)),
            },
            SubresourceRange = range,
        };
        ImageView handle;
        Result result = _context.Vk.CreateImageView(_context.Device, &info, null, &handle);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create image view '{image.Name}': {result}.");
        var view = new View(this, image, handle);
        _views.Add(key, view);
        return view;
    }

    internal static ImageSubresourceRange Range(ShaderImage image) => new(image.Format.GetAspectMask(),
        (uint)image.BaseMipLevel, (uint)image.MipLevels, (uint)image.BaseArrayLayer, (uint)image.ArrayLayers);

    internal Extent3D Extent(int mip) => new((uint)Math.Max(1, Description.Width >> mip),
        (uint)Math.Max(1, Description.Height >> mip), (uint)Math.Max(1, Description.Depth >> mip));

    internal void Transition(ShaderImage image, ImageLayout layout)
        => Transition(Range(image), layout);

    internal void Transition(ImageSubresourceRange range, ImageLayout layout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_borrowed is not null)
        {
            _borrowed.TransitionTo(layout);
            return;
        }
        _context.ThrowIfRenderPassActive();
        for (uint mip = range.BaseMipLevel; mip < range.BaseMipLevel + range.LevelCount; mip++)
        for (uint layer = range.BaseArrayLayer; layer < range.BaseArrayLayer + range.LayerCount; layer++)
        {
            ImageLayout previous = _layouts[mip, layer];
            if (previous == layout)
                continue;
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = previous, NewLayout = layout, Image = Handle,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                SrcAccessMask = previous == ImageLayout.Undefined ? 0 : AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
                DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
                SubresourceRange = new(Description.Format.GetAspectMask(), mip, 1, layer, 1),
            };
            _context.Vk.CmdPipelineBarrier(_context.GetRecordingCommandBuffer(),
                previous == ImageLayout.Undefined ? PipelineStageFlags.TopOfPipeBit : PipelineStageFlags.AllCommandsBit,
                PipelineStageFlags.AllCommandsBit, 0, 0, null, 0, null, 1, &barrier);
            _layouts[mip, layer] = layout;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (View view in _views.Values) view.Dispose();
        if (_borrowed is null)
            _context.DeferRelease(() =>
            {
                _context.Vk.DestroyImage(_context.Device, Handle, null);
                _context.Vk.FreeMemory(_context.Device, _memory, null);
            });
    }

    private sealed class View : VulkanTexture2D
    {
        private readonly ShaderGraphImage _owner;
        private readonly ShaderImage _description;

        internal View(ShaderGraphImage owner, ShaderImage image, ImageView handle)
            : base(owner._context, (int)owner.Extent(image.BaseMipLevel).Width,
                (int)owner.Extent(image.BaseMipLevel).Height, image.Format, owner.Handle, handle, owner.Usage)
            => (_owner, _description) = (owner, image);

        public override void TransitionTo(ImageLayout layout) => _owner.Transition(_description, layout);
        public override SKSurface CreateSkiaSurface() => throw new NotSupportedException("Graph image views are used inside shader passes.");

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _context.DeferRelease(() => _context.Vk.DestroyImageView(_context.Device, _imageView, null));
        }
    }
}

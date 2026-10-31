using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Shaders;

internal static unsafe class ShaderGraphTransfers
{
    internal static void Validate(VulkanContext context, ShaderTransfer transfer, IReadOnlyDictionary<int, ShaderImageFrame> frames)
    {
        if (transfer.Kind == ShaderTransferKind.CopyImage
            && frames[transfer.Source!.Id].PixelSize != frames[transfer.Destination.Id].PixelSize)
            throw new ArgumentException("Image copies require equal texel extents.");
        if (transfer.Kind == ShaderTransferKind.GenerateMipmaps)
        {
            var image = (ShaderImage)transfer.Destination;
            var required = FormatFeatureFlags.BlitSrcBit | FormatFeatureFlags.BlitDstBit;
            if (transfer.Filter == SamplerFilter.Linear) required |= FormatFeatureFlags.SampledImageFilterLinearBit;
            if (!ShaderGraphPipeline.SupportsOptimalTiling(context, image.Format, required))
                throw new NotSupportedException($"Format {image.Format} does not support mip generation with {transfer.Filter} filtering.");
        }
    }

    internal static void Execute(VulkanContext context, ShaderTransfer transfer,
        IReadOnlyDictionary<int, ShaderGraphImage> images, IReadOnlyDictionary<int, IBuffer> buffers)
    {
        ShaderGraphPipeline.SynchronizeResources(context);
        CommandBuffer command = context.GetRecordingCommandBuffer();
        if (transfer.Destination is ShaderBuffer buffer)
        {
            VulkanBuffer destination = context.RequireOwned<VulkanBuffer>(buffers[buffer.Id], nameof(buffers));
            if (transfer.Kind == ShaderTransferKind.ClearBuffer)
                context.Vk.CmdFillBuffer(command, destination.Handle, 0, (ulong)buffer.ByteLength, transfer.Value);
            else
            {
                VulkanBuffer source = context.RequireOwned<VulkanBuffer>(buffers[transfer.Source!.Id], nameof(buffers));
                var region = new BufferCopy(0, 0, (ulong)buffer.ByteLength);
                context.Vk.CmdCopyBuffer(command, source.Handle, destination.Handle, 1, &region);
            }
            return;
        }
        var image = (ShaderImage)transfer.Destination;
        ShaderGraphImage owner = images[image.Root.Id];
        ImageSubresourceRange range = ShaderGraphImage.Range(image);
        if (transfer.Kind == ShaderTransferKind.GenerateMipmaps)
        {
            for (int level = image.BaseMipLevel + 1; level < image.BaseMipLevel + image.MipLevels; level++)
            {
                var previous = new ImageSubresourceRange(range.AspectMask, (uint)level - 1, 1, range.BaseArrayLayer, range.LayerCount);
                var next = previous with { BaseMipLevel = (uint)level };
                owner.Transition(previous, ImageLayout.TransferSrcOptimal);
                owner.Transition(next, ImageLayout.TransferDstOptimal);
                Extent3D sourceExtent = owner.Extent(level - 1), destinationExtent = owner.Extent(level);
                var blit = new ImageBlit
                {
                    SrcSubresource = Layers(image, level - 1, range.AspectMask),
                    DstSubresource = Layers(image, level, range.AspectMask),
                };
                blit.SrcOffsets[1] = new((int)sourceExtent.Width, (int)sourceExtent.Height, (int)sourceExtent.Depth);
                blit.DstOffsets[1] = new((int)destinationExtent.Width, (int)destinationExtent.Height, (int)destinationExtent.Depth);
                context.Vk.CmdBlitImage(command, owner.Handle, ImageLayout.TransferSrcOptimal,
                    owner.Handle, ImageLayout.TransferDstOptimal, 1, &blit,
                    transfer.Filter == SamplerFilter.Linear ? Filter.Linear : Filter.Nearest);
            }
            return;
        }
        owner.Transition(range, ImageLayout.TransferDstOptimal);
        switch (transfer.Kind)
        {
            case ShaderTransferKind.ClearImage:
                var color = new ClearColorValue
                {
                    Uint32_0 = transfer.Color.X,
                    Uint32_1 = transfer.Color.Y,
                    Uint32_2 = transfer.Color.Z,
                    Uint32_3 = transfer.Color.W,
                };
                context.Vk.CmdClearColorImage(command, owner.Handle, ImageLayout.TransferDstOptimal, &color, 1, &range);
                break;
            case ShaderTransferKind.ClearDepth:
                var depth = new ClearDepthStencilValue(transfer.Depth, transfer.Value);
                context.Vk.CmdClearDepthStencilImage(command, owner.Handle, ImageLayout.TransferDstOptimal, &depth, 1, &range);
                break;
            case ShaderTransferKind.CopyImage:
                var sourceImage = (ShaderImage)transfer.Source!;
                ShaderGraphImage sourceOwner = images[sourceImage.Root.Id];
                sourceOwner.Transition(sourceImage, ImageLayout.TransferSrcOptimal);
                foreach (ImageAspectFlags aspect in new[] { ImageAspectFlags.ColorBit, ImageAspectFlags.DepthBit, ImageAspectFlags.StencilBit })
                {
                    if ((range.AspectMask & aspect) == 0) continue;
                    for (int level = 0; level < image.MipLevels; level++)
                    {
                        var copy = new ImageCopy
                        {
                            SrcSubresource = Layers(sourceImage, sourceImage.BaseMipLevel + level, aspect),
                            DstSubresource = Layers(image, image.BaseMipLevel + level, aspect),
                            Extent = owner.Extent(image.BaseMipLevel + level),
                        };
                        context.Vk.CmdCopyImage(command, sourceOwner.Handle, ImageLayout.TransferSrcOptimal,
                            owner.Handle, ImageLayout.TransferDstOptimal, 1, &copy);
                    }
                }
                break;
        }
    }

    private static ImageSubresourceLayers Layers(ShaderImage image, int mip, ImageAspectFlags aspect)
        => new(aspect, (uint)mip, (uint)image.BaseArrayLayer, (uint)image.ArrayLayers);
}

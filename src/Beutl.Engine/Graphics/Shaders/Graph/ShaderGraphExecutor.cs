using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using ImageUsageFlags = Silk.NET.Vulkan.ImageUsageFlags;

namespace Beutl.Graphics.Shaders;

// ReferenceBounds retains the complete input group when an effect receives several materialized pieces.
internal readonly record struct ShaderImageFrame(Rect Bounds, Rect RasterBounds, PixelSize PixelSize, float Scale, Rect? ReferenceBounds = null)
{
    public Vector4[] GetParameters() =>
    [
        new(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height),
        new(RasterBounds.X, RasterBounds.Y, RasterBounds.Width, RasterBounds.Height),
        new(PixelSize.Width, PixelSize.Height, Scale, 0),
    ];
}

internal readonly record struct ShaderGraphExecutionStatistics(int ProgramCacheHits, int ExecutedPasses,
    long PeakResourceBytes, int ImageAllocations, int BufferAllocations, int ResourceReuses, long ScheduledPixels);

internal static class ShaderGraphExecutor
{
    // This is an active-allocation ceiling, not the target pool's retained-cache budget.
    internal const long MaximumTransientBytes = 256L * 1024 * 1024;

    internal static long EstimateImageBytes(ImageDescription description)
    {
        long bytes = 0;
        for (int mip = 0; mip < description.MipLevels; mip++)
            bytes = checked(bytes + (long)Math.Max(1, description.Width >> mip)
                * Math.Max(1, description.Height >> mip) * Math.Max(1, description.Depth >> mip)
                * description.ArrayLayers * description.Format.BytesPerTexel());
        return bytes;
    }

    private static readonly ShaderProgram s_copy = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(set = 0, binding = 0) uniform sampler2D image;
        layout(push_constant) uniform Bounds { vec4 source; vec4 destination; } bounds;
        void main() {
            vec2 position = bounds.destination.xy + uv * bounds.destination.zw;
            color = texture(image, (position - bounds.source.xy) / bounds.source.zw);
        }
        """));

    public static ShaderGraphExecutionStatistics Execute(ShaderGraphDescription graph, ITexture2D source, ShaderImageFrame sourceFrame,
        ITexture2D output, float scale, Vector gridOffset, ShaderGraphProgramAcquirer acquireProgram, Rect? requiredOutput = null)
    {
        VulkanContext context = GLSLFilterPipeline.TryResolveVulkan(GraphicsContextFactory.SharedContext)
            ?? throw new NotSupportedException("Shader graphs require a Vulkan rendering context.");
        var images = new Dictionary<int, ITexture2D>();
        var buffers = new Dictionary<int, IBuffer>();
        var storage = new Dictionary<int, ShaderGraphImage>();
        var ownedStorage = new HashSet<ShaderGraphImage>();
        var available = new List<ShaderGraphImage>();
        var ownedBuffers = new HashSet<VulkanBuffer>();
        var availableBuffers = new List<VulkanBuffer>();
        var leases = new List<ProgramCacheLease<ShaderGraphPipeline>>();
        var programs = new Dictionary<ShaderPassDescription, ShaderGraphPipeline>();
        var regions = new ShaderGraphRegions(graph, sourceFrame, scale, gridOffset,
            requiredOutput ?? graph.TransformBounds(sourceFrame.Bounds, sourceFrame.ReferenceBounds));
        Dictionary<int, ShaderImageFrame> frames = regions.Frames;
        var descriptions = new Dictionary<int, ImageDescription>();
        long liveBytes = 0;
        long peakBytes = 0, scheduledPixels = 0;
        int imageAllocations = 0, bufferAllocations = 0, reuses = 0, executedPasses = 0;
        ImageUsageFlags[] usages = [.. graph.ImageUsages];
        try
        {
            foreach (ShaderImage image in graph.Resources.OfType<ShaderImage>())
            {
                if (image.Id != 0 && graph.LastUses[image.Id] < 0) continue;
                if (!image.IsView)
                {
                    ShaderImageFrame frame = frames[image.Id];
                    ImageDescription description = image.Description ?? new(ImageKind.Image2D,
                        frame.PixelSize.Width, frame.PixelSize.Height, image.Format);
                    descriptions.Add(image.Id, description);
                    if (usages[image.Id] != 0)
                        ShaderGraphImage.Validate(context, description, usages[image.Id]);
                }
            }

            // Preflight the complete graph before executing its first command.
            foreach (ShaderPassDescription pass in graph.Passes)
            {
                if (pass.Transfer is { } transfer)
                {
                    ShaderGraphTransfers.Validate(context, transfer, frames);
                    continue;
                }
                ProgramCacheLease<ShaderGraphPipeline> lease = acquireProgram(context, pass);
                leases.Add(lease);
                programs.Add(pass, lease.Program);
                lease.Program.Validate(pass);
                ShaderImage[] attachments = pass.Attachments.Select(a => a.Image)
                    .Concat(pass.DepthAttachment is { } depth ? [depth.Image] : []).ToArray();
                if (attachments.Length > 0)
                {
                    ShaderImageFrame frame = frames[attachments[0].Id];
                    if (attachments.Any(a => frames[a.Id] != frame))
                        throw new InvalidOperationException($"Pass '{pass.Name}' has mismatched attachment bounds or resolutions.");
                    if (frame.PixelSize.Width > context.MaxFramebufferWidth || frame.PixelSize.Height > context.MaxFramebufferHeight)
                        throw new NotSupportedException($"Pass '{pass.Name}' exceeds framebuffer dimensions.");
                }
            }

            // The boundary conversion is also preflighted, before any authored pass executes.
            ShaderImage selected = graph.Output.RenderingImage;
            Rect selectedRaster = frames[selected.Id].RasterBounds;
            Rect outputBounds = graph.TransformBounds(sourceFrame.Bounds, sourceFrame.ReferenceBounds);
            PixelRect outputPixels = PixelRect.FromRect(outputBounds.Translate(gridOffset), scale);
            Vector4[] mapping =
            [
                // Form the relative origin before shader interpolation. Adding and subtracting world
                // coordinates in float32 otherwise gives translated copies different filtering weights.
                new(0, 0, selectedRaster.Width, selectedRaster.Height),
                new(outputPixels.X / scale - gridOffset.X - selectedRaster.X,
                    outputPixels.Y / scale - gridOffset.Y - selectedRaster.Y,
                    outputPixels.Width / scale, outputPixels.Height / scale),
            ];
            var resultImage = new ShaderImage(graph, -1, "result", TextureFormat.RGBA16Float, null, null, 0, 1);
            var copy = new ShaderPassDescription("output", s_copy,
                [new(0, selected, DescriptorType.CombinedImageSampler, ResourceAccess.Read, SamplerState.LinearClamp, false)],
                [new(0, resultImage, AttachmentLoadOp.Clear)], [.. MemoryMarshal.AsBytes(mapping.AsSpan())],
                BlendState.Replace, (3, 1, 1), false);
            ProgramCacheLease<ShaderGraphPipeline> copyLease = acquireProgram(context, copy);
            leases.Add(copyLease);

            VulkanTexture2D sourceTexture = context.RequireOwned<VulkanTexture2D>(source, nameof(source));
            if ((usages[0] & ~sourceTexture.Usage) != 0)
            {
                usages[0] |= ImageUsageFlags.TransferDstBit;
                Allocate(graph.Resources[0]);
                context.CopyTexture(source, images[0]);
            }
            else
                storage.Add(0, new ShaderGraphImage(context, descriptions[0], sourceTexture.Usage, sourceTexture));
            ownedStorage.UnionWith(storage.Values);

            for (int index = 0; index < graph.Passes.Length; index++)
            {
                ShaderPassDescription pass = graph.Passes[index];
                if (regions.PassRegions[index] is { IsEmpty: true })
                {
                    // Only independently evaluated color passes can be omitted.
                }
                else if (pass.Transfer is { } transfer)
                {
                    if (transfer.Source is { } src) Allocate(src, createView: false);
                    Allocate(transfer.Destination, createView: false);
                    ShaderGraphTransfers.Execute(context, transfer, storage, buffers);
                    executedPasses++;
                }
                else
                {
                    foreach (ShaderPassBinding binding in pass.Bindings)
                        if (!binding.Metadata) Allocate(binding.Resource);
                    foreach (ShaderColorAttachment attachment in pass.Attachments) Allocate(attachment.Image, attachment: true);
                    if (pass.DepthAttachment is { } depth) Allocate(depth.Image, attachment: true);
                    foreach (ShaderBuffer buffer in pass.Draw.ReadBuffers) Allocate(buffer);
                    PixelRect? scissor = null;
                    if (!pass.Attachments.IsEmpty)
                    {
                        ShaderImageFrame frame = frames[pass.Attachments[0].Image.Id];
                        PixelRect area = regions.PassRegions[index] is { } region
                            ? ShaderGraphRegions.Scissor(frame, region) : new(0, 0, frame.PixelSize.Width, frame.PixelSize.Height);
                        scheduledPixels += (long)area.Width * area.Height;
                        if (regions.PassRegions[index].HasValue) scissor = area;
                    }
                    programs[pass].Execute(pass, images, buffers, frames, scissor);
                    executedPasses++;
                }
                foreach (ShaderResource resource in graph.Resources)
                    if (graph.LastUses[resource.Id] == index) Release(resource);
            }

            // Normalize the selected image into the renderer's linear-premultiplied RGBA target. All
            // intermediate formats remain untouched, including scalar data attachments.
            Allocate(selected);
            images.Add(-1, output);
            copyLease.Program.Execute(copy, images, buffers, frames);
            return new(leases.Count(lease => lease.IsCacheHit), executedPasses, peakBytes,
                imageAllocations, bufferAllocations, reuses, scheduledPixels);
        }
        finally
        {
            // Submission establishes queue order before returning pooled targets or backend-shared images.
            // Native resource disposal is deferred by VulkanContext until recorded work completes.
            try
            {
                context.FlushCommands(waitForCompletion: false);
            }
            finally
            {
                foreach (ShaderGraphImage image in ownedStorage) image.Dispose();
                foreach (VulkanBuffer buffer in ownedBuffers)
                    buffer.Dispose();
                foreach (ProgramCacheLease<ShaderGraphPipeline> lease in leases)
                    lease.Dispose();
            }
        }

        void Allocate(ShaderResource resource, bool attachment = false, bool createView = true)
        {
            if (resource is ShaderImage image)
            {
                ShaderImage root = image.Root;
                if (!storage.TryGetValue(root.Id, out ShaderGraphImage? owner))
                {
                    ImageDescription description = descriptions[root.Id];
                    int reuseIndex = available.FindIndex(i => i.Description == description && (i.Usage & usages[root.Id]) == usages[root.Id]);
                    if (reuseIndex >= 0)
                    {
                        owner = available[reuseIndex];
                        available.RemoveAt(reuseIndex);
                        reuses++;
                    }
                    else
                    {
                        CheckBudget(EstimateImageBytes(description), resource.Name);
                        owner = new ShaderGraphImage(context, description, usages[root.Id]);
                        ownedStorage.Add(owner);
                        CheckBudget(checked((long)owner.AllocationSize), resource.Name);
                        liveBytes = checked(liveBytes + (long)owner.AllocationSize);
                        peakBytes = Math.Max(peakBytes, liveBytes);
                        imageAllocations++;
                    }
                    storage.Add(root.Id, owner);
                }
                if (createView) images[image.Id] = owner.GetView(image, attachment);
            }
            else if (resource is ShaderBuffer buffer && !buffers.ContainsKey(buffer.Id))
            {
                // Host writes cannot overwrite bytes still read by earlier queued commands. Reuse
                // only when a later GPU pass initializes the new logical buffer's contents.
                int reuseIndex = buffer.InitialData.IsEmpty
                    ? availableBuffers.FindIndex(b => b.Size == (ulong)buffer.ByteLength) : -1;
                VulkanBuffer allocated;
                if (reuseIndex >= 0)
                {
                    allocated = availableBuffers[reuseIndex];
                    availableBuffers.RemoveAt(reuseIndex);
                    reuses++;
                }
                else
                {
                    CheckBudget(buffer.ByteLength, buffer.Name);
                    allocated = (VulkanBuffer)context.CreateBuffer((ulong)buffer.ByteLength,
                        BufferUsage.StorageBuffer | BufferUsage.UniformBuffer | BufferUsage.VertexBuffer | BufferUsage.IndexBuffer
                            | BufferUsage.IndirectBuffer | BufferUsage.TransferSource | BufferUsage.TransferDestination,
                        MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
                    ownedBuffers.Add(allocated);
                    CheckBudget(checked((long)allocated.AllocationSize), buffer.Name);
                    liveBytes = checked(liveBytes + (long)allocated.AllocationSize);
                    peakBytes = Math.Max(peakBytes, liveBytes);
                    bufferAllocations++;
                }
                buffers.Add(buffer.Id, allocated);
                if (!buffer.InitialData.IsEmpty) allocated.Upload<byte>(buffer.InitialData.AsSpan());
            }
        }

        void CheckBudget(long bytes, string name)
        {
            if (bytes > MaximumTransientBytes - liveBytes && (available.Count != 0 || availableBuffers.Count != 0))
            {
                // Reclaim retired, incompatible allocations only under pressure. Submission completion
                // is required before their deferred native releases actually reduce memory usage.
                foreach (ShaderGraphImage image in available)
                {
                    image.Dispose();
                    ownedStorage.Remove(image);
                    liveBytes -= checked((long)image.AllocationSize);
                }
                foreach (VulkanBuffer buffer in availableBuffers)
                {
                    buffer.Dispose();
                    ownedBuffers.Remove(buffer);
                    liveBytes -= checked((long)buffer.AllocationSize);
                }
                available.Clear();
                availableBuffers.Clear();
                context.FlushCommands(waitForCompletion: true);
            }
            if (bytes > MaximumTransientBytes - liveBytes)
                throw new InvalidOperationException($"Shader graph resource '{name}' exceeds the {MaximumTransientBytes}-byte active allocation budget.");
        }

        void Release(ShaderResource resource)
        {
            images.Remove(resource.Id);
            if (resource is ShaderImage { IsView: false } image && image.Id != 0
                && storage.Remove(image.Id, out ShaderGraphImage? owner)) available.Add(owner);
            if (buffers.Remove(resource.Id, out IBuffer? buffer))
                availableBuffers.Add((VulkanBuffer)buffer);
        }
    }
}

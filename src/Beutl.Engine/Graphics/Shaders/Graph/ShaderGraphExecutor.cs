using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using ImageUsageFlags = Silk.NET.Vulkan.ImageUsageFlags;

namespace Beutl.Graphics.Shaders;

internal readonly record struct ShaderImageFrame(Rect Bounds, Rect RasterBounds, PixelSize PixelSize, float Scale)
{
    public Vector4[] GetParameters() =>
    [
        new(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height),
        new(RasterBounds.X, RasterBounds.Y, RasterBounds.Width, RasterBounds.Height),
        new(PixelSize.Width, PixelSize.Height, Scale, 0),
    ];
}

internal static class ShaderGraphExecutor
{
    // This is an active-allocation ceiling, not the target pool's retained-cache budget.
    internal const long MaximumTransientBytes = 256L * 1024 * 1024;

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

    public static int Execute(ShaderGraphDescription graph, ITexture2D source, ShaderImageFrame sourceFrame,
        ITexture2D output, float scale, Vector gridOffset, ShaderGraphProgramAcquirer acquireProgram)
    {
        VulkanContext context = GLSLFilterPipeline.TryResolveVulkan(GraphicsContextFactory.SharedContext)
            ?? throw new NotSupportedException("Shader graphs require a Vulkan rendering context.");
        var images = new Dictionary<int, ITexture2D>();
        var buffers = new Dictionary<int, IBuffer>();
        var storage = new Dictionary<int, ShaderGraphImage>();
        var ownedStorage = new HashSet<ShaderGraphImage>();
        var available = new List<ShaderGraphImage>();
        var leases = new List<ProgramCacheLease<ShaderGraphPipeline>>();
        var programs = new Dictionary<ShaderPassDescription, ShaderGraphPipeline>();
        var frames = new Dictionary<int, ShaderImageFrame> { [0] = sourceFrame };
        var descriptions = new Dictionary<int, ImageDescription>();
        long liveBytes = 0;
        var usages = graph.Resources.OfType<ShaderImage>().Where(i => !i.IsView)
            .ToDictionary(image => image.Id, _ => (ImageUsageFlags)0);
        foreach (ShaderPassDescription pass in graph.Passes)
        {
            foreach (ShaderPassBinding binding in pass.Bindings)
                if (!binding.Metadata && binding.Resource is ShaderImage image)
                    usages[image.Root.Id] |= binding.Type == DescriptorType.StorageImage
                        ? ImageUsageFlags.StorageBit : ImageUsageFlags.SampledBit;
            foreach (ShaderColorAttachment attachment in pass.Attachments)
                usages[attachment.Image.Root.Id] |= ImageUsageFlags.ColorAttachmentBit;
            if (pass.DepthAttachment is { } depth)
                usages[depth.Image.Root.Id] |= ImageUsageFlags.DepthStencilAttachmentBit;
            if (pass.Transfer is { } transfer)
            {
                if (transfer.Source is ShaderImage src) usages[src.Root.Id] |= ImageUsageFlags.TransferSrcBit;
                if (transfer.Destination is ShaderImage dst) usages[dst.Root.Id] |= ImageUsageFlags.TransferDstBit;
            }
        }
        usages[graph.Output.Image.Root.Id] |= ImageUsageFlags.SampledBit;
        try
        {
            foreach (ShaderImage image in graph.Resources.OfType<ShaderImage>())
            {
                if (image.Id != 0 && graph.LastUses[image.Id] < 0) continue;
                if (image.Id != 0)
                {
                    Rect bounds = image.ResolveBounds(sourceFrame.Bounds);
                    RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(graph));
                    float density = image.Description.HasValue ? 1 : scale * image.ResolutionScale;
                    PixelRect pixels = image.Description is { } fixedSize
                        ? new PixelRect(0, 0, fixedSize.Width, fixedSize.Height)
                        : PixelRect.FromRect(bounds.Translate(gridOffset), density);
                    if (pixels.Width <= 0 || pixels.Height <= 0)
                        throw new InvalidOperationException($"Image '{image.Name}' has empty dimensions.");
                    frames.Add(image.Id, new(bounds, image.Description.HasValue ? bounds
                        : new Rect(pixels.X / density - gridOffset.X, pixels.Y / density - gridOffset.Y,
                            pixels.Width / density, pixels.Height / density), pixels.Size, density));
                }
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
            ShaderImage selected = graph.Output.Image;
            Rect selectedRaster = frames[selected.Id].RasterBounds;
            Rect outputBounds = graph.TransformBounds(sourceFrame.Bounds);
            PixelRect outputPixels = PixelRect.FromRect(outputBounds.Translate(gridOffset), scale);
            Vector4[] mapping =
            [
                new(selectedRaster.X, selectedRaster.Y, selectedRaster.Width, selectedRaster.Height),
                new(outputPixels.X / scale - gridOffset.X, outputPixels.Y / scale - gridOffset.Y,
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
                if (pass.Transfer is { } transfer)
                {
                    if (transfer.Source is { } src) Allocate(src, createView: false);
                    Allocate(transfer.Destination, createView: false);
                    ShaderGraphTransfers.Execute(context, transfer, storage, buffers);
                }
                else
                {
                    foreach (ShaderPassBinding binding in pass.Bindings)
                        if (!binding.Metadata) Allocate(binding.Resource);
                    foreach (ShaderColorAttachment attachment in pass.Attachments) Allocate(attachment.Image, attachment: true);
                    if (pass.DepthAttachment is { } depth) Allocate(depth.Image, attachment: true);
                    foreach (ShaderBuffer buffer in pass.Draw.ReadBuffers) Allocate(buffer);
                    programs[pass].Execute(pass, images, buffers, frames);
                }
                foreach (ShaderResource resource in graph.Resources)
                    if (graph.LastUses[resource.Id] == index) Release(resource);
            }

            // Normalize the selected image into the renderer's linear-premultiplied RGBA target. All
            // intermediate formats remain untouched, including scalar data attachments.
            Allocate(selected);
            images.Add(-1, output);
            copyLease.Program.Execute(copy, images, buffers, frames);
            return leases.Count(lease => lease.IsCacheHit);
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
                foreach (IBuffer buffer in buffers.Values)
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
                    }
                    else
                    {
                        long estimate = 0;
                        for (int mip = 0; mip < description.MipLevels; mip++)
                            estimate = checked(estimate + (long)Math.Max(1, description.Width >> mip)
                                * Math.Max(1, description.Height >> mip) * Math.Max(1, description.Depth >> mip)
                                * description.ArrayLayers * description.Format.BytesPerTexel());
                        CheckBudget(estimate, resource.Name);
                        owner = new ShaderGraphImage(context, description, usages[root.Id]);
                        ownedStorage.Add(owner);
                        CheckBudget(checked((long)owner.AllocationSize), resource.Name);
                        liveBytes = checked(liveBytes + (long)owner.AllocationSize);
                    }
                    storage.Add(root.Id, owner);
                }
                if (createView) images[image.Id] = owner.GetView(image, attachment);
            }
            else if (resource is ShaderBuffer buffer && !buffers.ContainsKey(buffer.Id))
            {
                CheckBudget(buffer.ByteLength, buffer.Name);
                IBuffer allocated = context.CreateBuffer((ulong)buffer.ByteLength,
                    BufferUsage.StorageBuffer | BufferUsage.UniformBuffer | BufferUsage.VertexBuffer | BufferUsage.IndexBuffer
                        | BufferUsage.IndirectBuffer | BufferUsage.TransferSource | BufferUsage.TransferDestination,
                    MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
                buffers.Add(buffer.Id, allocated);
                if (!buffer.InitialData.IsEmpty) allocated.Upload<byte>(buffer.InitialData.AsSpan());
                liveBytes = checked(liveBytes + buffer.ByteLength);
            }
        }

        void CheckBudget(long bytes, string name)
        {
            if (bytes > MaximumTransientBytes - liveBytes)
                throw new InvalidOperationException($"Shader graph resource '{name}' exceeds the {MaximumTransientBytes}-byte active allocation budget.");
        }

        void Release(ShaderResource resource)
        {
            images.Remove(resource.Id);
            if (resource is ShaderImage { IsView: false } image && image.Id != 0
                && storage.Remove(image.Id, out ShaderGraphImage? owner)) available.Add(owner);
            if (buffers.Remove(resource.Id, out IBuffer? buffer))
                buffer.Dispose(); // Deferred destruction still occupies memory until submission completes.
        }
    }
}

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
        var images = new Dictionary<int, ITexture2D> { [0] = source };
        var buffers = new Dictionary<int, IBuffer>();
        var ownedImages = new HashSet<int>();
        var leases = new List<ProgramCacheLease<ShaderGraphPipeline>>();
        var frames = new Dictionary<int, ShaderImageFrame> { [0] = sourceFrame };
        long liveBytes = 0;
        var availableImages = new List<ITexture2D>();
        var sizes = new Dictionary<int, long>();
        var usages = graph.Resources.OfType<ShaderImage>().ToDictionary(image => image.Id, _ => (ImageUsageFlags)0);
        foreach (ShaderPassDescription pass in graph.Passes)
        {
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                if (!binding.Metadata && binding.Resource is ShaderImage image)
                    usages[image.Id] |= binding.Type == DescriptorType.StorageImage
                        ? ImageUsageFlags.StorageBit : ImageUsageFlags.SampledBit;
            }
            foreach (ShaderColorAttachment attachment in pass.Attachments)
                usages[attachment.Image.Id] |= ImageUsageFlags.ColorAttachmentBit;
        }
        usages[graph.Output.Image.Id] |= ImageUsageFlags.SampledBit;
        try
        {
            foreach (ShaderResource resource in graph.Resources)
            {
                if (resource.Id == 0 || graph.LastUses[resource.Id] < 0)
                    continue;
                if (resource is ShaderImage image)
                {
                    Rect bounds = image.ResolveBounds(sourceFrame.Bounds);
                    RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(graph));
                    float density = scale * image.ResolutionScale;
                    PixelRect pixels = PixelRect.FromRect(bounds.Translate(gridOffset), density);
                    int limit = (usages[image.Id] & ImageUsageFlags.ColorAttachmentBit) != 0
                        ? context.MaxAttachmentDimension : context.MaxImageDimension2D;
                    if (pixels.Width <= 0 || pixels.Height <= 0 || pixels.Width > limit || pixels.Height > limit)
                        throw new InvalidOperationException($"Image '{image.Name}' exceeds the supported dimensions ({limit}).");
                    frames.Add(image.Id, new(bounds,
                        new Rect(pixels.X / density - gridOffset.X, pixels.Y / density - gridOffset.Y,
                            pixels.Width / density, pixels.Height / density), pixels.Size, density));
                    sizes.Add(image.Id, checked((long)pixels.Width * pixels.Height * BytesPerPixel(image.Format)));
                }
                else if (resource is ShaderBuffer buffer)
                    sizes.Add(buffer.Id, buffer.ByteLength);
            }

            // Validate and compile every pass before allocating its working set, so malformed later stages
            // do not run a prefix of the graph or reach Vulkan with an invalid resource interface.
            foreach (ShaderPassDescription pass in graph.Passes)
            {
                ProgramCacheLease<ShaderGraphPipeline> lease = acquireProgram(context, pass);
                leases.Add(lease);
                lease.Program.Validate(pass);
                if (pass.Attachments.Length > 0)
                {
                    ShaderImageFrame frame = frames[pass.Attachments[0].Image.Id];
                    if (pass.Attachments.Any(a => frames[a.Image.Id] != frame))
                        throw new InvalidOperationException($"Pass '{pass.Name}' has mismatched attachment bounds or resolutions.");
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

            // Renderer-owned inputs need not have storage usage. Keep them read-only and make a
            // graph-owned copy only when a storage read requires it, within the same allocation budget.
            if ((usages[0] & ImageUsageFlags.StorageBit) != 0
                && (context.RequireOwned<VulkanTexture2D>(source, nameof(source)).Usage & ImageUsageFlags.StorageBit) == 0)
            {
                usages[0] |= VulkanTexture2D.ColorTextureUsage;
                sizes[0] = checked((long)source.Width * source.Height * BytesPerPixel(source.Format));
                images.Remove(0);
                Allocate(graph.Resources[0]);
                context.CopyTexture(source, images[0]);
            }

            for (int index = 0; index < graph.Passes.Length; index++)
            {
                ShaderPassDescription pass = graph.Passes[index];
                foreach (ShaderPassBinding binding in pass.Bindings)
                {
                    if (!binding.Metadata)
                        Allocate(binding.Resource);
                }
                foreach (ShaderColorAttachment attachment in pass.Attachments)
                    Allocate(attachment.Image);
                foreach (ShaderBuffer buffer in pass.Draw.ReadBuffers)
                    Allocate(buffer);
                leases[index].Program.Execute(pass, images, buffers, frames);

                foreach (ShaderResource resource in graph.Resources)
                {
                    if (graph.LastUses[resource.Id] == index)
                        Release(resource.Id);
                }
            }

            // Normalize the selected image into the renderer's linear-premultiplied RGBA target. All
            // intermediate formats remain untouched, including scalar data attachments.
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
                foreach (int id in ownedImages)
                    images[id].Dispose();
                foreach (ITexture2D image in availableImages)
                    image.Dispose();
                foreach (IBuffer buffer in buffers.Values)
                    buffer.Dispose();
                foreach (ProgramCacheLease<ShaderGraphPipeline> lease in leases)
                    lease.Dispose();
            }
        }

        void Allocate(ShaderResource resource)
        {
            if (images.ContainsKey(resource.Id) || buffers.ContainsKey(resource.Id))
                return;
            long bytes = sizes[resource.Id];
            if (resource is ShaderImage reusable)
            {
                ShaderImageFrame footprint = frames[resource.Id];
                int reuseIndex = availableImages.FindIndex(image => image.Format == reusable.Format
                    && image.Width == footprint.PixelSize.Width && image.Height == footprint.PixelSize.Height
                    && (((VulkanTexture2D)image).Usage & usages[resource.Id]) == usages[resource.Id]);
                if (reuseIndex >= 0)
                {
                    images.Add(resource.Id, availableImages[reuseIndex]);
                    ownedImages.Add(resource.Id);
                    availableImages.RemoveAt(reuseIndex);
                    return;
                }
            }
            if (bytes > MaximumTransientBytes - liveBytes)
                throw new InvalidOperationException($"Shader graph resource '{resource.Name}' exceeds the {MaximumTransientBytes}-byte active allocation budget.");
            if (resource is ShaderImage image)
            {
                ShaderImageFrame frame = frames[image.Id];
                images.Add(image.Id, context.CreateTexture2D(frame.PixelSize.Width, frame.PixelSize.Height, image.Format, usages[image.Id]));
                ownedImages.Add(image.Id);
            }
            else if (resource is ShaderBuffer buffer)
            {
                IBuffer allocated = context.CreateBuffer((ulong)bytes,
                    BufferUsage.StorageBuffer | BufferUsage.UniformBuffer | BufferUsage.VertexBuffer | BufferUsage.IndexBuffer,
                    MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
                buffers.Add(buffer.Id, allocated);
                if (!buffer.InitialData.IsEmpty)
                    allocated.Upload<byte>(buffer.InitialData.AsSpan());
            }
            liveBytes = checked(liveBytes + bytes);
        }

        void Release(int id)
        {
            if (ownedImages.Remove(id))
            {
                availableImages.Add(images[id]);
                images.Remove(id);
            }
            if (buffers.Remove(id, out IBuffer? buffer))
            {
                // Deferred destruction still consumes memory until submission completes. Do not subtract
                // these bytes from the budget or overwrite host-visible data used by an earlier pass.
                buffer.Dispose();
            }
        }
    }

    private static int BytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.RGBA32Float => 16,
        TextureFormat.RGBA16Float => 8,
        TextureFormat.RGBA8Unorm or TextureFormat.BGRA8Unorm or TextureFormat.R32Float => 4,
        TextureFormat.R16Float => 2,
        TextureFormat.R8Unorm => 1,
        _ => throw new NotSupportedException($"Unsupported graph image format {format}."),
    };
}

using System.Collections.Immutable;
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
    /// <summary>The byte size of <see cref="GetParameters"/>: three vec4 values.</summary>
    public const int ParameterSize = 3 * 4 * sizeof(float);

    /// <summary>Frames <paramref name="pixels"/>, rasterized at <paramref name="density"/> on a device grid shifted by
    /// <paramref name="gridOffset"/>, as the raster bounds of an image with logical <paramref name="bounds"/>.</summary>
    public static ShaderImageFrame FromPixels(Rect bounds, PixelRect pixels, float density, Vector gridOffset)
    {
        Rect raster = pixels.ToRect(density);
        return new(bounds, new Rect(raster.X - gridOffset.X, raster.Y - gridOffset.Y, raster.Width, raster.Height),
            pixels.Size, density);
    }

    public static Point ResolveOrigin(RelativePoint origin, Rect group) => group.Position + origin.ToPixels(group.Size);

    public Point GroupOrigin(RelativePoint origin) => ResolveOrigin(origin, ReferenceBounds ?? Bounds);

    public Vector4[] GetParameters() =>
    [
        new(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height),
        new(RasterBounds.X, RasterBounds.Y, RasterBounds.Width, RasterBounds.Height),
        new(PixelSize.Width, PixelSize.Height, Scale, 0),
    ];
}

// ImageAllocations and BufferAllocations count allocations from the driver. ResourceReuses counts what an execution
// took instead: its own returned resources, or images an earlier execution left in a ShaderGraphImagePool.
internal readonly record struct ShaderGraphExecutionStatistics(int ProgramCacheHits, int ExecutedPasses,
    long PeakResourceBytes, int ImageAllocations, int BufferAllocations, int ResourceReuses, long ScheduledPixels);

/// <summary>The frames an execution's passes bind as image metadata, in one buffer with an aligned slot per image.</summary>
internal sealed class ShaderGraphParameters : IDisposable
{
    private readonly Dictionary<int, ulong> _offsets;

    private ShaderGraphParameters(VulkanBuffer buffer, Dictionary<int, ulong> offsets)
        => (Buffer, _offsets) = (buffer, offsets);

    internal VulkanBuffer Buffer { get; }

    internal ulong OffsetOf(int image) => _offsets[image];

    // Every metadata binding of one image reads the same frame, so one slot serves all of them.
    internal static ShaderGraphParameters? Create(VulkanContext context, ShaderGraphDescription graph,
        IReadOnlyDictionary<int, ShaderImageFrame> frames)
    {
        ulong alignment = Math.Max(1, context.Limits.MinUniformBufferOffsetAlignment);
        ulong stride = (ShaderImageFrame.ParameterSize + alignment - 1) / alignment * alignment;
        var offsets = new Dictionary<int, ulong>();
        foreach (ShaderPassDescription pass in graph.Passes)
            foreach (ShaderPassBinding binding in pass.Bindings)
                if (binding.Metadata)
                    offsets.TryAdd(binding.Resource.Id, (ulong)offsets.Count * stride);
        if (offsets.Count == 0)
            return null;

        var data = new byte[checked((int)((ulong)offsets.Count * stride))];
        foreach ((int image, ulong offset) in offsets)
            MemoryMarshal.AsBytes(frames[image].GetParameters().AsSpan()).CopyTo(data.AsSpan((int)offset));
        var buffer = (VulkanBuffer)context.CreateBuffer((ulong)data.Length, BufferUsage.UniformBuffer,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
        try
        {
            buffer.Upload<byte>(data);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        return new(buffer, offsets);
    }

    public void Dispose() => Buffer.Dispose();
}

internal static class ShaderGraphExecutor
{
    // This is an active-allocation ceiling, not the target pool's retained-cache budget.
    internal const long MaximumTransientBytes = 256L * 1024 * 1024;

    internal static long EstimateImageBytes(ImageDescription description)
    {
        long bytes = 0;
        for (int mip = 0; mip < description.MipLevels; mip++)
        {
            (int width, int height, int depth) = description.MipExtent(mip);
            bytes = checked(bytes + (long)width * height * depth
                * description.ArrayLayers * description.Format.BytesPerTexel());
        }
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

    // Only the selected image and the mapping vary between boundary conversions, and neither is pipeline state.
    private static readonly ShaderImage s_result = new(s_copy, -1, "result", TextureFormat.RGBA16Float, null, null, 0, 1);
    private static readonly ShaderProgramIdentity s_copyIdentity = ShaderGraphDescription.GetPipelineIdentity(CopyPass(s_result, []));

    private static ShaderPassDescription CopyPass(ShaderImage selected, ImmutableArray<byte> mapping)
        => new("output", s_copy,
            [new(0, selected, DescriptorType.CombinedImageSampler, ResourceAccess.Read, SamplerState.LinearClamp, false)],
            [new(0, s_result, AttachmentLoadOp.Clear)], mapping, BlendState.Replace, (3, 1, 1), false);

    /// <param name="requiredOutput">The part of the output <paramref name="output"/> covers, or null for all of it.</param>
    /// <param name="retainedImages">Where the execution takes images from and returns them to, so later executions
    /// skip the allocation; without it the execution disposes its images.</param>
    /// <param name="externalImages">The pictures the graph declared besides the source, in that order.</param>
    public static ShaderGraphExecutionStatistics Execute(ShaderGraphDescription graph, ITexture2D source, ShaderImageFrame sourceFrame,
        ITexture2D output, float scale, Vector gridOffset, ShaderGraphProgramAcquirer acquireProgram, Rect? requiredOutput = null,
        ShaderGraphImagePool? retainedImages = null, IReadOnlyList<(ITexture2D Texture, ShaderImageFrame Frame)>? externalImages = null)
    {
        VulkanContext context = GLSLFilterPipeline.TryResolveVulkan(GraphicsContextFactory.SharedContext)
            ?? throw new NotSupportedException("Shader graphs require a Vulkan rendering context.");
        var images = new Dictionary<int, ITexture2D>();
        var buffers = new Dictionary<int, IBuffer>();
        var storage = new Dictionary<int, ShaderGraphImage>();
        var imagePool = new TransientPool<ShaderGraphImage>();
        var bufferPool = new TransientPool<VulkanBuffer>();
        var leases = new List<ProgramCacheLease<ShaderGraphPipeline>>();
        var programs = new ShaderGraphPipeline?[graph.Passes.Length];
        var regions = new ShaderGraphRegions(graph, sourceFrame, scale, gridOffset,
            requiredOutput ?? graph.TransformBounds(sourceFrame.Bounds, sourceFrame.ReferenceBounds),
            externalImages?.Select(static image => image.Frame).ToArray());
        Dictionary<int, ShaderImageFrame> frames = regions.Frames;
        var descriptions = new Dictionary<int, ImageDescription>();
        ShaderGraphParameters? parameters = null;
        long liveBytes = 0;
        long peakBytes = 0, scheduledPixels = 0;
        int imageAllocations = 0, bufferAllocations = 0, reuses = 0, executedPasses = 0;
        bool completed = false, synchronizedRetained = false;
        ImageUsageFlags[] usages = [.. graph.ImageUsages];
        try
        {
            foreach (ShaderImage image in graph.Resources.OfType<ShaderImage>())
            {
                if (image.Id != 0 && graph.LastUses[image.Id] < 0) continue;
                if (!image.IsView)
                {
                    ImageDescription description = image.DescriptionFor(frames[image.Id].PixelSize);
                    descriptions.Add(image.Id, description);
                    if (usages[image.Id] != 0)
                        ShaderGraphImage.Validate(context, description, usages[image.Id]);
                }
            }

            // Preflight the complete graph before executing its first command.
            for (int index = 0; index < graph.Passes.Length; index++)
            {
                ShaderPassDescription pass = graph.Passes[index];
                if (pass.Transfer is { } transfer)
                {
                    ShaderGraphTransfers.Validate(context, transfer, frames);
                    continue;
                }
                ProgramCacheLease<ShaderGraphPipeline> lease = acquireProgram(context, pass, graph.PassIdentities[index]!);
                leases.Add(lease);
                programs[index] = lease.Program;
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
            Rect outputBounds = requiredOutput ?? graph.TransformBounds(sourceFrame.Bounds, sourceFrame.ReferenceBounds);
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
            ShaderPassDescription copy = CopyPass(selected, [.. MemoryMarshal.AsBytes(mapping.AsSpan())]);
            ProgramCacheLease<ShaderGraphPipeline> copyLease = acquireProgram(context, copy, s_copyIdentity);
            leases.Add(copyLease);
            copyLease.Program.Validate(copy);
            parameters = ShaderGraphParameters.Create(context, graph, frames);

            BindInput((ShaderImage)graph.Resources[0], source);
            foreach (ShaderImage external in graph.Resources.OfType<ShaderImage>())
            {
                if (external.ExternalIndex >= 0 && descriptions.ContainsKey(external.Id))
                    BindInput(external, externalImages![external.ExternalIndex].Texture);
            }

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
                    programs[index]!.Execute(pass, images, buffers, frames, parameters, scissor);
                    executedPasses++;
                }
                foreach (ShaderResource resource in graph.ReleasesAfter[index])
                    Release(resource);
            }

            // Normalize the selected image into the renderer's linear-premultiplied RGBA target. All
            // intermediate formats remain untouched, including scalar data attachments.
            Allocate(selected);
            images.Add(-1, output);
            copyLease.Program.Execute(copy, images, buffers, frames, parameters);
            completed = true;
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
            catch
            {
                // The discarded command buffer held transitions the images already recorded as done.
                completed = false;
                throw;
            }
            finally
            {
                // A failed execution may have left an image mid-way, so only a completed one is kept.
                imagePool.Release(image =>
                {
                    if (completed && retainedImages is not null && !image.IsBorrowed)
                        retainedImages.Return(image);
                    else
                        image.Dispose();
                });
                bufferPool.Release(static buffer => buffer.Dispose());
                parameters?.Dispose();
                foreach (ProgramCacheLease<ShaderGraphPipeline> lease in leases)
                    lease.Dispose();
            }
        }

        void BindInput(ShaderImage image, ITexture2D texture)
        {
            VulkanTexture2D owned = context.RequireOwned<VulkanTexture2D>(texture, nameof(source));
            if ((usages[image.Id] & ~owned.Usage) != 0)
            {
                usages[image.Id] |= ImageUsageFlags.TransferDstBit;
                Allocate(image);
                context.CopyTexture(texture, images[image.Id]);
            }
            else
            {
                var borrowed = new ShaderGraphImage(context, descriptions[image.Id], owned.Usage, owned);
                storage.Add(image.Id, borrowed);
                imagePool.Add(borrowed);
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
                    owner = imagePool.Take(i => i.Supports(description, usages[root.Id]));
                    if (owner is not null)
                        reuses++;
                    else if ((owner = retainedImages?.Take(context, description, usages[root.Id])) is not null)
                    {
                        // The image's layouts can match what this execution needs, so no transition would order
                        // it after the work that used it last; one barrier orders every retained image at once.
                        if (!synchronizedRetained)
                        {
                            ShaderGraphPipeline.SynchronizeResources(context);
                            synchronizedRetained = true;
                        }
                        imagePool.Add(owner);
                        Admit(owner.AllocationSize, resource.Name);
                        reuses++;
                    }
                    else
                    {
                        CheckBudget(EstimateImageBytes(description), resource.Name);
                        owner = new ShaderGraphImage(context, description, usages[root.Id]);
                        imagePool.Add(owner);
                        Admit(owner.AllocationSize, resource.Name);
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
                VulkanBuffer? allocated = buffer.InitialData.IsEmpty
                    ? bufferPool.Take(b => b.Size == (ulong)buffer.ByteLength) : null;
                if (allocated is not null)
                    reuses++;
                else
                {
                    CheckBudget(buffer.ByteLength, buffer.Name);
                    allocated = (VulkanBuffer)context.CreateBuffer((ulong)buffer.ByteLength,
                        BufferUsage.StorageBuffer | BufferUsage.UniformBuffer | BufferUsage.VertexBuffer | BufferUsage.IndexBuffer
                            | BufferUsage.IndirectBuffer | BufferUsage.TransferSource | BufferUsage.TransferDestination,
                        MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
                    bufferPool.Add(allocated);
                    Admit(allocated.AllocationSize, buffer.Name);
                    bufferAllocations++;
                }
                buffers.Add(buffer.Id, allocated);
                if (!buffer.InitialData.IsEmpty) allocated.Upload<byte>(buffer.InitialData.AsSpan());
            }
        }

        // Counts a new allocation's actual size, which can exceed its estimate, against the active budget.
        void Admit(ulong allocationSize, string name)
        {
            CheckBudget(checked((long)allocationSize), name);
            liveBytes = checked(liveBytes + (long)allocationSize);
            peakBytes = Math.Max(peakBytes, liveBytes);
        }

        void CheckBudget(long bytes, string name)
        {
            if (bytes > MaximumTransientBytes - liveBytes && (imagePool.HasAvailable || bufferPool.HasAvailable))
            {
                // Reclaim retired, incompatible allocations only under pressure. Submission completion
                // is required before their deferred native releases actually reduce memory usage.
                liveBytes -= imagePool.ReclaimAvailable(static image => image.AllocationSize);
                liveBytes -= bufferPool.ReclaimAvailable(static buffer => buffer.AllocationSize);
                context.FlushCommands(waitForCompletion: true);
            }
            if (bytes > MaximumTransientBytes - liveBytes)
                throw new InvalidOperationException($"Shader graph resource '{name}' exceeds the {MaximumTransientBytes}-byte active allocation budget.");
        }

        void Release(ShaderResource resource)
        {
            images.Remove(resource.Id);
            if (resource is ShaderImage { IsView: false, IsInput: false } image
                && storage.Remove(image.Id, out ShaderGraphImage? owner)) imagePool.Return(owner);
            if (buffers.Remove(resource.Id, out IBuffer? buffer))
                bufferPool.Return((VulkanBuffer)buffer);
        }
    }

    /// <summary>The allocations one execution owns. A returned allocation waits for a compatible request
    /// until budget pressure reclaims it, or until <see cref="Release"/> hands every allocation on.</summary>
    private sealed class TransientPool<T> where T : class, IDisposable
    {
        private readonly HashSet<T> _owned = [];
        private readonly List<T> _available = [];

        public bool HasAvailable => _available.Count != 0;

        public void Add(T item) => _owned.Add(item);

        public void Return(T item) => _available.Add(item);

        public T? Take(Predicate<T> match)
        {
            int index = _available.FindIndex(match);
            if (index < 0)
                return null;
            T item = _available[index];
            _available.RemoveAt(index);
            return item;
        }

        /// <summary>Disposes every returned allocation and gives the bytes they held.</summary>
        public long ReclaimAvailable(Func<T, ulong> allocationSize)
        {
            long bytes = 0;
            foreach (T item in _available)
            {
                item.Dispose();
                _owned.Remove(item);
                bytes += checked((long)allocationSize(item));
            }
            _available.Clear();
            return bytes;
        }

        public void Release(Action<T> release)
        {
            foreach (T item in _owned)
                release(item);
        }
    }
}

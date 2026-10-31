using System.Collections.Immutable;
using System.Text;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using ImageUsageFlags = Silk.NET.Vulkan.ImageUsageFlags;

namespace Beutl.Graphics.Shaders;

/// <summary>An immutable, validated sequence of shader passes and their virtual resource dependencies.</summary>
public sealed class ShaderGraphDescription
{
    internal ShaderGraphDescription(ImmutableArray<ShaderResource> resources,
        ImmutableArray<ShaderPassDescription> passes, ShaderGraphOutput output)
    {
        Resources = resources;
        Passes = passes;
        Output = output;
        LastUses = new int[resources.Length];
        Array.Fill(LastUses, -1);
        _firstUses = new int[resources.Length];
        Array.Fill(_firstUses, int.MaxValue);
        ImageUsages = new ImageUsageFlags[resources.Length];
        var initialized = resources.OfType<ShaderBuffer>().Where(b => !b.InitialData.IsEmpty).Select(b => b.Id).ToHashSet();
        var images = new HashSet<(int Image, int Mip, int Layer, int Aspect)> { (0, 0, 0, 0) };
        for (int index = 0; index < passes.Length; index++)
        {
            ShaderPassDescription pass = passes[index];
            if (pass.Transfer is { } transfer)
            {
                if (transfer.Source is { } source)
                {
                    RequireInitialized(source, pass.Name, firstMipOnly: transfer.Kind == ShaderTransferKind.GenerateMipmaps);
                    Touch(source, index, ImageUsageFlags.TransferSrcBit);
                }
                Initialize(transfer.Destination);
                Touch(transfer.Destination, index, ImageUsageFlags.TransferDstBit);
                continue;
            }
            foreach (ShaderRuntimeConstant constant in pass.RuntimeConstants)
                if (constant.Image is { } constantImage)
                    Touch(constantImage, index);
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                if (!binding.Metadata && (binding.Access & ResourceAccess.Read) != 0)
                    RequireInitialized(binding.Resource, pass.Name, aspect: binding.Resource is ShaderImage img && img.Format.IsDepthFormat() ? 1 : null);
                Touch(binding.Resource, index, binding.Metadata ? 0 : binding.Type == DescriptorType.StorageImage
                    ? ImageUsageFlags.StorageBit : ImageUsageFlags.SampledBit);
            }
            foreach (ShaderColorAttachment attachment in pass.Attachments)
            {
                if (attachment.Load == AttachmentLoadOp.Load)
                    RequireInitialized(attachment.Image, pass.Name);
                if (attachment.Load == AttachmentLoadOp.DontCare && pass.Blend.Enabled)
                    throw new InvalidOperationException($"Pass '{pass.Name}' cannot blend into undefined attachment contents.");
                Touch(attachment.Image, index, ImageUsageFlags.ColorAttachmentBit);
            }
            if (pass.DepthAttachment is { } depth)
            {
                if (depth.Load == AttachmentLoadOp.Load)
                {
                    if (pass.DepthStencil.ReadsDepth) RequireInitialized(depth.Image, pass.Name, aspect: 1);
                    if (pass.DepthStencil.ReadsStencil) RequireInitialized(depth.Image, pass.Name, aspect: 2);
                }
                Touch(depth.Image, index, ImageUsageFlags.DepthStencilAttachmentBit);
            }
            foreach (ShaderBuffer buffer in pass.Draw.ReadBuffers)
            {
                RequireInitialized(buffer, pass.Name);
                Touch(buffer, index);
            }
            foreach (ShaderPassBinding binding in pass.Bindings)
                if ((binding.Access & ResourceAccess.Write) != 0)
                    Initialize(binding.Resource);
            foreach (ShaderColorAttachment attachment in pass.Attachments)
                Initialize(attachment.Image);
            if (pass.DepthAttachment is { } writtenDepth)
            {
                if (writtenDepth.Load == AttachmentLoadOp.DontCare)
                    images.ExceptWith(writtenDepth.Image.Subresources);
                if (writtenDepth.Load == AttachmentLoadOp.Clear)
                    Initialize(writtenDepth.Image);
                else
                {
                    if (pass.DepthStencil.WritesDepth) Initialize(writtenDepth.Image, 1);
                    if (pass.DepthStencil.WritesStencil) Initialize(writtenDepth.Image, 2);
                }
            }
        }
        RequireInitialized(output.RenderingImage, "output");
        Touch(output.RenderingImage, passes.Length, ImageUsageFlags.SampledBit);
        PassIdentities = [.. passes.Select(pass => pass.Transfer is null ? GetPipelineIdentity(pass) : null)];
        // The output's images stay live through the boundary conversion; unused resources never allocate.
        List<ShaderResource>[] releases = [.. passes.Select(_ => new List<ShaderResource>())];
        foreach (ShaderResource resource in resources)
            if (LastUses[resource.Id] >= 0 && LastUses[resource.Id] < passes.Length)
                releases[LastUses[resource.Id]].Add(resource);
        ReleasesAfter = [.. releases.Select(bucket => bucket.ToImmutableArray())];
        UsesGroupOrigin = passes.Any(pass => pass.RuntimeConstants.Any(c => c.Kind == ShaderRuntimeConstantKind.BoundsOrigin));
        StructuralIdentity = CreateIdentity(includeValues: false);
        OutputIdentity = new ShaderGraphValueIdentity(CreateIdentity(includeValues: true),
            resources.OfType<ShaderBuffer>().Select(b => b.InitialData).ToImmutableArray());

        void Touch(ShaderResource resource, int index, ImageUsageFlags usage = 0)
        {
            LastUses[resource.Id] = index;
            if (resource is ShaderImage image)
            {
                LastUses[image.Root.Id] = index;
                ImageUsages[image.Root.Id] |= usage;
                // Metadata needs a coordinate frame, but does not allocate image storage.
                if (usage == 0) return;
                _firstUses[image.Root.Id] = Math.Min(_firstUses[image.Root.Id], index);
            }
            _firstUses[resource.Id] = Math.Min(_firstUses[resource.Id], index);
        }

        void Initialize(ShaderResource resource, int? aspect = null)
        {
            if (resource is ShaderImage image)
                images.UnionWith(image.Subresources.Where(s => aspect is null || s.Aspect == aspect));
            else
                initialized.Add(resource.Id);
        }

        void RequireInitialized(ShaderResource resource, string pass, bool firstMipOnly = false, int? aspect = null)
        {
            bool ready = resource is ShaderImage image
                ? image.Subresources.Where(s => (!firstMipOnly || s.Mip == image.BaseMipLevel) && (aspect is null || s.Aspect == aspect)).All(images.Contains)
                : initialized.Contains(resource.Id);
            if (!ready)
                throw new InvalidOperationException($"Pass '{pass}' reads uninitialized resource '{resource.Name}'.");
        }
    }

    public ImmutableArray<ShaderResource> Resources { get; }
    public ShaderGraphOutput Output { get; }
    public ImmutableArray<string> PassNames => Passes.Select(p => p.Name).ToImmutableArray();
    internal ImmutableArray<ShaderPassDescription> Passes { get; }
    internal int[] LastUses { get; }
    internal ImageUsageFlags[] ImageUsages { get; }
    private readonly int[] _firstUses;

    /// <summary>Each pass's program-cache identity, or null for a transfer.</summary>
    internal ImmutableArray<ShaderProgramIdentity?> PassIdentities { get; }

    /// <summary>The resources each pass uses last, in resource order, whose storage can be reused after it runs.</summary>
    internal ImmutableArray<ImmutableArray<ShaderResource>> ReleasesAfter { get; }

    /// <summary>Whether a pass reads an origin in the complete input group's bounds.</summary>
    internal bool UsesGroupOrigin { get; }

    internal string StructuralIdentity { get; }
    internal ShaderGraphValueIdentity OutputIdentity { get; }

    internal Rect TransformBounds(Rect sourceBounds, Rect? referenceBounds = null)
    {
        if (sourceBounds.IsInvalid)
            return Rect.Invalid;
        Rect bounds = Output.Image.ResolveBounds(sourceBounds, referenceBounds);
        RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(sourceBounds));
        return bounds;
    }

    internal float ClampWorkingScale(Rect sourceBounds, float scale, Vector gridOffset, Rect? referenceBounds = null,
        PixelSize? sourceSize = null, ImageUsageFlags sourceUsage = VulkanTexture2D.ColorTextureUsage)
    {
        // The executor copies an incompatible source before the first pass and keeps it until
        // execution ends. Its native extent does not shrink with the graph's working density.
        PixelSize inputSize = sourceSize ?? PixelRect.FromRect(sourceBounds.Translate(gridOffset), scale).Size;
        long sourceBytes = (ImageUsages[0] & ~sourceUsage) != 0
            ? ShaderGraphExecutor.EstimateImageBytes(((ShaderImage)Resources[0]).DescriptionFor(inputSize)) : 0;
        foreach (ShaderImage image in Resources.OfType<ShaderImage>())
        {
            if (_firstUses[image.Id] == int.MaxValue || image.Description.HasValue)
                continue;
            Rect bounds = image.ResolveBounds(sourceBounds, referenceBounds).Translate(gridOffset);
            scale = Math.Min(scale, BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(
                bounds, scale * image.ResolutionScale) / image.ResolutionScale);
        }
        // An allocation sizes its storage from its raster root's frame. Those bounds do not depend on
        // density, so they are resolved once for every estimate below.
        var rasters = new (ShaderImage Image, Rect Bounds)[Resources.Length];
        foreach (ShaderImage image in Resources.OfType<ShaderImage>())
        {
            if (image.Id == 0 || _firstUses[image.Id] == int.MaxValue || image.IsView) continue;
            ShaderImage raster = image.RasterRoot;
            rasters[image.Id] = (raster, raster.ResolveBounds(sourceBounds, referenceBounds).Translate(gridOffset));
        }
        // Retired allocations can be reclaimed by the executor; only overlapping lifetimes
        // constrain density. Leave room for device-specific allocation alignment.
        const long budget = ShaderGraphExecutor.MaximumTransientBytes * 15 / 16;
        if (PeakBytes(scale) <= budget || PeakBytes(0) > budget)
            return scale;
        float lower = 0, upper = scale;
        for (int i = 0; i < 24; i++)
        {
            float candidate = (lower + upper) / 2;
            if (PeakBytes(candidate) <= budget) lower = candidate;
            else upper = candidate;
        }
        return lower > 0 ? lower : scale;

        long PeakBytes(float density)
        {
            var changes = new long[Passes.Length + 2];
            foreach (ShaderResource resource in Resources)
            {
                if (resource.Id == 0 || _firstUses[resource.Id] == int.MaxValue || resource is ShaderImage { IsView: true }) continue;
                long bytes;
                if (resource is ShaderImage image)
                {
                    (ShaderImage raster, Rect bounds) = rasters[image.Id];
                    PixelSize size = raster.Id == 0 ? inputSize
                        : PixelRect.FromRect(bounds, density * raster.ResolutionScale).Size;
                    bytes = ShaderGraphExecutor.EstimateImageBytes(
                        image.DescriptionFor(new(Math.Max(1, size.Width), Math.Max(1, size.Height))));
                }
                else bytes = ((ShaderBuffer)resource).ByteLength;
                changes[_firstUses[resource.Id]] = checked(changes[_firstUses[resource.Id]] + bytes);
                changes[LastUses[resource.Id] + 1] = checked(changes[LastUses[resource.Id] + 1] - bytes);
            }
            long peak = sourceBytes, live = sourceBytes;
            foreach (long change in changes)
            {
                live = checked(live + change);
                peak = Math.Max(peak, live);
            }
            return peak;
        }
    }

    private string CreateIdentity(bool includeValues)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Resources.Length);
        foreach (ShaderResource resource in Resources)
        {
            writer.Write(resource is ShaderImage);
            if (resource is ShaderImage image)
            {
                writer.Write((int)image.Format);
                writer.Write((int)image.Kind);
                writer.Write(image.Root.Id);
                writer.Write(image.BaseMipLevel);
                writer.Write(image.BaseArrayLayer);
                writer.Write(image.MipLevels);
                writer.Write(image.ArrayLayers);
                writer.Write(image.Depth);
                writer.Write(image.Description.HasValue);
                writer.Write(image.Like?.Id ?? -1);
                writer.Write(image.RasterSource?.Id ?? -1);
                writer.Write(image.ExplicitBounds.HasValue);
                writer.Write(image.BoundsTransforms.Length);
                if (includeValues)
                {
                    writer.Write((int)image.BoundsOrigin.Unit);
                    writer.Write(image.BoundsOrigin.Point.X); writer.Write(image.BoundsOrigin.Point.Y);
                    foreach (Matrix transform in image.BoundsTransforms)
                    {
                        writer.Write(transform.M11); writer.Write(transform.M12);
                        writer.Write(transform.M21); writer.Write(transform.M22);
                        writer.Write(transform.M31); writer.Write(transform.M32);
                    }
                }
                if (includeValues)
                {
                    Rect bounds = image.ExplicitBounds ?? default;
                    writer.Write(bounds.X);
                    writer.Write(bounds.Y);
                    writer.Write(bounds.Width);
                    writer.Write(bounds.Height);
                    writer.Write(image.Padding);
                    writer.Write(image.ResolutionScale);
                }
            }
            else if (resource is ShaderBuffer buffer)
            {
                writer.Write(buffer.Stride);
                if (includeValues)
                {
                    writer.Write(buffer.Count);
                    writer.Write(buffer.InitialData.Length);
                }
            }
        }
        writer.Write(Passes.Length);
        // Each distinct pipeline is written once; a pass names its entry by index.
        var programs = new Dictionary<ShaderProgramIdentity, int>();
        var programSources = new List<string>();
        foreach (ShaderProgramIdentity? identity in PassIdentities)
        {
            if (identity is not null && programs.TryAdd(identity, programs.Count))
                programSources.Add(identity.Source);
        }
        writer.Write(programSources.Count);
        foreach (string source in programSources)
            writer.Write(source);
        for (int index = 0; index < Passes.Length; index++)
        {
            ShaderPassDescription pass = Passes[index];
            writer.Write(pass.RuntimeConstants.Length);
            foreach (ShaderRuntimeConstant constant in pass.RuntimeConstants)
            {
                writer.Write((int)constant.Kind);
                writer.Write(constant.Offset);
                writer.Write(constant.Image?.Id ?? -1);
                if (includeValues && constant.Kind == ShaderRuntimeConstantKind.BoundsOrigin)
                {
                    writer.Write((int)constant.Origin.Unit);
                    writer.Write(constant.Origin.Point.X); writer.Write(constant.Origin.Point.Y);
                }
            }
            writer.Write(pass.PartialRendering);
            writer.Write(pass.InputRegions.Count);
            foreach ((int binding, ShaderInputRegion region) in pass.InputRegions.OrderBy(p => p.Key))
            {
                writer.Write(binding);
                if (includeValues)
                {
                    writer.Write(region.Transform.M11); writer.Write(region.Transform.M12);
                    writer.Write(region.Transform.M21); writer.Write(region.Transform.M22);
                    writer.Write(region.Transform.M31); writer.Write(region.Transform.M32);
                    writer.Write(region.Padding);
                }
            }
            writer.Write(pass.Transfer is not null);
            if (pass.Transfer is { } transfer)
            {
                writer.Write((int)transfer.Kind);
                writer.Write(transfer.Source?.Id ?? -1);
                writer.Write(transfer.Destination.Id);
                writer.Write((int)transfer.Filter);
                if (includeValues)
                {
                    writer.Write(transfer.Color.X); writer.Write(transfer.Color.Y);
                    writer.Write(transfer.Color.Z); writer.Write(transfer.Color.W);
                    writer.Write(transfer.Depth); writer.Write(transfer.Value);
                }
                continue;
            }
            writer.Write(programs[PassIdentities[index]!]);
            writer.Write(pass.DepthAttachment?.Image.Id ?? -1);
            if (includeValues && pass.DepthAttachment is { } depth)
            {
                writer.Write(depth.ClearDepth);
                writer.Write(depth.ClearStencil);
            }
            writer.Write(pass.Draw.Indirect?.Buffer.Id ?? -1);
            if (includeValues && pass.Draw.Indirect is { } indirect)
            {
                writer.Write(indirect.Offset); writer.Write(indirect.Count);
                writer.Write(indirect.Stride); writer.Write(indirect.CommandSize);
            }
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                writer.Write(binding.Resource.Id);
                writer.Write((int)binding.Access);
                writer.Write(binding.Metadata);
            }
            foreach (ShaderColorAttachment attachment in pass.Attachments)
                writer.Write(attachment.Image.Id);
            writer.Write(pass.Draw.Vertices.Length);
            foreach (ShaderVertexBuffer vertex in pass.Draw.Vertices)
            {
                writer.Write(vertex.Binding);
                writer.Write(vertex.Buffer.Id);
                if (includeValues)
                    writer.Write(vertex.Offset);
            }
            writer.Write(pass.Draw.Indices is not null);
            if (pass.Draw.Indices is { } indices)
            {
                writer.Write(indices.Buffer.Id);
                writer.Write((int)indices.Format);
                if (includeValues)
                    writer.Write(indices.Offset);
            }
            if (includeValues)
            {
                writer.Write(pass.Constants.Length);
                writer.Write(pass.Constants.AsSpan());
                writer.Write(pass.Work.X);
                writer.Write(pass.Work.Y);
                writer.Write(pass.Work.Z);
                writer.Write(pass.DispatchThreads);
                writer.Write(pass.Draw.FirstElement);
                writer.Write(pass.Draw.VertexOffset);
                writer.Write(pass.Draw.FirstInstance);
            }
        }
        writer.Write(Output.Image.Id);
        writer.Write(Output.RenderingImage.Id);
        if (includeValues) writer.Write((int)Output.Color);
        writer.Flush();
        return Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    internal static ShaderProgramIdentity GetPipelineIdentity(ShaderPassDescription pass)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("ShaderGraphPipeline/v4");
        WritePipelineIdentity(writer, pass);
        writer.Flush();
        return ShaderProgramIdentity.CreateSpirv(Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length)));
    }

    private static void WritePipelineIdentity(BinaryWriter writer, ShaderPassDescription pass)
    {
        writer.Write(pass.DepthAttachment is not null);
        if (pass.DepthAttachment is { } depth)
        {
            writer.Write((int)depth.Image.Format);
            writer.Write((int)depth.Load);
        }
        writer.Write(pass.DepthStencil.TestEnabled);
        writer.Write(pass.DepthStencil.WriteEnabled);
        writer.Write((int)pass.DepthStencil.Compare);
        writer.Write(pass.DepthStencil.StencilEnabled);
        WriteStencil(pass.DepthStencil.Front);
        WriteStencil(pass.DepthStencil.Back);
        writer.Write((int)pass.CullMode);
        writer.Write((int)pass.FrontFace);
        writer.Write(pass.PatchControlPoints);
        writer.Write((int)pass.Draw.Topology);
        writer.Write(pass.Draw.Bindings.Length);
        foreach (VertexBindingDescription binding in pass.Draw.Bindings)
        {
            writer.Write(binding.Binding);
            writer.Write(binding.Stride);
            writer.Write((int)binding.InputRate);
        }
        writer.Write(pass.Draw.Attributes.Length);
        foreach (VertexAttributeDescription attribute in pass.Draw.Attributes)
        {
            writer.Write(attribute.Location);
            writer.Write(attribute.Binding);
            writer.Write((int)attribute.Format);
            writer.Write(attribute.Offset);
        }
        writer.Write(pass.Program!.Stages.Count);
        Span<byte> value = stackalloc byte[sizeof(ulong)];
        foreach ((ShaderStage stage, ShaderSource source) in pass.Program.Stages.OrderBy(s => s.Key))
        {
            writer.Write((int)stage);
            writer.Write((int)source.Language);
            writer.Write(source.Text);
            SpecializationConstant[] constants = pass.Program.SpecializationConstants
                .Where(c => (c.Stages & stage) != 0).OrderBy(c => c.ConstantId).ToArray();
            writer.Write(constants.Length);
            foreach (SpecializationConstant constant in constants)
            {
                writer.Write(constant.ConstantId);
                writer.Write(constant.SizeInBytes);
                constant.CopyValueTo(value);
                writer.Write(value[..constant.SizeInBytes]);
            }
        }
        writer.Write(pass.Bindings.Length);
        foreach (ShaderPassBinding binding in pass.Bindings)
        {
            writer.Write(binding.Binding);
            writer.Write((int)binding.Type);
            writer.Write((int)binding.Sampler.MinFilter);
            writer.Write((int)binding.Sampler.MagFilter);
            writer.Write((int)binding.Sampler.AddressU);
            writer.Write((int)binding.Sampler.AddressV);
            writer.Write((int)binding.Sampler.AddressW);
            writer.Write((int)binding.Sampler.MipmapFilter);
            writer.Write(binding.Sampler.MinLod);
            writer.Write(binding.Sampler.MaxLod);
            writer.Write((int?)binding.Sampler.Compare ?? -1);
        }
        writer.Write(pass.Attachments.Length);
        foreach (ShaderColorAttachment attachment in pass.Attachments)
        {
            writer.Write((int)attachment.Image.Format);
            writer.Write((int)attachment.Load);
        }
        writer.Write(pass.Blend.Enabled);
        writer.Write((int)pass.Blend.SourceColor);
        writer.Write((int)pass.Blend.DestinationColor);
        writer.Write((int)pass.Blend.ColorOperation);
        writer.Write((int)pass.Blend.SourceAlpha);
        writer.Write((int)pass.Blend.DestinationAlpha);
        writer.Write((int)pass.Blend.AlphaOperation);

        void WriteStencil(StencilFaceState face)
        {
            writer.Write((int)face.Compare); writer.Write((int)face.Fail);
            writer.Write((int)face.DepthFail); writer.Write((int)face.Pass);
            writer.Write(face.CompareMask); writer.Write(face.WriteMask); writer.Write(face.Reference);
        }
    }
}

// Keep the already-copied immutable buffers by reference instead of serializing large payloads into a
// second allocation and a UTF-16 cache key. Hashes only select buckets; equality compares every byte.
internal sealed class ShaderGraphValueIdentity : IEquatable<ShaderGraphValueIdentity>
{
    private readonly string _metadata;
    private readonly ImmutableArray<ImmutableArray<byte>> _buffers;
    private readonly int _hash;

    public ShaderGraphValueIdentity(string metadata, ImmutableArray<ImmutableArray<byte>> buffers)
    {
        _metadata = metadata;
        _buffers = buffers;
        var hash = new HashCode();
        hash.Add(metadata);
        foreach (ImmutableArray<byte> buffer in buffers)
            hash.AddBytes(buffer.AsSpan());
        _hash = hash.ToHashCode();
    }

    public bool Equals(ShaderGraphValueIdentity? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null || _hash != other._hash || _metadata != other._metadata || _buffers.Length != other._buffers.Length)
            return false;
        for (int i = 0; i < _buffers.Length; i++)
        {
            if (!_buffers[i].AsSpan().SequenceEqual(other._buffers[i].AsSpan()))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is ShaderGraphValueIdentity other && Equals(other);

    public override int GetHashCode() => _hash;
}

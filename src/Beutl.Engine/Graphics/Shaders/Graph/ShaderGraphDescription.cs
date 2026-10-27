using System.Collections.Immutable;
using System.Text;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

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
                    Touch(source, index);
                }
                Initialize(transfer.Destination);
                Touch(transfer.Destination, index);
                continue;
            }
            if (pass.ImageSizeConstants is { } sizeConstants)
                Touch(sizeConstants.Image, index);
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                if (!binding.Metadata && (binding.Access & ResourceAccess.Read) != 0)
                    RequireInitialized(binding.Resource, pass.Name, aspect: binding.Resource is ShaderImage img && img.Format.IsDepthFormat() ? 1 : null);
                Touch(binding.Resource, index);
            }
            foreach (ShaderColorAttachment attachment in pass.Attachments)
            {
                if (attachment.Load == AttachmentLoadOp.Load)
                    RequireInitialized(attachment.Image, pass.Name);
                if (attachment.Load == AttachmentLoadOp.DontCare && pass.Blend.Enabled)
                    throw new InvalidOperationException($"Pass '{pass.Name}' cannot blend into undefined attachment contents.");
                Touch(attachment.Image, index);
            }
            if (pass.DepthAttachment is { } depth)
            {
                if (depth.Load == AttachmentLoadOp.Load)
                {
                    if (pass.DepthStencil.ReadsDepth) RequireInitialized(depth.Image, pass.Name, aspect: 1);
                    if (pass.DepthStencil.ReadsStencil) RequireInitialized(depth.Image, pass.Name, aspect: 2);
                }
                Touch(depth.Image, index);
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
        Touch(output.RenderingImage, passes.Length);
        StructuralIdentity = CreateIdentity(includeValues: false);
        OutputIdentity = new ShaderGraphValueIdentity(CreateIdentity(includeValues: true),
            resources.OfType<ShaderBuffer>().Select(b => b.InitialData).ToImmutableArray());

        void Touch(ShaderResource resource, int index)
        {
            LastUses[resource.Id] = index;
            if (resource is ShaderImage image)
                LastUses[image.Root.Id] = index;
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

    internal float ClampWorkingScale(Rect sourceBounds, float scale, Vector gridOffset, Rect? referenceBounds = null)
    {
        foreach (ShaderImage image in Resources.OfType<ShaderImage>())
        {
            if (LastUses[image.Id] < 0 || image.Description.HasValue)
                continue;
            Rect bounds = image.ResolveBounds(sourceBounds, referenceBounds).Translate(gridOffset);
            scale = Math.Min(scale, BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(
                bounds, scale * image.ResolutionScale) / image.ResolutionScale);
        }
        return scale;
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
        foreach (ShaderPassDescription pass in Passes)
        {
            writer.Write(pass.ImageSizeConstants?.Image.Id ?? -1);
            writer.Write(pass.ImageSizeConstants?.Offset ?? -1);
            writer.Write(pass.BoundsOriginConstants?.Offset ?? -1);
            if (includeValues && pass.BoundsOriginConstants is { } origin)
            {
                writer.Write((int)origin.Origin.Unit);
                writer.Write(origin.Origin.Point.X); writer.Write(origin.Origin.Point.Y);
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
            WritePipelineIdentity(writer, pass);
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

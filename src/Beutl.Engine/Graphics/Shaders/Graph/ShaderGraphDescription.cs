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
        var initialized = new HashSet<int> { 0 };
        foreach (ShaderBuffer buffer in resources.OfType<ShaderBuffer>())
        {
            if (!buffer.InitialData.IsEmpty)
                initialized.Add(buffer.Id);
        }

        for (int index = 0; index < passes.Length; index++)
        {
            ShaderPassDescription pass = passes[index];
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                if (!binding.Metadata && (binding.Access & ResourceAccess.Read) != 0)
                    RequireInitialized(binding.Resource, pass.Name);
                LastUses[binding.Resource.Id] = index;
            }
            foreach (ShaderColorAttachment attachment in pass.Attachments)
            {
                if (attachment.Load == AttachmentLoadOp.Load)
                    RequireInitialized(attachment.Image, pass.Name);
                if (attachment.Load == AttachmentLoadOp.DontCare && pass.Blend.Enabled)
                    throw new InvalidOperationException($"Pass '{pass.Name}' cannot blend into undefined attachment contents.");
                LastUses[attachment.Image.Id] = index;
            }
            foreach (ShaderBuffer buffer in pass.Draw.ReadBuffers)
            {
                RequireInitialized(buffer, pass.Name);
                LastUses[buffer.Id] = index;
            }
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                if ((binding.Access & ResourceAccess.Write) != 0)
                    initialized.Add(binding.Resource.Id);
            }
            foreach (ShaderColorAttachment attachment in pass.Attachments)
                initialized.Add(attachment.Image.Id);
        }
        RequireInitialized(output.Image, "output");
        LastUses[output.Image.Id] = passes.Length;
        StructuralIdentity = CreateIdentity(includeValues: false);
        OutputIdentity = new ShaderGraphValueIdentity(CreateIdentity(includeValues: true),
            resources.OfType<ShaderBuffer>().Select(b => b.InitialData).ToImmutableArray());

        void RequireInitialized(ShaderResource resource, string pass)
        {
            if (!initialized.Contains(resource.Id))
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

    internal Rect TransformBounds(Rect sourceBounds)
    {
        if (sourceBounds.IsInvalid)
            return Rect.Invalid;
        Rect bounds = Output.Image.ResolveBounds(sourceBounds);
        RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(sourceBounds));
        return bounds;
    }

    internal float ClampWorkingScale(Rect sourceBounds, float scale, Vector gridOffset)
    {
        foreach (ShaderImage image in Resources.OfType<ShaderImage>())
        {
            if (LastUses[image.Id] < 0)
                continue;
            Rect bounds = image.ResolveBounds(sourceBounds).Translate(gridOffset);
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
                writer.Write(image.Like?.Id ?? -1);
                writer.Write(image.ExplicitBounds.HasValue);
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
            WritePipelineIdentity(writer, pass);
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
        writer.Write((int)Output.Color);
        writer.Flush();
        return Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    internal static ShaderProgramIdentity GetPipelineIdentity(ShaderPassDescription pass)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("ShaderGraphPipeline/v2");
        WritePipelineIdentity(writer, pass);
        writer.Flush();
        return ShaderProgramIdentity.CreateSpirv(Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length)));
    }

    private static void WritePipelineIdentity(BinaryWriter writer, ShaderPassDescription pass)
    {
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
        writer.Write(pass.Program.Stages.Count);
        foreach ((ShaderStage stage, ShaderSource source) in pass.Program.Stages.OrderBy(s => s.Key))
        {
            writer.Write((int)stage);
            writer.Write((int)source.Language);
            writer.Write(source.Text);
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

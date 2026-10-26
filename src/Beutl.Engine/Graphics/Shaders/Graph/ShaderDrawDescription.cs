using System.Collections.Immutable;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics.Shaders;

internal sealed record ShaderVertexBuffer(int Binding, ShaderBuffer Buffer, long Offset);

internal sealed record ShaderIndexBuffer(ShaderBuffer Buffer, IndexFormat Format, long Offset)
{
    public int ElementSize => Format == IndexFormat.UInt16 ? 2 : 4;
}

internal sealed record ShaderDrawDescription(
    ImmutableArray<VertexBindingDescription> Bindings,
    ImmutableArray<VertexAttributeDescription> Attributes,
    ImmutableArray<ShaderVertexBuffer> Vertices,
    ShaderIndexBuffer? Indices,
    PrimitiveTopology Topology,
    int FirstElement, int VertexOffset, int FirstInstance)
{
    public static ShaderDrawDescription Empty { get; } = new([], [], [], null, PrimitiveTopology.TriangleList, 0, 0, 0);

    public IEnumerable<ShaderBuffer> ReadBuffers => Vertices.Select(v => v.Buffer)
        .Concat(Indices is { } indices ? [indices.Buffer] : []);

    public VertexInputDescription GetVertexInput() => new() { Bindings = [.. Bindings], Attributes = [.. Attributes] };

    public static int AttributeSize(VertexFormat format) => ((int)format % 4 + 1) * sizeof(float);

    public void Validate(string name, int count, int instanceCount)
    {
        if (Bindings.Select(b => b.Binding).Distinct().Count() != Bindings.Length
            || Attributes.Select(a => a.Location).Distinct().Count() != Attributes.Length)
            throw new ArgumentException($"Pass '{name}' has duplicate vertex bindings or attribute locations.");
        foreach (VertexBindingDescription binding in Bindings)
        {
            if (!Enum.IsDefined(binding.InputRate))
                throw new ArgumentException($"Pass '{name}' has an invalid vertex input rate.");
        }
        foreach (VertexAttributeDescription attribute in Attributes)
        {
            if (!Enum.IsDefined(attribute.Format))
                throw new ArgumentException($"Pass '{name}' has an invalid vertex attribute format.");
            if (!Bindings.Any(b => b.Binding == attribute.Binding))
                throw new ArgumentException($"Pass '{name}', location {attribute.Location}: missing vertex binding {attribute.Binding}.");
            ShaderVertexBuffer vertex = Vertices.FirstOrDefault(v => v.Binding == attribute.Binding)
                ?? throw new ArgumentException($"Pass '{name}', location {attribute.Location}: missing vertex buffer {attribute.Binding}.");
            VertexBindingDescription binding = Bindings.First(b => b.Binding == attribute.Binding);
            if (Indices is not null && binding.InputRate == VertexInputRate.Vertex)
                continue; // Index values may be produced on the device; initial bytes are not authoritative.
            long first = binding.InputRate == VertexInputRate.Instance ? FirstInstance : FirstElement;
            long elements = binding.InputRate == VertexInputRate.Instance ? instanceCount : count;
            long address = checked(vertex.Offset + first * binding.Stride + attribute.Offset);
            long end = checked(address + (elements - 1) * binding.Stride + AttributeSize(attribute.Format));
            if (address % 4 != 0 || elements > 1 && binding.Stride % 4 != 0)
                throw new ArgumentException($"Pass '{name}', location {attribute.Location}: vertex fetch addresses must be aligned to four bytes.");
            if (end > vertex.Buffer.ByteLength)
                throw new ArgumentException($"Pass '{name}', location {attribute.Location}: draw exceeds vertex buffer '{vertex.Buffer.Name}'.");
        }
        if (Indices is { } index
            && ((long)FirstElement + count) * index.ElementSize > index.Buffer.ByteLength - index.Offset)
            throw new ArgumentException($"Pass '{name}': draw exceeds index buffer '{index.Buffer.Name}'.");
    }
}

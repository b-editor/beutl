using System.Runtime.InteropServices;
using System.Text;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;

namespace Beutl.UnitTests.NodeGraph;

public class ShaderAuthoringInterfaceTests
{
    [Test]
    public void BlocksWithoutInstanceNamesKeepDistinctInputNames()
    {
        var metadata = ShaderAuthoringInterface.Compile(new ShaderSource("""
            #version 450
            layout(location=0) out vec4 color;
            layout(binding=0) uniform Parameters { float amount; };
            layout(binding=1) uniform OtherParameters { float otherAmount; };
            void main() { color=vec4(amount+otherAmount); }
            """), ShaderStage.Fragment);
        Assert.That(metadata.Descriptors.Select(d => d.Name), Is.EqualTo(new[] { "Parameters", "OtherParameters" }));
    }

    [Test]
    public void CompilationPreservesUnusedInputsAndMatrixLayoutWithoutAGpu()
    {
        var metadata = ShaderAuthoringInterface.Compile(new ShaderSource("""
            #version 450
            layout(location = 0) out vec4 color;
            layout(set = 0, binding = 1, std140) uniform Parameters {
                float unusedAmount;
                mat3 transform;
                vec4 palette[2];
            } parameters;
            layout(set = 0, binding = 2) uniform sampler2D textureInput;
            layout(push_constant) uniform Constants { float time; } constants;
            void main() { color = vec4(1); }
            """), ShaderStage.Fragment);
        var parameters = metadata.Descriptors.Single(d => d.Binding == 1);
        Assert.That(parameters.Name, Is.EqualTo("parameters"));
        Assert.That(parameters.Members.Select(m => m.Name),
            Is.EqualTo(new[] { "unusedAmount", "transform", "palette" }));
        Assert.That(parameters.Members[1].MatrixStride, Is.EqualTo(16));
        Assert.That(parameters.Members[1].Columns, Is.EqualTo(3));
        Assert.That(parameters.Members[2].ArrayLength, Is.EqualTo(2));
        Assert.That(parameters.Members[2].ArrayStride, Is.EqualTo(16));
        Assert.That(metadata.Descriptors.Single(d => d.Binding == 2).Name, Is.EqualTo("textureInput"));
        Assert.That(metadata.PushConstants.Single().Name, Is.EqualTo("time"));
    }

    [Test]
    public void OutputsConstantsAndStorageShapesAreRead()
    {
        var metadata = ShaderAuthoringInterface.Compile(new ShaderSource("""
            #version 450
            layout(location = 1) out uvec4 ids;
            layout(location = 0) out vec4 color;
            layout(constant_id = 7) const float gain = 0.5;
            layout(constant_id = 8) const bool enabled = true;
            layout(set = 0, binding = 0, rgba16f) uniform writeonly image2D target;
            layout(set = 0, binding = 1, std430) buffer Items { vec4 values[]; } items;
            layout(set = 0, binding = 2, std430) buffer Header { vec4 value; } header;
            void main() { color = vec4(gain); ids = uvec4(enabled ? 1 : 0); }
            """), ShaderStage.Fragment);

        Assert.That(metadata.ColorOutputs.Select(o => (o.Name, o.Location, o.ScalarKind)), Is.EquivalentTo(new[]
        {
            ("color", 0, ShaderScalarKind.FloatingPoint), ("ids", 1, ShaderScalarKind.UnsignedInteger)
        }));
        ShaderSpecializationLayout gain = metadata.SpecializationConstants.Single(c => c.ConstantId == 7);
        Assert.That(gain.Name, Is.EqualTo("gain"));
        Assert.That(gain.ScalarKind, Is.EqualTo(ShaderScalarKind.FloatingPoint));
        Assert.That(BitConverter.UInt32BitsToSingle((uint)gain.DefaultBits), Is.EqualTo(0.5f));
        Assert.That(metadata.SpecializationConstants.Single(c => c.ConstantId == 8).DefaultBits, Is.EqualTo(1));
        ShaderDescriptorLayout target = metadata.Descriptors.Single(d => d.Binding == 0);
        Assert.That(target.ImageFormat, Is.EqualTo(TextureFormat.RGBA16Float));
        Assert.That(target.ImageKind, Is.EqualTo(ImageKind.Image2D));
        Assert.That(target.Access, Is.EqualTo(ResourceAccess.Write));
        Assert.That(metadata.Descriptors.Single(d => d.Binding == 1).IsRuntimeSized, Is.True);
        Assert.That(metadata.Descriptors.Single(d => d.Binding == 1).ArrayStride, Is.EqualTo(16));
        Assert.That(metadata.Descriptors.Single(d => d.Binding == 2).IsRuntimeSized, Is.False);
        Assert.That(metadata.Descriptors.Single(d => d.Binding == 2).MinimumSize, Is.EqualTo(16));
    }

    [Test]
    public void BufferMembersUseDeclaredNamesAndOffsets()
    {
        var words = new List<uint> { 0x07230203, 0x00010000, 0, 20, 0 };
        void Op(uint opcode, params uint[] operands)
        {
            words.Add(((uint)operands.Length + 1) << 16 | opcode);
            words.AddRange(operands);
        }
        uint[] Text(string text)
        {
            byte[] bytes = new byte[(Encoding.UTF8.GetByteCount(text) + 4) / 4 * 4];
            Encoding.UTF8.GetBytes(text, bytes);
            return MemoryMarshal.Cast<byte, uint>(bytes).ToArray();
        }
        Op(5, [5, .. Text("parameters")]);
        Op(6, [3, 0, .. Text("amount")]);
        Op(6, [3, 1, .. Text("tint")]);
        Op(71, 3, 2); // Block
        Op(72, 3, 0, 35, 0); // Offset
        Op(72, 3, 1, 35, 16);
        Op(71, 5, 34, 0); // DescriptorSet
        Op(71, 5, 33, 2); // Binding
        Op(22, 1, 32); // float
        Op(23, 2, 1, 4); // vec4
        Op(30, 3, 1, 2); // struct
        Op(32, 4, 2, 3); // Uniform pointer
        Op(59, 4, 5, 2); // variable
        byte[] spirv = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(words)).ToArray();

        var metadata = ShaderAuthoringInterface.Read(spirv, ShaderStage.Fragment);
        var descriptor = metadata.Descriptors.Single();
        Assert.That(descriptor.Name, Is.EqualTo("parameters"));
        Assert.That(descriptor.Binding, Is.EqualTo(2));
        Assert.That(descriptor.Type, Is.EqualTo(DescriptorType.UniformBuffer));
        Assert.That(descriptor.MinimumSize, Is.EqualTo(32));
        Assert.That(descriptor.Members.Select(member => member.Name), Is.EqualTo(new[] { "amount", "tint" }));
        Assert.That(descriptor.Members.Select(member => member.Offset), Is.EqualTo(new[] { 0, 16 }));
        Assert.That(descriptor.Members[1].Rows, Is.EqualTo(4));
    }
}

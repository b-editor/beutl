using Beutl.Graphics.Backend;
using BeutlDescriptorType = Beutl.Graphics.Backend.DescriptorType;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Pins the descriptor writes the Vulkan backend owes its callers a diagnosis for: a binding the pipeline
/// layout never declared, a write whose descriptor type disagrees with the declaration, and a write that
/// runs past the declared array. vkUpdateDescriptorSets sees a binding number and a type with nothing
/// tying either to the layout the set was allocated from, so none of the three is an error the driver has
/// to report - the declarations have to be kept and checked on the managed side.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VulkanDescriptorBindingValidationTests
{
    private const int Width = 16;
    private const int Height = 8;

    // Binding 0 is sampled and binding 1 is read by the fragment shader below. Binding 2 is declared with a
    // count of zero, which Vulkan defines as a reserved binding no shader may access, so it is the layout's
    // empty array. Binding 3 is declared by nobody.
    private const int SamplerBinding = 0;
    private const int UniformBinding = 1;
    private const int EmptyArrayBinding = 2;
    private const int UndeclaredBinding = 3;

    [Test]
    [Category("GpuPassFusionGpu")]
    public void AWriteToAnUndeclaredBinding_IsRejected()
    {
        RunWithDescriptorSet((_, set, texture, sampler, _) =>
        {
            ArgumentException? error = Assert.Throws<ArgumentException>(
                () => set.UpdateTexture(UndeclaredBinding, texture, sampler),
                "the pipeline layout declares no binding 3, so the write names a slot that does not exist");
            TestContext.WriteLine(error!.Message);
            Assert.That(error.Message, Does.Contain("3"), "the message must name the binding");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void AWriteWhoseTypeDisagreesWithTheDeclaration_IsRejected()
    {
        RunWithDescriptorSet((_, set, texture, sampler, buffer) =>
        {
            ArgumentException? asBuffer = Assert.Throws<ArgumentException>(
                () => set.UpdateBuffer(SamplerBinding, buffer),
                "binding 0 is a combined image sampler, so a uniform buffer cannot be written into it");
            ArgumentException? asTexture = Assert.Throws<ArgumentException>(
                () => set.UpdateTexture(UniformBinding, texture, sampler),
                "binding 1 is a uniform buffer, so a combined image sampler cannot be written into it");

            TestContext.WriteLine(asBuffer!.Message);
            TestContext.WriteLine(asTexture!.Message);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(asBuffer.Message, Does.Contain("CombinedImageSampler"));
                Assert.That(asBuffer.Message, Does.Contain("UniformBuffer"));
                Assert.That(asTexture.Message, Does.Contain("UniformBuffer"));
                Assert.That(asTexture.Message, Does.Contain("CombinedImageSampler"));
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void AWriteBeyondTheDeclaredArray_IsRejected()
    {
        RunWithDescriptorSet((_, set, texture, sampler, _) =>
        {
            ArgumentException? pastEnd = Assert.Throws<ArgumentException>(
                () => set.UpdateTexture(EmptyArrayBinding, texture, sampler),
                "binding 2 declares zero descriptors, so array element 0 is already past its end");
            ArgumentOutOfRangeException? negative = Assert.Throws<ArgumentOutOfRangeException>(
                () => set.UpdateTexture(-1, texture, sampler),
                "a negative binding must not be reinterpreted as a very large unsigned one");

            TestContext.WriteLine(pastEnd!.Message);
            TestContext.WriteLine(negative!.Message);
            Assert.That(pastEnd.Message, Does.Contain("2"), "the message must name the binding");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void AWriteThatMatchesTheDeclaration_Succeeds()
    {
        RunWithDescriptorSet((context, set, texture, sampler, buffer) =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    () => set.UpdateTexture(SamplerBinding, texture, sampler),
                    Throws.Nothing,
                    "binding 0 is declared as a combined image sampler and is written as one");
                Assert.That(
                    () => set.UpdateBuffer(UniformBinding, buffer),
                    Throws.Nothing,
                    "binding 1 is declared as a uniform buffer and is written as one");
            }

            context.WaitIdle();
        });
    }

    private static void RunWithDescriptorSet(
        Action<IGraphicsContext, IDescriptorSet, ITexture2D, ISampler, IBuffer> body)
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            IShaderCompiler compiler = context.CreateShaderCompiler();
            byte[] vertex = compiler.CompileToSpirv(VertexShader, ShaderStage.Vertex);
            byte[] fragment = compiler.CompileToSpirv(FragmentShader, ShaderStage.Fragment);

            using IRenderPass3D pass = context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            DescriptorBinding[] bindings =
            [
                new(SamplerBinding, BeutlDescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment),
                new(UniformBinding, BeutlDescriptorType.UniformBuffer, 1, ShaderStage.Fragment),
                new(EmptyArrayBinding, BeutlDescriptorType.CombinedImageSampler, 0, ShaderStage.Fragment),
            ];
            using IPipeline3D pipeline = context.CreatePipeline3D(
                pass,
                vertex,
                fragment,
                bindings,
                VertexInputDescription.Empty,
                PipelineOptions.Fullscreen);
            using IDescriptorSet descriptors = context.CreateDescriptorSet(
                pipeline,
                [
                    new DescriptorPoolSize(BeutlDescriptorType.CombinedImageSampler, 1),
                    new DescriptorPoolSize(BeutlDescriptorType.UniformBuffer, 1),
                ]);

            using ITexture2D texture = context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
            using ISampler sampler = context.CreateSampler();
            using IBuffer buffer = context.CreateBuffer(
                256,
                BufferUsage.UniformBuffer,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            body(context, descriptors, texture, sampler, buffer);
        });
    }

    private const string VertexShader = """
        #version 450

        layout(location = 0) out vec2 fragCoord;

        void main() {
            vec2 positions[3] = vec2[](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
            vec2 uvs[3] = vec2[](vec2(0.0, 0.0), vec2(2.0, 0.0), vec2(0.0, 2.0));
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            fragCoord = uvs[gl_VertexIndex];
        }
        """;

    private const string FragmentShader = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(binding = 0) uniform sampler2D sourceTexture;
        layout(binding = 1) uniform Tint { vec4 color; } tint;

        void main() {
            outColor = texture(sourceTexture, fragCoord) * tint.color;
        }
        """;
}

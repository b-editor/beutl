using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Nodes;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

// The pipelines for casters with holes in them, which the first such caster to draw makes.
[TestFixture]
public sealed class ShadowCasterPipelinesTests
{
    // A failure part way leaves nothing for the next attempt to write over, so every pipeline made is disposed.
    [Test]
    public void A_failure_while_making_the_cutout_pipelines_leaves_none_of_them_behind()
    {
        var made = new List<Mock<IPipeline3D>>();
        int calls = 0;
        var context = new Mock<IGraphicsContext>();
        context.Setup(c => c.CreatePipeline3D(
                It.IsAny<IRenderPass3D>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<DescriptorBinding[]>(),
                It.IsAny<VertexInputDescription>(), It.IsAny<PipelineOptions?>()))
            .Returns(() =>
            {
                // The double-sided pipeline, made after the one-sided one, fails every time.
                if (++calls % 2 == 0) throw new InvalidOperationException("The device refused the pipeline.");
                var pipeline = new Mock<IPipeline3D>();
                made.Add(pipeline);
                return pipeline.Object;
            });
        var compiler = new Mock<IShaderCompiler>();
        compiler.Setup(c => c.CompileToSpirv(It.IsAny<string>(), It.IsAny<ShaderStage>(), It.IsAny<string>())).Returns([]);
        var pipelines = new ShadowCasterPipelines("vertex", "fragment");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            Assert.Throws<InvalidOperationException>(() => pipelines.BindCutout(
                context.Object, compiler.Object, Mock.Of<IRenderPass3D>(), false, Mock.Of<ITexture2D>(), Matrix4x4.Identity));
        }

        pipelines.Dispose();

        Assert.That(made, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            foreach (Mock<IPipeline3D> pipeline in made)
                pipeline.Verify(p => p.Dispose(), Times.Once);
        });
    }
}

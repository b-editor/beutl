using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.Serialization;
using static Beutl.Graphics3DTests.ShaderGraphEffectTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphMigrationTests
{
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void ScriptPreservesTimelineAndImageParametersAcrossSeeks(float scale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new GLSLScriptEffect
            {
                TimeRange = { Value = TimeRange.FromSeconds(2, 8) },
                FragmentShader = { Value = """
                    #version 450
                    layout(location = 0) out vec4 color;
                    layout(push_constant) uniform Parameters {
                        float progress; float duration; float time;
                        float width; float height; float scale;
                    } pc;
                    void main() {
                        color = gl_FragCoord.x < 1.0
                            ? vec4(pc.progress, pc.duration, pc.time, 1)
                            : vec4(pc.width, pc.height, pc.scale, 1);
                    }
                    """ }
            };
            Rect bounds = new(10.25f, 20.25f, 8, 6);
            PixelSize size = PixelRect.FromRect(bounds, scale).Size;
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            foreach (int time in new[] { 4, 8, 4 })
            {
                using FilterEffect.Resource resource = effect.ToResource(new CompositionContext(MediaTime.FromSeconds(time)));
                using var context = new FilterEffectContext(bounds);
                effect.ApplyTo(context, resource);
                var graph = ((FEItem_ShaderGraph)context.GetOrderedItems().Single()).Description;
                byte[] recorded = graph.Passes[0].Constants.ToArray();
                float[] pixels = ShaderGraphContractTests.Execute(graph, bounds, scale, cache: cache).Pixels;
                Assert.That(pixels.Take(4), Is.EqualTo(new[] { (time - 2) / 8f, 8f, time - 2f, 1f }));
                Assert.That(pixels.Skip(4).Take(4), Is.EqualTo(new[] { (float)size.Width, size.Height, scale, 1 }));
                Assert.That(graph.Passes[0].Constants, Is.EqualTo(recorded), "Execution must not mutate the recorded ABI bytes.");
            }
            Assert.That(cache.Statistics.Creations, Is.EqualTo(2), "Only the script and boundary-copy pipelines are compiled.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExistingSettingsRoundTrip(bool script)
    {
        FilterEffect effect = script
            ? new GLSLScriptEffect { FragmentShader = { Value = "#version 450\nlayout(location=0) out vec4 color; void main() { color=vec4(0.5); }" } }
            : new PixelSortEffect
            {
                Direction = { Value = PixelSortDirection.Vertical },
                SortKey = { Value = PixelSortKey.Hue },
                ThresholdMin = { Value = 12 },
                ThresholdMax = { Value = 67 },
                Ascending = { Value = false }
            };
        var group = new FilterEffectGroup { Children = { effect } };
        var saved = CoreSerializer.SerializeToJsonObject(group);
        var loaded = (FilterEffectGroup)CoreSerializer.DeserializeFromJsonObject(saved, typeof(FilterEffectGroup));
        Assert.That(Record(loaded.Children.Single()).OutputIdentity, Is.EqualTo(Record(effect).OutputIdentity));
    }

    [Test]
    public void ScriptRecordingRejectsInvalidSourceWithoutCreatingADeviceProgram()
    {
        var effect = new GLSLScriptEffect { FragmentShader = { Value = "unfinished user shader" } };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new(0, 0, 8, 8));
        effect.ApplyTo(context, resource);
        Assert.That(context.GetOrderedItems(), Is.Empty);
    }

    [Test]
    public void RadialCenterAndExpansionInvalidateResultsButReusePrograms()
    {
        var effect = new RadialBlur();
        ShaderGraphDescription first = Record(effect);
        effect.Amount.Value = 50;
        effect.Center.Value = new RelativePoint(0.25f, 0.75f, RelativeUnit.Relative);
        ShaderGraphDescription second = Record(effect);
        Assert.That(second.OutputIdentity, Is.Not.EqualTo(first.OutputIdentity));
        Assert.That(second.StructuralIdentity, Is.EqualTo(first.StructuralIdentity));
        Assert.That(ShaderGraphDescription.GetPipelineIdentity(second.Passes[0]),
            Is.EqualTo(ShaderGraphDescription.GetPipelineIdentity(first.Passes[0])));
    }
}

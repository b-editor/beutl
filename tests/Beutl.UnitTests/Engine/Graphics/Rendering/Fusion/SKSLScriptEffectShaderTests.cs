using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
[NonParallelizable]
public sealed class SKSLScriptEffectShaderTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    [Test]
    public void MainScript_RecordsWholeSourceWithoutEffectItemBoundary()
    {
        var effect = new SKSLScriptEffect
        {
            Script =
            {
                CurrentValue =
                    """
                    uniform shader src;
                    uniform float progress;
                    uniform float customValue;

                    half4 main(float2 fragCoord) {
                        return src.eval(fragCoord) + half4(customValue);
                    }
                    """,
            },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);
        using var secondContext = new FilterEffectContext(s_bounds);

        context.ApplyTransactional(effect, resource);
        secondContext.ApplyTransactional(effect, resource);

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        var shader = (FEItem_Shader)items.Single();
        var secondShader = (FEItem_Shader)secondContext.GetOrderedItems().Single();
        Assert.Multiple(() =>
        {
            Assert.That(items.OfType<IFEItem_Custom>(), Is.Empty);
            Assert.That(shader.Description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(shader.Description.SourceTileMode, Is.EqualTo(SKShaderTileMode.Clamp));
            Assert.That(shader.Description.Bounds.RequiresFullInput, Is.True);
            Assert.That(shader.Description.Resources, Is.Empty);
            Assert.That(
                shader.Description.Uniforms.Select(static binding => binding.Name),
                Is.EqualTo(new[] { "progress", "customValue" }));
            Assert.That(secondShader.Description, Is.Not.SameAs(shader.Description));
            Assert.That(secondShader.Description.Source, Is.SameAs(shader.Description.Source));
            Assert.That(
                secondShader.Description.StructuralIdentity,
                Is.EqualTo(shader.Description.StructuralIdentity));
        });
    }

    [Test]
    public void ApplyScript_FusesWithFollowingColorStage()
    {
        var script = new SKSLScriptEffect
        {
            Script =
            {
                CurrentValue =
                    """
                    half4 apply(half4 color) {
                        return half4(color.rgb * 0.5, color.a);
                    }
                    """,
            },
        };

        using CompiledRenderRequest compiled = Compile(
            script,
            new Invert { Amount = { CurrentValue = 25f } });

        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        TestContext.WriteLine(
            $"SKSL apply -> Invert: {compiled.ExecutionPlan.Islands.Length} islands, "
            + $"{compiled.ExecutionPlan.ShaderRuns.Count()} shader run, {run.StageFragmentIndices.Length} stages");
        Assert.Multiple(() =>
        {
            Assert.That(
                compiled.ExecutionPlan.Islands.Select(static island => island.ShaderRun is not null),
                Is.EqualTo(new[] { false, true }));
            Assert.That(compiled.ExecutionPlan.ShaderRuns, Has.Exactly(1).Items);
            Assert.That(run.StageFragmentIndices, Has.Length.EqualTo(2));
            Assert.That(
                Enumerable.Range(0, run.StageFragmentIndices.Length)
                    .Select(index => run.GetDescription(compiled.Graph, index).Kind),
                Is.EqualTo(new[]
                {
                    ShaderDescriptionKind.CurrentPixel,
                    ShaderDescriptionKind.CurrentPixel,
                }));
            Assert.That(run.GetWholeSourceHead(compiled.Graph), Is.Null);
        });
    }

    [Test]
    public void ReservedBindingName_FallsBackToCustomEffectItem()
    {
        var effect = new SKSLScriptEffect
        {
            Script =
            {
                CurrentValue =
                    """
                    uniform shader src;
                    uniform float fe0_value;

                    half4 main(float2 fragCoord) {
                        return src.eval(fragCoord) + half4(fe0_value);
                    }
                    """,
            },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);

        context.ApplyTransactional(effect, resource);

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        Assert.Multiple(() =>
        {
            Assert.That(items.OfType<IFEItem_Custom>(), Has.Exactly(1).Items);
            Assert.That(items.OfType<FEItem_Shader>(), Is.Empty);
        });
    }

    [Test]
    public void IntegerArrayWithoutCanonicalZero_FallsBackToCustomEffectItem()
    {
        var effect = new SKSLScriptEffect
        {
            Script =
            {
                CurrentValue =
                    """
                    uniform shader src;
                    uniform int values[2];

                    half4 main(float2 fragCoord) {
                        return src.eval(fragCoord) + half4(float(values[0]));
                    }
                    """,
            },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);

        context.ApplyTransactional(effect, resource);

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        Assert.Multiple(() =>
        {
            Assert.That(items.OfType<IFEItem_Custom>(), Has.Exactly(1).Items);
            Assert.That(items.OfType<FEItem_Shader>(), Is.Empty);
        });
    }

    [Test]
    public void OutputSizeUniforms_UseSemanticOutputAndClampedWorkingScale()
    {
        var effect = new SKSLScriptEffect
        {
            Script =
            {
                CurrentValue =
                    """
                    uniform shader src;
                    uniform float width;
                    uniform float height;
                    uniform float2 iResolution;
                    uniform float iScale;

                    half4 main(float2 fragCoord) {
                        return src.eval(fragCoord);
                    }
                    """,
            },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var recording = new FilterEffectContext(s_bounds);
        recording.ApplyTransactional(effect, resource);
        ShaderDescription description = ((FEItem_Shader)recording.GetOrderedItems().Single()).Description;
        var token = new RenderExecutionSessionToken();

        Dictionary<string, ShaderUniformValue> values = token.RunAndComplete(() =>
        {
            var execution = new ShaderExecutionContext(
                token,
                s_bounds,
                s_bounds,
                new Rect(0, 0, 2, 3),
                new PixelRect(0, 0, 2, 3),
                default,
                EffectiveScale.At(1),
                outputScale: 1,
                workingScale: 2,
                maxWorkingScale: 2,
                RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary);
            return description.Uniforms.ToDictionary(
                static binding => binding.Name,
                binding => binding.Bind(description.Source.Uniforms[binding.Name], execution));
        });

        Assert.Multiple(() =>
        {
            Assert.That(values["width"].Floats, Is.EqualTo(new[] { 32f }));
            Assert.That(values["height"].Floats, Is.EqualTo(new[] { 24f }));
            Assert.That(values["iResolution"].Floats, Is.EqualTo(new[] { 32f, 24f }));
            Assert.That(values["iScale"].Floats, Is.EqualTo(new[] { 2f }));
        });
    }

    [Test]
    public void TimeUniforms_AreSnapshottedWhenShaderIsRecorded()
    {
        var effect = new SKSLScriptEffect
        {
            TimeRange = TimeRange.FromSeconds(8),
            Script =
            {
                CurrentValue =
                    """
                    uniform shader src;
                    uniform float progress;
                    uniform float duration;
                    uniform float time;
                    uniform float iTime;

                    half4 main(float2 coord) {
                        bool recorded = progress == 0.25 && duration == 8.0
                            && time == 2.0 && iTime == 2.0;
                        return recorded
                            ? half4(0.0, 1.0, 0.0, 1.0)
                            : half4(1.0, 0.0, 1.0, 1.0);
                    }
                    """,
            },
        };
        using FilterEffect.Resource resource = effect.ToResource(
            new CompositionContext(TimeSpan.FromSeconds(2)));
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new RectangleRenderNode(s_bounds, Brushes.Resource.White, null));
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Enabled));
        CompiledRenderRequest compiled;
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            compiled = new RenderRequestCompiler().Compile(request, graph, SkslBackendBudgetResolver.Portable);
        }
        catch
        {
            request.Dispose();
            throw;
        }

        using (compiled)
        {
            bool updateOnly = false;
            resource.Update(
                effect,
                new CompositionContext(TimeSpan.FromSeconds(6)),
                ref updateOnly);

            using var targetRegistry = new RenderTargetPool(new CpuTargetFactory());
            using RenderTargetLeaseSession targets = targetRegistry.BeginSession(RenderIntent.Preview);
            PixelRect deviceBounds = PixelRect.FromRect(compiled.ExecutionTargetBounds, 1);
            using RenderTargetLease output = targets.Acquire(deviceBounds.Size);
            using var canvas = new ImmediateCanvas(
                output.Target,
                RenderIntent.Preview,
                density: 1,
                maxWorkingScale: 1,
                logicalSize: compiled.ExecutionTargetBounds.Size);
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(
                       -compiled.ExecutionTargetBounds.X,
                       -compiled.ExecutionTargetBounds.Y)))
            {
                new RenderRequestExecutor(targets).Execute(compiled, canvas);
            }

            using Bitmap bitmap = output.Target.Snapshot();
            SKColor color = bitmap.SKBitmap.GetPixel(8, 6);
            Assert.Multiple(() =>
            {
                Assert.That(color.Green, Is.GreaterThanOrEqualTo(250),
                    "the recorded request must retain its T1 progress/duration/time/iTime values");
                Assert.That(color.Red, Is.LessThanOrEqualTo(5));
                Assert.That(color.Blue, Is.LessThanOrEqualTo(5));
            });
        }
    }

    [Test]
    public void SourceLessGenerator_InjectsImplicitSourceAndRenders()
    {
        var effect = new SKSLScriptEffect
        {
            Script =
            {
                CurrentValue =
                    """
                    half4 main(float2 fragCoord) {
                        return half4(1.0, 0.0, 0.0, 1.0);
                    }
                    """,
            },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 2, 2));
        context.ApplyTransactional(effect, resource);
        var shader = (FEItem_Shader)context.GetOrderedItems().Single();
        using RenderTarget backing = new CpuRenderTarget(2, 2);
        backing.Value.Canvas.Clear(SKColors.Transparent);
        backing.Value.Canvas.Flush();
        using var targets = new EffectTargets
        {
            new EffectTarget(
                backing,
                new Rect(0, 0, 2, 2),
                EffectiveScale.At(1),
                new PixelRect(0, 0, 2, 2)),
        };
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        activator.Apply(context);
        activator.Flush(false);

        using Bitmap bitmap = targets.Single().RenderTarget!.Snapshot();
        SKColor[] pixels = Enumerable.Range(0, bitmap.Width * bitmap.Height)
            .Select(index => bitmap.SKBitmap.GetPixel(index % bitmap.Width, index / bitmap.Width))
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(shader.Description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(shader.Description.Source.Uniforms["src"].IsShader, Is.True);
            Assert.That(context.GetOrderedItems().OfType<IFEItem_Custom>(), Is.Empty);
            Assert.That(pixels, Has.All.Matches<SKColor>(static pixel =>
                pixel.Red >= 250 && pixel.Green <= 5 && pixel.Blue <= 5 && pixel.Alpha >= 250));
        });
    }

    private static CompiledRenderRequest Compile(params FilterEffect[] effects)
    {
        var group = new FilterEffectGroup();
        foreach (FilterEffect effect in effects)
            group.Children.Add(effect);

        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(s_bounds, Brushes.Resource.White, null));
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: s_bounds,
            requestedRegion: null,
            cachePolicy: Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Enabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            return new RenderRequestCompiler().Compile(request, graph, SkslBackendBudgetResolver.Portable);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height)
    {
        private static SKSurface CreateSurface(int width, int height)
            => SKSurface.Create(new SKImageInfo(
                   width,
                   height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   SKColorSpace.CreateSrgbLinear()))
               ?? throw new InvalidOperationException("Failed to create the CPU test surface.");
    }
}

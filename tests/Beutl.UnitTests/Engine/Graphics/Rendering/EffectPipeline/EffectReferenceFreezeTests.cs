using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Verifies the 004 reference renders (T004) at output scale 1.0 and
/// <see cref="SceneFixtures.ReferenceSize"/>. A missing pre-redesign baseline always fails and must be restored
/// from source control; the implementation under test can never recreate it. Post-redesign determinism references
/// live under <c>004-review</c> and may be regenerated only when their intended output changes.
/// </summary>
[NonParallelizable]
[TestFixture]
public class EffectReferenceFreezeTests
{
    private const string Category = "004-baseline";

    // LutEffect's static constructor resolves an ILogger via BeutlApplication.Current.LoggerFactory, which
    // is null in the bare unit-test harness. Seed it so effects that log at type-init can be instantiated.
    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    /// <summary>The effect census (research §0; the frozen reference set holds 45 files incl. the chain/NodeGraph cases). <c>RequiresCompute</c> marks Vulkan-compute-only effects.</summary>
    public static IEnumerable<TestCaseData> CensusEffects()
    {
        foreach ((string name, Func<FilterEffect> make) in InvariantColorEffects())
            yield return Case(name, make, requiresCompute: false);
        foreach ((string name, Func<FilterEffect> make) in SpatialEffects())
            yield return Case(name, make, requiresCompute: false);
        foreach ((string name, Func<FilterEffect> make) in GeometryEffects())
            yield return Case(name, make, requiresCompute: false);
        foreach ((string name, Func<FilterEffect> make) in SplitEffects())
            yield return Case(name, make, requiresCompute: false);
        foreach ((string name, Func<FilterEffect> make) in MetaEffects())
            yield return Case(name, make, requiresCompute: false);

        // Script effects with fixed sources.
        yield return Case("SKSLScriptEffect", () =>
        {
            var e = new SKSLScriptEffect();
            e.Script.CurrentValue =
                "uniform shader src;\nhalf4 main(float2 fc) { half4 c = src.eval(fc); return half4(1.0 - c.rgb, c.a); }";
            return e;
        }, requiresCompute: false);
        // This is a post-removal Builder-surface determinism anchor, not an independent legacy parity gate. The
        // blob was re-frozen at 60f7b4731 and updated at 2097c930d. It lives under 004-review and may be regenerated
        // only when the intended Builder script output changes.
        yield return Case("CSharpScriptEffect", () =>
        {
            var e = new CSharpScriptEffect();
            e.Script.CurrentValue = "Builder.Blur(new Size(4, 4));";
            return e;
        }, requiresCompute: false);

        // Vulkan-compute effects: gated on Supports3DRendering.
        yield return Case("GLSLScriptEffect", () =>
        {
            var e = new GLSLScriptEffect();
            e.FragmentShader.CurrentValue =
                "#version 450\n"
                + "layout(location = 0) in vec2 fragCoord;\n"
                + "layout(location = 0) out vec4 outColor;\n"
                + "layout(set = 0, binding = 0) uniform sampler2D srcTexture;\n"
                + "void main() { vec4 c = texture(srcTexture, fragCoord); outColor = vec4(1.0 - c.rgb, c.a); }";
            return e;
        }, requiresCompute: true);
        yield return Case("PixelSortEffect", () =>
        {
            var e = new PixelSortEffect();
            e.ThresholdMin.CurrentValue = 0.2f;
            e.ThresholdMax.CurrentValue = 0.8f;
            return e;
        }, requiresCompute: true);
    }

    /// <summary>The four O3 chain scenes.</summary>
    public static IEnumerable<TestCaseData> ChainScenes()
    {
        foreach (string scene in SceneFixtures.SceneNames)
            yield return new TestCaseData(scene, (Func<Drawable.Resource>)(() => SceneFixtures.Build(scene, SceneFixtures.ReferenceSize)))
                .SetName($"Chain_{scene}");
    }

    [TestCaseSource(nameof(CensusEffects))]
    public void Effect_MatchesReference(string name, Func<FilterEffect> makeEffect, bool requiresCompute)
    {
        string referenceName = $"effect-{name}";
        if (name == "CSharpScriptEffect")
            FreezeReview(referenceName, () => MakeShape(makeEffect), requiresCompute);
        else
            AssertFrozen(referenceName, () => MakeShape(makeEffect), requiresCompute);
    }

    [TestCaseSource(nameof(ChainScenes))]
    public void Chain_MatchesReference(string name, Func<Drawable.Resource> makeScene)
    {
        AssertFrozen($"chain-{name}", makeScene, requiresCompute: false);
    }

    public static IEnumerable<TestCaseData> ReviewChainScenes()
    {
        foreach (string scene in ReviewSceneFixtures.SceneNames)
        {
            yield return new TestCaseData(
                    scene,
                    (Func<Drawable.Resource>)(() =>
                        ReviewSceneFixtures.Build(scene, ReviewSceneFixtures.ReferenceSize)))
                .SetName($"ReviewChain_{scene}");
        }
    }

    [TestCaseSource(nameof(ReviewChainScenes))]
    public void StrengthenedChain_MatchesReviewReference(string name, Func<Drawable.Resource> makeScene)
    {
        FreezeReview($"chain-{name}", makeScene, requiresCompute: false);
    }

    [Test]
    public void StrengthenedPixelSort_MatchesReviewReference()
    {
        FreezeReview("effect-PixelSortEffect", () => MakeShape(() => new PixelSortEffect
        {
            ThresholdMin = { CurrentValue = 20f },
            ThresholdMax = { CurrentValue = 80f },
        }), requiresCompute: true);
    }

    // NodeGraphFilterEffect never flows through the shape FilterEffect path the same way; build the graph
    // explicitly (Input -> FilterEffectNode<Invert> -> Output) and drive it through a host shape.
    [Test]
    public void NodeGraphFilterEffect_MatchesReference()
    {
        AssertFrozen("effect-NodeGraphFilterEffect", MakeNodeGraphHost, requiresCompute: false);
    }

    // A post-redesign determinism reference (NOT a pre-redesign parity gate): a spaced SplitEffect followed by a
    // non-invariant DropShadow. Pins the review-B1 fix — every fan-out branch is sized/placed from its own bounds
    // so the outer tiles' shadows are not clipped to the graph-level rect. Frozen from the current pipeline under a
    // distinct category; it may be regenerated only if the scene's intended output changes.
    [Test]
    public void SplitDropShadow_Determinism_FreezesOrMatchesReference()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap actual = GoldenImageHarness.RenderAtScale(
                MakeSplitDropShadowScene(), SceneFixtures.ReferenceSize, 1f);
            GoldenReferenceStore.FreezeOrAssert("004-review", "chain-SplitDropShadow", actual);
        });
    }

    private static Drawable.Resource MakeSplitDropShadowScene()
    {
        var split = new SplitEffect();
        split.HorizontalDivisions.CurrentValue = 3;
        split.VerticalDivisions.CurrentValue = 1;
        split.HorizontalSpacing.CurrentValue = 40;

        var shadow = new DropShadow();
        shadow.Position.CurrentValue = new Point(6, 6);
        shadow.Sigma.CurrentValue = new Size(3, 3);
        shadow.Color.CurrentValue = Colors.Black;

        var group = new FilterEffectGroup();
        group.Children.Add(split);
        group.Children.Add(shadow);
        return MakeShape(() => group);
    }

    private static void AssertFrozen(string name, Func<Drawable.Resource> makeResource, bool requiresCompute)
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        if (requiresCompute)
            VulkanTestEnvironment.RequireComputeCapable(context, name);

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap actual = GoldenImageHarness.RenderAtScale(makeResource(), SceneFixtures.ReferenceSize, 1f);
            GoldenReferenceStore.AssertExisting(Category, name, actual);
        });
    }

    private static void FreezeReview(string name, Func<Drawable.Resource> makeResource, bool requiresCompute)
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        if (requiresCompute)
            VulkanTestEnvironment.RequireComputeCapable(context, name);

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap actual = GoldenImageHarness.RenderAtScale(
                makeResource(), ReviewSceneFixtures.ReferenceSize, 1f);
            GoldenReferenceStore.FreezeOrAssert("004-review", name, actual);
        });
    }

    private static Drawable.Resource MakeShape(Func<FilterEffect> makeEffect)
    {
        var fill = new LinearGradientBrush();
        fill.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        fill.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
        fill.GradientStops.Add(new GradientStop(Colors.Red, 0));
        fill.GradientStops.Add(new GradientStop(Colors.Lime, 0.5f));
        fill.GradientStops.Add(new GradientStop(Colors.Blue, 1));

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 240;
        shape.Height.CurrentValue = 150;
        shape.Fill.CurrentValue = fill;

        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 12f;
        shape.Transform.CurrentValue = rotation;

        shape.FilterEffect.CurrentValue = makeEffect();
        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource MakeNodeGraphHost()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;

        var inputNode = new FilterEffectInputNode();
        var invertNode = new FilterEffectNode<Invert>();
        invertNode.Object.Amount.CurrentValue = 1f;
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(invertNode);
        model.Nodes.Add(outputNode);

        var chainInput = (IInputPort)invertNode.Items[1];
        var chainOutput = (IOutputPort)invertNode.Items[0];
        model.Connect(chainInput, inputNode.Output);
        model.Connect(outputNode.InputPort, chainOutput);

        return MakeShape(() => effect);
    }

    private static TestCaseData Case(string name, Func<FilterEffect> make, bool requiresCompute)
        => new TestCaseData(name, make, requiresCompute).SetName($"Effect_{name}");

    private static IEnumerable<(string, Func<FilterEffect>)> InvariantColorEffects()
    {
        yield return ("Gamma", () => { var e = new Gamma(); e.Amount.CurrentValue = 1.5f; return e; });
        yield return ("Invert", () => { var e = new Invert(); e.Amount.CurrentValue = 1f; return e; });
        yield return ("Threshold", () => { var e = new Threshold(); e.Value.CurrentValue = 0.5f; return e; });
        yield return ("ColorGrading", () =>
        {
            var e = new ColorGrading();
            e.Contrast.CurrentValue = 1.2f;
            e.Saturation.CurrentValue = 1.3f;
            e.Exposure.CurrentValue = 0.2f;
            return e;
        }
        );
        yield return ("Curves", () =>
        {
            // Default curves are all linear, i.e. identity output — the reference would be blind to a no-op.
            var e = new Curves();
            e.MasterCurve.CurrentValue = new CurveMap(
                [new CurveControlPoint(0, 0), new CurveControlPoint(0.5f, 0.72f), new CurveControlPoint(1, 1)]);
            e.RedCurve.CurrentValue = new CurveMap(
                [new CurveControlPoint(0, 0.1f), new CurveControlPoint(1, 0.9f)]);
            return e;
        }
        );
        yield return ("Negaposi", () => { var e = new Negaposi(); e.Strength.CurrentValue = 1f; return e; });
        yield return ("ChromaKey", () => { var e = new ChromaKey(); e.Color.CurrentValue = Colors.Lime; return e; });
        yield return ("ColorKey", () => { var e = new ColorKey(); e.Color.CurrentValue = Colors.Lime; return e; });
        // A source-less LutEffect renders identity; the fixed inverting cube makes the reference non-vacuous.
        yield return ("LutEffect", () =>
        {
            var e = new LutEffect();
            e.Source.CurrentValue = SceneFixtures.CreateInvertLutSource();
            return e;
        }
        );
        yield return ("Saturate", () => { var e = new Saturate(); e.Amount.CurrentValue = 1.5f; return e; });
        yield return ("HueRotate", () => { var e = new HueRotate(); e.Angle.CurrentValue = 90f; return e; });
        yield return ("Brightness", () => { var e = new Brightness(); e.Amount.CurrentValue = 1.2f; return e; });
        yield return ("HighContrast", () => { var e = new HighContrast(); e.Contrast.CurrentValue = 0.5f; return e; });
        yield return ("Lighting", () =>
        {
            var e = new Lighting();
            e.Multiply.CurrentValue = Colors.LightGray;
            e.Add.CurrentValue = Color.FromArgb(255, 20, 20, 20);
            return e;
        }
        );
        yield return ("LumaColor", () => new LumaColor());
    }

    private static IEnumerable<(string, Func<FilterEffect>)> SpatialEffects()
    {
        yield return ("Blur", () => { var e = new Blur(); e.Sigma.CurrentValue = new Size(8, 8); return e; });
        yield return ("DropShadow", () =>
        {
            var e = new DropShadow();
            e.Position.CurrentValue = new Point(8, 8);
            e.Sigma.CurrentValue = new Size(6, 6);
            e.Color.CurrentValue = Colors.Black;
            return e;
        }
        );
        yield return ("Dilate", () => { var e = new Dilate(); e.RadiusX.CurrentValue = 3; e.RadiusY.CurrentValue = 3; return e; });
        yield return ("Erode", () => { var e = new Erode(); e.RadiusX.CurrentValue = 3; e.RadiusY.CurrentValue = 3; return e; });
        yield return ("InnerShadow", () =>
        {
            var e = new InnerShadow();
            e.Position.CurrentValue = new Point(10, 10);
            e.Sigma.CurrentValue = new Size(6, 6);
            e.Color.CurrentValue = Colors.Black;
            return e;
        }
        );
        yield return ("TransformEffect", () =>
        {
            var rot = new RotationTransform();
            rot.Rotation.CurrentValue = 30f;
            var e = new TransformEffect();
            e.Transform.CurrentValue = rot;
            return e;
        }
        );
        yield return ("MosaicEffect", () => { var e = new MosaicEffect(); e.TileSize.CurrentValue = new Size(16, 16); return e; });
        yield return ("ColorShift", () =>
        {
            var e = new ColorShift();
            e.RedOffset.CurrentValue = new PixelPoint(6, 0);
            e.BlueOffset.CurrentValue = new PixelPoint(-6, 0);
            return e;
        }
        );
        yield return ("DisplacementMapEffect", () =>
        {
            var map = new LinearGradientBrush();
            map.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
            map.EndPoint.CurrentValue = new RelativePoint(1, 0, RelativeUnit.Relative);
            map.GradientStops.Add(new GradientStop(Colors.Black, 0));
            map.GradientStops.Add(new GradientStop(Colors.White, 1));
            var transform = new DisplacementMapTranslateTransform();
            transform.X.CurrentValue = 12;
            transform.Y.CurrentValue = 0;
            var e = new DisplacementMapEffect();
            e.DisplacementMap.CurrentValue = map;
            e.Transform.CurrentValue = transform;
            e.Channel.CurrentValue = DisplacementMapChannel.Luminance;
            return e;
        }
        );
    }

    private static IEnumerable<(string, Func<FilterEffect>)> GeometryEffects()
    {
        yield return ("FlatShadow", () =>
        {
            var e = new FlatShadow();
            e.Angle.CurrentValue = 0;
            e.Length.CurrentValue = 40;
            e.Brush.CurrentValue = Brushes.Red;
            return e;
        }
        );
        yield return ("StrokeEffect", () =>
        {
            var pen = new Pen();
            pen.Thickness.CurrentValue = 14;
            pen.Brush.CurrentValue = Brushes.Red;
            var e = new StrokeEffect();
            e.Pen.CurrentValue = pen;
            return e;
        }
        );
        yield return ("Clipping", () =>
        {
            var e = new Clipping();
            e.Left.CurrentValue = 24;
            e.Top.CurrentValue = 24;
            e.Right.CurrentValue = 24;
            e.Bottom.CurrentValue = 24;
            return e;
        }
        );
        yield return ("LayerEffect", () => new LayerEffect());
        yield return ("DelayAnimationEffect", () =>
        {
            var inner = new Blur();
            inner.Sigma.CurrentValue = new Size(4, 4);
            var e = new DelayAnimationEffect();
            e.Delay.CurrentValue = 0.5f;
            e.Effect.CurrentValue = inner;
            return e;
        }
        );
        yield return ("ShakeEffect", () =>
        {
            var e = new ShakeEffect();
            // ShakeEffect derives its shake offset from Id.GetHashCode(); pin Id so the render is reproducible.
            e.Id = new Guid("00000000-0000-0000-0000-000000000004");
            e.StrengthX.CurrentValue = 10;
            e.StrengthY.CurrentValue = 10;
            e.Speed.CurrentValue = 2;
            return e;
        }
        );
        yield return ("PathFollowEffect", () =>
        {
            var e = new PathFollowEffect();
            e.Geometry.CurrentValue = PathGeometry.Parse("M0,0 L70,50");
            e.Progress.CurrentValue = 100;
            return e;
        }
        );
        yield return ("BlendEffect", () =>
        {
            var e = new BlendEffect();
            e.Brush.CurrentValue = Brushes.Red;
            e.BlendMode.CurrentValue = BlendMode.Multiply;
            return e;
        }
        );
    }

    private static IEnumerable<(string, Func<FilterEffect>)> SplitEffects()
    {
        yield return ("SplitEffect", () =>
        {
            var e = new SplitEffect();
            e.HorizontalDivisions.CurrentValue = 3;
            e.VerticalDivisions.CurrentValue = 3;
            e.HorizontalSpacing.CurrentValue = 12;
            e.VerticalSpacing.CurrentValue = 12;
            return e;
        }
        );
        yield return ("PartsSplitEffect", () => new PartsSplitEffect());
    }

    private static IEnumerable<(string, Func<FilterEffect>)> MetaEffects()
    {
        yield return ("FilterEffectGroup", () =>
        {
            var group = new FilterEffectGroup();
            var gamma = new Gamma();
            gamma.Amount.CurrentValue = 1.4f;
            group.Children.Add(gamma);
            group.Children.Add(new Invert());
            return group;
        }
        );
        yield return ("FilterEffectPresenter", () =>
        {
            var target = new Gamma();
            target.Amount.CurrentValue = 1.6f;
            var e = new FilterEffectPresenter();
            e.Target.CurrentValue = target;
            return e;
        }
        );
    }

    // FallbackFilterEffect is the placeholder for a filter-effect type that failed to load. It has no legacy golden
    // because legacy ApplyTo threw, but its redesigned identity-graph behavior is executable without a GPU.
    [Test]
    public void FallbackFilterEffect_DescribeAndExecuteAsIdentity()
    {
        var bounds = new Rect(0, 0, 64, 48);
        var effect = new FallbackFilterEffect();
        var resource = (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f);
        effect.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            bounds, static _ => { }, hitTest: bounds.Contains);

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(graph.Nodes, Is.Empty, "fallback Describe appends no work");
                Assert.That(plan.Passes, Is.Empty, "the identity graph compiles to no passes");
                Assert.That(outputs, Has.Length.EqualTo(1));
                Assert.That(outputs[0], Is.SameAs(input), "execution preserves the original operation");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }
}

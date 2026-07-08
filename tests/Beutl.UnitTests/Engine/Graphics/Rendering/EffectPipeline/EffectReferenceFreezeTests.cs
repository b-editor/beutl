using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
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
/// Freezes the pre-redesign reference renders (T004) that every 004 parity gate compares against.
/// Each case renders at output scale 1.0 at <see cref="SceneFixtures.ReferenceSize"/>; when the frozen
/// reference is missing it is written, and when present the fresh render is asserted against it within
/// the golden thresholds (SSIM ≥ 0.99 / MAE ≤ 0.02, linear RGBA16F). Run this on a Vulkan-capable machine
/// once to produce and commit the references under <c>Golden/References/004-baseline/</c>.
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

    /// <summary>The 42-effect census (research §0). <c>RequiresCompute</c> marks Vulkan-compute-only effects.</summary>
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
        // CSharpScriptEffect's imperative FilterEffectContext surface was removed (breaking,
        // contracts/breaking-changes.md); the pre-redesign `Context.Blur(...)` reference is intentionally
        // invalidated. The census case now exercises the migrated GeometrySession surface — its reference
        // re-freezes on the next Vulkan run.
        yield return Case("CSharpScriptEffect", () =>
        {
            var e = new CSharpScriptEffect();
            e.Script.CurrentValue =
                "var canvas = Session.OpenCanvas();\n"
                + "canvas.Clear();\n"
                + "using (canvas.PushOpacity(0.5f))\n"
                + "using (canvas.PushDeviceSpace())\n"
                + "    Session.Inputs[0].Draw(canvas, default);";
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
    public void Effect_FreezesOrMatchesReference(string name, Func<FilterEffect> makeEffect, bool requiresCompute)
    {
        Freeze($"effect-{name}", () => MakeShape(makeEffect), requiresCompute);
    }

    [TestCaseSource(nameof(ChainScenes))]
    public void Chain_FreezesOrMatchesReference(string name, Func<Drawable.Resource> makeScene)
    {
        Freeze($"chain-{name}", makeScene, requiresCompute: false);
    }

    // NodeGraphFilterEffect never flows through the shape FilterEffect path the same way; build the graph
    // explicitly (Input -> FilterEffectNode<Invert> -> Output) and drive it through a host shape.
    [Test]
    public void NodeGraphFilterEffect_FreezesOrMatchesReference()
    {
        Freeze("effect-NodeGraphFilterEffect", MakeNodeGraphHost, requiresCompute: false);
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

    private static void Freeze(string name, Func<Drawable.Resource> makeResource, bool requiresCompute)
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        if (requiresCompute && !context.Supports3DRendering)
        {
            Assert.Ignore($"{name}: requires a Vulkan compute-capable context (Supports3DRendering == false).");
        }

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap actual = GoldenImageHarness.RenderAtScale(makeResource(), SceneFixtures.ReferenceSize, 1f);
            GoldenReferenceStore.FreezeOrAssert(Category, name, actual);
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

    // FallbackFilterEffect is the placeholder for a filter-effect type that failed to load. Its legacy
    // ApplyTo throws unconditionally, so no pre-redesign reference render exists to freeze; the redesign's
    // identity-graph behavior (research D7) is a post-migration change verified by the migration parity task.
    [Test]
    public void FallbackFilterEffect_HasNoLegacyReference()
    {
        Assert.Ignore("FallbackFilterEffect.ApplyTo throws on the legacy pipeline; it has no freezable "
            + "pre-redesign reference. Its post-redesign identity behavior is covered by the migration parity gate.");
    }
}

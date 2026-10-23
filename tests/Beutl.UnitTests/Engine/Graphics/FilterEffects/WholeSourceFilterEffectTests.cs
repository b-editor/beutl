using System.Numerics;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class WholeSourceFilterEffectTests
{
    private static readonly Rect s_bounds = new(10, 20, 100, 60);

    [TestCase(0f, 4f, 0f, 0f, 1f, 1f)]
    [TestCase(16f, 4f, 0f, 0f, 1f, 0f)]
    [TestCase(-16f, 4f, 0f, 1f, 0f, 1f)]
    [TestCase(0f, 10000f, 0f, 0f, 0f, 0f)]
    [TestCase(0f, -10000f, 0f, 1f, 1f, 1f)]
    [TestCase(float.NaN, float.PositiveInfinity, float.NaN, 0f, 1f, 1f)]
    public void DiagonalClipping_SignedWidthControlsPixelsAndHits(
        float width, float position, float angle, float top, float middle, float bottom)
    {
        var effect = MakeDiagonalClipping(angle, position, width);
        (float[] before, float[] pixels) = FilterEffectTestHarness.RunOnSolidTarget(
            effect, new SKColor(230, 40, 20, 160), new PixelSize(64, 64));
        var bounds = new Rect(10.25f, 20.5f, 64, 64);
        (ShaderDescription description, Rect output) = FilterEffectTestHarness.RecordShader(effect, bounds);
        var input = new RenderHitTestInput(bounds, point => point.X >= bounds.Center.X);
        bool Hit(float x, float y) => description.HitTest!.Value.Evaluate(
            output, [input], [], new Point(bounds.X + x, bounds.Y + y));

        Assert.Multiple(() =>
        {
            Assert.That(output, Is.EqualTo(bounds), "clipping must not recenter the drawable");
            Assert.That(pixels.All(float.IsFinite), Is.True);
            foreach ((int y, float coverage) in new[] { (16, top), (36, middle), (52, bottom) })
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    int index = (y * 64 + 40) * 4 + channel;
                    Assert.That(pixels[index], Is.EqualTo(before[index] * coverage));
                }
                Assert.That(Hit(40, y), Is.EqualTo(coverage != 0));
            }
            Assert.That(Hit(16, 36), Is.False, "transparent input must not become selectable");
            Assert.That(Hit(80, 36), Is.False, "the mask must not hit beyond its output");
        });
        (_, float[] transparent) = FilterEffectTestHarness.RunOnSolidTarget(effect, SKColors.Transparent, new PixelSize(64, 64));
        Assert.That(transparent, Is.All.Zero);
    }

    [TestCase(0f, 16, 48, 48, 16)]
    [TestCase(90f, 16, 48, 48, 16)]
    [TestCase(180f, 48, 16, 16, 48)]
    [TestCase(270f, 48, 16, 16, 48)]
    [TestCase(45f, 16, 48, 48, 16)]
    [TestCase(405f, 16, 48, 48, 16)]
    [TestCase(-45f, 48, 48, 16, 16)]
    public void DiagonalClipping_RotatesTheBoundary(float angle, int keptX, int keptY, int cutX, int cutY)
    {
        float[] pixels = RenderDiagonalClipping(MakeDiagonalClipping(angle));
        Assert.Multiple(() =>
        {
            Assert.That(ClipAlpha(pixels, keptX, keptY), Is.EqualTo(1f));
            Assert.That(ClipAlpha(pixels, cutX, cutY), Is.Zero);
        });
    }

    [TestCase(45f, 0f, 0f, 0.5f, 0f, 1f)]
    [TestCase(0f, 0.5f, 0.25f, 0.25f, 0f, 0f)]
    [TestCase(0f, 0.5f, -0.25f, 0.75f, 1f, 1f)]
    public void DiagonalClipping_AntialiasesEdgesAndSubpixelBands(
        float angle, float position, float width, float center, float top, float bottom)
    {
        float[] pixels = RenderDiagonalClipping(MakeDiagonalClipping(angle, position, width));
        Assert.Multiple(() =>
        {
            Assert.That(ClipAlpha(pixels, 32, 32), Is.EqualTo(center).Within(0.001f));
            Assert.That(ClipAlpha(pixels, 32, 31), Is.EqualTo(top).Within(0.001f));
            Assert.That(ClipAlpha(pixels, 32, 33), Is.EqualTo(bottom).Within(0.001f));
            if (width == 0)
                Assert.That(pixels.Where((_, index) => index % 4 == 3).Sum(), Is.EqualTo(2048f).Within(0.1f));
        });
    }

    [Test]
    public void DiagonalClipping_OppositeSignsReconstructThePremultipliedSource()
    {
        var color = new SKColor(230, 40, 20, 160);
        (float[] before, float[] inside) = FilterEffectTestHarness.RunOnSolidTarget(
            MakeDiagonalClipping(31, 1.35f, 7.5f), color, new PixelSize(64, 64));
        (_, float[] outside) = FilterEffectTestHarness.RunOnSolidTarget(
            MakeDiagonalClipping(31, 1.35f, -7.5f), color, new PixelSize(64, 64));
        for (int index = 0; index < before.Length; index++)
            Assert.That(inside[index] + outside[index], Is.EqualTo(before[index]).Within(0.001f), $"premultiplied channel {index}");
    }

    [TestCase(0, 16f)]
    [TestCase(1, 0f)]
    [TestCase(2, -16f)]
    public void DiagonalClipping_SerializationPreservesWidthAnimationAcrossZero(int seconds, float expectedWidth)
    {
        var effect = MakeDiagonalClipping(30, 4, -20);
        effect.Width.SetKeyFrameAnimation(new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 16 } },
                new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = -16 } },
            },
        });
        var json = CoreSerializer.SerializeToJsonObject(effect);
        var restored = (DiagonalClipping)CoreSerializer.DeserializeFromJsonObject(json, typeof(DiagonalClipping));
        using DiagonalClipping.Resource resource = restored.ToResource(new CompositionContext(MediaTime.FromSeconds(seconds)));
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(restored, resource);
        ShaderDescription description = ((FEItem_Shader)context.GetOrderedItems().Single()).Description;
        Assert.Multiple(() =>
        {
            Assert.That(json.ContainsKey("Mode"), Is.False);
            Assert.That(restored.Angle.Value, Is.EqualTo(30));
            Assert.That(restored.Position.Value, Is.EqualTo(4));
            Assert.That(restored.Width.Value, Is.EqualTo(-20));
            Assert.That(resource.Width, Is.EqualTo(expectedWidth));
            Assert.That(FilterEffectTestHarness.BindUniform(description, "width", s_bounds).Floats![0], Is.EqualTo(expectedWidth));
        });
    }

    private static DiagonalClipping MakeDiagonalClipping(float angle = 45, float position = 0, float width = 0) => new()
    {
        Angle = { Value = angle },
        Position = { Value = position },
        Width = { Value = width },
    };

    private static float[] RenderDiagonalClipping(DiagonalClipping effect)
        => FilterEffectTestHarness.RunOnSolidTarget(effect, SKColors.White, new PixelSize(64, 64)).After;

    private static float ClipAlpha(float[] pixels, int x, int y) => pixels[(y * 64 + x) * 4 + 3];

    [TestCaseSource(nameof(MigratedEffects))]
    public void MigratedEffects_RecordWholeSourceWithoutEffectItemBoundary(
        Func<FilterEffect> factory,
        SKShaderTileMode expectedTileMode,
        bool expectedFullInput,
        int expectedResourceCount)
    {
        (ShaderDescription description, Rect bounds) = FilterEffectTestHarness.RecordShader(factory(), s_bounds);
        Assert.Multiple(() =>
        {
            Assert.That(bounds, Is.EqualTo(s_bounds));
            Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(description.SourceTileMode, Is.EqualTo(expectedTileMode));
            Assert.That(description.Bounds.RequiresFullInput, Is.EqualTo(expectedFullInput));
            Assert.That(description.Resources, Has.Count.EqualTo(expectedResourceCount));
        });
    }

    [TestCaseSource(nameof(SolidColorEffects))]
    public void MigratedEffects_PreservePremultipliedSolidColorOutput(Func<FilterEffect> factory)
    {
        FilterEffect effect = factory();
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnSolidTarget(
            effect, new SKColor(230, 40, 20, 180), new PixelSize(3, 2));
        Assert.That(after, Has.Length.EqualTo(before.Length));
        for (int index = 0; index < before.Length; index++)
        {
            Assert.That(
                after[index],
                Is.EqualTo(before[index]).Within(0.003f),
                $"{effect.GetType().Name} changed solid-color output channel {index}");
        }
    }

    [Test]
    public void ColorShift_RecordsForwardAndBackwardOffsetBounds()
    {
        var effect = new ColorShift
        {
            RedOffset = { Value = new PixelPoint(4, 2) },
            GreenOffset = { Value = new PixelPoint(1, 0) },
        };
        (ShaderDescription description, Rect bounds) = FilterEffectTestHarness.RecordShader(effect, s_bounds);
        Assert.Multiple(() =>
        {
            Assert.That(bounds, Is.EqualTo(new Rect(10, 20, 104, 62)));
            Assert.That(
                description.Bounds.GetRequiredInputBounds(s_bounds),
                Is.EqualTo(new Rect(6, 18, 104, 62)));
        });
    }

    [Test]
    public void ColorShift_AnimatedOffsets_KeepTheRecordedShaderStructure()
    {
        var first = new ColorShift
        {
            RedOffset = { Value = new PixelPoint(4, 2) },
            GreenOffset = { Value = new PixelPoint(1, 0) },
        };
        var second = new ColorShift
        {
            RedOffset = { Value = new PixelPoint(-3, 5) },
            GreenOffset = { Value = new PixelPoint(0, -2) },
        };

        ShaderDescription firstDescription = FilterEffectTestHarness.RecordShader(first, s_bounds).Description;
        ShaderDescription secondDescription = FilterEffectTestHarness.RecordShader(second, s_bounds).Description;

        Assert.Multiple(() =>
        {
            Assert.That(
                secondDescription.Bounds.StructuralIdentity,
                Is.EqualTo(firstDescription.Bounds.StructuralIdentity),
                "animated offset values must not create a new bounds-contract shape");
            Assert.That(
                secondDescription.StructuralIdentity,
                Is.EqualTo(firstDescription.StructuralIdentity),
                "animated offset values must reuse the same shader structure");
        });
    }

    [Test]
    public void Mosaic_RelativeOrigin_UsesCompleteCanonicalDeviceFootprint()
    {
        var outputBounds = new Rect(0.25f, 0.25f, 100, 80);
        var requestedRegion = new Rect(20, 10, 30, 20);
        using FilterEffect.Resource resource = new MosaicEffect().ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(outputBounds);
        context.ApplyTransactional(resource.RequireOriginal(), resource);
        ShaderDescription description = ((FEItem_Shader)context.GetOrderedItems().Single()).Description;
        ShaderUniformBinding binding = description.Uniforms.Single(static item => item.Name == "origin");
        SkslUniformDeclaration declaration = description.Source.Uniforms["origin"];
        PixelRect requestedDeviceBounds = PixelRect.FromRect(requestedRegion, 1);
        PixelRect completeDeviceBounds = PixelRect.FromRect(outputBounds, 1);
        var token = new RenderExecutionSessionToken();

        Vector2 origin = token.RunAndComplete(() =>
        {
            var execution = new ShaderExecutionContext(
                token,
                outputBounds,
                outputBounds,
                requestedRegion,
                requestedDeviceBounds,
                requestedDeviceBounds.ToRect(1).Position,
                EffectiveScale.At(1),
                outputScale: 1,
                workingScale: 1,
                maxWorkingScale: 1,
                RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary);
            float[] values = binding.Bind(declaration, execution).Floats!;
            return new Vector2(values[0], values[1]);
        });

        Assert.That(
            origin,
            Is.EqualTo(new Vector2(
                completeDeviceBounds.X - requestedDeviceBounds.X + completeDeviceBounds.Width / 2f,
                completeDeviceBounds.Y - requestedDeviceBounds.Y + completeDeviceBounds.Height / 2f)));
    }

    [TestCase(GradientSpreadMethod.Pad, SKShaderTileMode.Clamp)]
    [TestCase(GradientSpreadMethod.Reflect, SKShaderTileMode.Mirror)]
    [TestCase(GradientSpreadMethod.Repeat, SKShaderTileMode.Repeat)]
    [TestCase(GradientSpreadMethod.Decal, SKShaderTileMode.Decal)]
    public void DisplacementMap_PreservesSourceTileMode(
        GradientSpreadMethod spreadMethod,
        SKShaderTileMode expected)
    {
        var effect = new DisplacementMapEffect
        {
            SpreadMethod = { Value = spreadMethod },
        };

        ShaderDescription description = FilterEffectTestHarness.RecordShader(effect, s_bounds).Description;
        Assert.That(description.SourceTileMode, Is.EqualTo(expected));
    }

    [Test]
    public void DisplacementMapPreview_RemainsEffectItemCustomBoundary()
    {
        var effect = new DisplacementMapEffect
        {
            ShowDisplacementMap = { Value = true },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);

        context.ApplyTransactional(effect, resource);

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items.OfType<IFEItem_Custom>(), Has.Exactly(1).Items);
            Assert.That(items.OfType<FEItem_Shader>(), Is.Empty);
        });
    }

    [Test]
    public void DisplacementMap_DirectCompatibilityExecution_CommitsAndReleasesBorrowedResource()
    {
        var effect = new DisplacementMapEffect();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        Brush.Resource displacementMap = ((DisplacementMapEffect.Resource)resource).DisplacementMap!;
        using var backing = CpuRenderTargets.Create(3, 2);
        backing.Value.Canvas.Clear(SKColors.Red);
        backing.Value.Canvas.Flush();
        using var targets = new EffectTargets
        {
            new EffectTarget(
                backing,
                new Rect(0, 0, 3, 2),
                EffectiveScale.At(1),
                new PixelRect(0, 0, 3, 2)),
        };
        var context = new FilterEffectContext(new Rect(0, 0, 3, 2));
        RenderResource? token = null;
        try
        {
            context.ApplyTransactional(effect, resource);
            token = ((FEItem_Shader)context.GetOrderedItems().Single())
                .Description.Resources.Single().Resource;
            Assert.That(token.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Pending));

            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(
                targets,
                builder,
                RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary,
                drawableBrushMaterializer: null,
                outputScale: 1,
                workingScale: 1,
                maxWorkingScale: 1);
            executor.Apply(context);
            Assert.That(token.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Committed));
            executor.Flush(false);

            using Bitmap bitmap = targets.Single().RenderTarget!.Snapshot();
            Assert.That(bitmap.SKBitmap.GetPixel(1, 1).Red, Is.GreaterThan(239));
        }
        finally
        {
            context.Dispose();
        }

        Assert.That(token, Is.Not.Null);
        Assert.That(token!.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Released));
        using SKShader? rebound = new BrushConstructor(
                new Rect(0, 0, 3, 2),
                displacementMap,
                BlendMode.SrcOver,
                RenderIntent.Preview,
                drawableBrushMaterializer: null,
                scale: 1,
                maxWorkingScale: 1)
            .CreateShader();
        Assert.That(rebound, Is.Not.Null);
    }

    [Test]
    public void DisplacementMapTargetTransaction_DrawFailureDisposesReplacementAndPreservesOriginalSlot()
    {
        using var originalBacking = CpuRenderTargets.Create(3, 2);
        using var replacementBacking = CpuRenderTargets.Create(3, 2);
        var original = new EffectTarget(
            originalBacking,
            new Rect(0, 0, 3, 2),
            EffectiveScale.At(1),
            new PixelRect(0, 0, 3, 2));
        var replacement = new EffectTarget(
            replacementBacking,
            new Rect(0, 0, 3, 2),
            EffectiveScale.At(1),
            new PixelRect(0, 0, 3, 2));
        using var targets = new EffectTargets { original };
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary);
        var failure = new InvalidOperationException("draw failed");

        InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(() =>
            DisplacementMapEffect.RenderAndCommitReplacement(
                context,
                0,
                original,
                replacement,
                failure,
                static current => throw current));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(replacement.IsEmpty, Is.True);
            Assert.That(targets[0], Is.SameAs(original));
            Assert.That(original.IsEmpty, Is.False);
        });
    }

    [Test]
    public void DisplacementMapTargetTransaction_DrawSuccessCommitsReplacementAndDisposesOriginal()
    {
        using var originalBacking = CpuRenderTargets.Create(3, 2);
        using var replacementBacking = CpuRenderTargets.Create(3, 2);
        var original = new EffectTarget(
            originalBacking,
            new Rect(0, 0, 3, 2),
            EffectiveScale.At(1),
            new PixelRect(0, 0, 3, 2));
        var replacement = new EffectTarget(
            replacementBacking,
            new Rect(0, 0, 3, 2),
            EffectiveScale.At(1),
            new PixelRect(0, 0, 3, 2));
        using var targets = new EffectTargets { original };
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary);

        DisplacementMapEffect.RenderAndCommitReplacement(
            context,
            0,
            original,
            replacement,
            false,
            static _ => { });

        Assert.Multiple(() =>
        {
            Assert.That(targets[0], Is.SameAs(replacement));
            Assert.That(original.IsEmpty, Is.True);
            Assert.That(replacement.IsEmpty, Is.False);
        });
    }

    private static IEnumerable<TestCaseData> MigratedEffects()
    {
        yield return new TestCaseData(
                (Func<FilterEffect>)(() => new MosaicEffect()),
                SKShaderTileMode.Clamp,
                true,
                0)
            .SetName("Mosaic_WholeSource");
        yield return new TestCaseData(
                (Func<FilterEffect>)(() => new ColorShift()),
                SKShaderTileMode.Decal,
                false,
                0)
            .SetName("ColorShift_WholeSource");
        yield return new TestCaseData(
                (Func<FilterEffect>)CreateDisplacementMap<DisplacementMapTranslateTransform>,
                SKShaderTileMode.Clamp,
                true,
                1)
            .SetName("DisplacementMapTranslate_WholeSource");
        yield return new TestCaseData(
                (Func<FilterEffect>)CreateDisplacementMap<DisplacementMapScaleTransform>,
                SKShaderTileMode.Clamp,
                true,
                1)
            .SetName("DisplacementMapScale_WholeSource");
        yield return new TestCaseData(
                (Func<FilterEffect>)CreateDisplacementMap<DisplacementMapRotationTransform>,
                SKShaderTileMode.Clamp,
                true,
                1)
            .SetName("DisplacementMapRotation_WholeSource");
    }

    private static IEnumerable<TestCaseData> SolidColorEffects()
    {
        yield return new TestCaseData((Func<FilterEffect>)(() => new MosaicEffect()))
            .SetName("Mosaic_SolidColorOutput");
        yield return new TestCaseData((Func<FilterEffect>)(() => new ColorShift()))
            .SetName("ColorShift_SolidColorOutput");
        yield return new TestCaseData(
                (Func<FilterEffect>)CreateDisplacementMap<DisplacementMapTranslateTransform>)
            .SetName("DisplacementMapTranslate_SolidColorOutput");
        yield return new TestCaseData(
                (Func<FilterEffect>)CreateDisplacementMap<DisplacementMapScaleTransform>)
            .SetName("DisplacementMapScale_SolidColorOutput");
        yield return new TestCaseData(
                (Func<FilterEffect>)CreateDisplacementMap<DisplacementMapRotationTransform>)
            .SetName("DisplacementMapRotation_SolidColorOutput");
    }

    private static FilterEffect CreateDisplacementMap<T>()
        where T : DisplacementMapTransform, new()
        => new DisplacementMapEffect
        {
            Transform = { Value = new T() },
        };
}

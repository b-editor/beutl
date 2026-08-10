using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class WholeSourceFilterEffectTests
{
    private const float UnallocatableScale = 2_000_000f;

    private static readonly Rect s_bounds = new(10, 20, 100, 60);

    [TestCaseSource(nameof(MigratedEffects))]
    public void MigratedEffects_RecordWholeSourceWithoutLegacyBoundary(
        Func<FilterEffect> factory,
        SKShaderTileMode expectedTileMode,
        bool expectedFullInput,
        int expectedResourceCount)
    {
        FilterEffect effect = factory();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);

        context.ApplyTransactional(effect, resource);

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items.OfType<IFEItem_Custom>(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
        });

        var item = (FEItem_Shader)items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.Description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(item.Description.SourceTileMode, Is.EqualTo(expectedTileMode));
            Assert.That(item.Description.Bounds.RequiresFullInput, Is.EqualTo(expectedFullInput));
            Assert.That(item.Description.Resources, Has.Count.EqualTo(expectedResourceCount));
        });
    }

    [TestCaseSource(nameof(SolidColorEffects))]
    public void MigratedEffects_PreservePremultipliedSolidColorOutput(Func<FilterEffect> factory)
    {
        FilterEffect effect = factory();
        using var backing = new CpuRenderTarget(3, 2);
        backing.Value.Canvas.Clear(new SKColor(230, 40, 20, 180));
        backing.Value.Canvas.Flush();
        using Bitmap beforeBitmap = backing.Snapshot();
        float[] before = ReadPixels(beforeBitmap);
        using var targets = new EffectTargets
        {
            new EffectTarget(
                backing,
                new Rect(0, 0, 3, 2),
                EffectiveScale.At(1),
                new PixelRect(0, 0, 3, 2)),
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 3, 2));
        context.ApplyTransactional(effect, resource);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        activator.Apply(context);
        activator.Flush(false);

        using Bitmap afterBitmap = targets.Single().RenderTarget!.Snapshot();
        float[] after = ReadPixels(afterBitmap);
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
            RedOffset = { CurrentValue = new PixelPoint(4, 2) },
            GreenOffset = { CurrentValue = new PixelPoint(1, 0) },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);

        context.ApplyTransactional(effect, resource);

        var item = (FEItem_Shader)context.GetOrderedItems().Single();
        Assert.Multiple(() =>
        {
            Assert.That(context.Bounds, Is.EqualTo(new Rect(10, 20, 104, 62)));
            Assert.That(
                item.Description.Bounds.GetRequiredInputBounds(s_bounds),
                Is.EqualTo(new Rect(6, 18, 104, 62)));
        });
    }

    [Test]
    public void ColorShift_AnimatedOffsets_KeepStructureAndChangeRuntimeIdentity()
    {
        var first = new ColorShift
        {
            RedOffset = { CurrentValue = new PixelPoint(4, 2) },
            GreenOffset = { CurrentValue = new PixelPoint(1, 0) },
        };
        var second = new ColorShift
        {
            RedOffset = { CurrentValue = new PixelPoint(-3, 5) },
            GreenOffset = { CurrentValue = new PixelPoint(0, -2) },
        };

        ShaderDescription firstDescription = Record(first);
        ShaderDescription secondDescription = Record(second);

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
            Assert.That(
                secondDescription.CreateRuntimeIdentity(),
                Is.Not.EqualTo(firstDescription.CreateRuntimeIdentity()),
                "offset values must remain part of the runtime cache identity");
        });

        static ShaderDescription Record(ColorShift effect)
        {
            using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
            using var context = new FilterEffectContext(s_bounds);
            context.ApplyTransactional(effect, resource);
            return ((FEItem_Shader)context.GetOrderedItems().Single()).Description;
        }
    }

    [Test]
    public void Mosaic_RelativeOrigin_UsesCompleteCanonicalDeviceFootprint()
    {
        var outputBounds = new Rect(0.25f, 0.25f, 100, 80);
        var requestedRegion = new Rect(20, 10, 30, 20);
        using FilterEffect.Resource resource = new MosaicEffect().ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(outputBounds);
        context.ApplyTransactional(resource.GetOriginal(), resource);
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
            SpreadMethod = { CurrentValue = spreadMethod },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);

        context.ApplyTransactional(effect, resource);

        var item = (FEItem_Shader)context.GetOrderedItems().Single();
        Assert.That(item.Description.SourceTileMode, Is.EqualTo(expected));
    }

    [Test]
    public void DisplacementMapPreview_RemainsLegacyCustomBoundary()
    {
        var effect = new DisplacementMapEffect
        {
            ShowDisplacementMap = { CurrentValue = true },
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
        using var backing = new CpuRenderTarget(3, 2);
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
            using var activator = new FilterEffectActivator(
                targets,
                builder,
                RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary,
                outputScale: 1,
                workingScale: 1,
                maxWorkingScale: 1);
            activator.Apply(context);
            Assert.That(token.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Committed));
            activator.Flush(false);

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
                scale: 1,
                maxWorkingScale: 1)
            .CreateShader();
        Assert.That(rebound, Is.Not.Null);
    }

    [Test]
    public void DisplacementMap_EmptyDrawableMap_UsesTransparentLegacyPreviewShader()
    {
        var effect = new DisplacementMapEffect
        {
            DisplacementMap = { CurrentValue = new DrawableBrush() },
            Transform =
            {
                CurrentValue = new DisplacementMapTranslateTransform
                {
                    X = { CurrentValue = 2 },
                },
            },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 3, 2));
        context.ApplyTransactional(effect, resource);

        RegisteredEffectBrush registration = context.RegisteredBrushes.Single();
        var brushes = new Dictionary<FilterEffectBrush, LoweredBrush>
        {
            [registration.Handle] = LoweredBrush.Empty,
        };
        using var backing = new CpuRenderTarget(3, 2);
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
        var customContext = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            brushes: brushes);
        IFEItem_Custom custom = context.GetOrderedItems().OfType<IFEItem_Custom>().Single();

        Assert.That(() => custom.Accepts(customContext), Throws.Nothing);

        using Bitmap bitmap = targets.Single().RenderTarget!.Snapshot();
        SKColor pixel = bitmap.SKBitmap.GetPixel(1, 1);
        Assert.Multiple(() =>
        {
            Assert.That(pixel.Red, Is.GreaterThan(239));
            Assert.That(pixel.Green, Is.LessThan(16));
            Assert.That(pixel.Blue, Is.LessThan(16));
            Assert.That(pixel.Alpha, Is.GreaterThan(239));
        });
    }

    [Test]
    public void DisplacementMap_EmptyDrawableMap_ShowMapPaintsTransparent()
    {
        var effect = new DisplacementMapEffect
        {
            DisplacementMap = { CurrentValue = new DrawableBrush() },
            ShowDisplacementMap = { CurrentValue = true },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 3, 2));
        context.ApplyTransactional(effect, resource);

        RegisteredEffectBrush registration = context.RegisteredBrushes.Single();
        var brushes = new Dictionary<FilterEffectBrush, LoweredBrush>
        {
            [registration.Handle] = LoweredBrush.Empty,
        };
        using var backing = new CpuRenderTarget(3, 2);
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
        var customContext = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            brushes: brushes);
        IFEItem_Custom custom = context.GetOrderedItems().OfType<IFEItem_Custom>().Single();

        Assert.That(() => custom.Accepts(customContext), Throws.Nothing);

        using Bitmap bitmap = targets.Single().RenderTarget!.Snapshot();
        SKColor pixel = bitmap.SKBitmap.GetPixel(1, 1);
        Assert.Multiple(() =>
        {
            Assert.That(pixel.Red, Is.Zero);
            Assert.That(pixel.Green, Is.Zero);
            Assert.That(pixel.Blue, Is.Zero);
            Assert.That(pixel.Alpha, Is.Zero);
        });
    }

    [Test]
    public void DisplacementMapTransparentFallback_DeliveryPropagatesDrawableBrushAllocationFailure()
    {
        using DrawableBrush.Resource brush = new DrawableBrush().ToResource(CompositionContext.Default);
        using SKShader tile = SKShader.CreateColor(SKColors.White);
        var handle = new FilterEffectBrush(brush, brush);
        var brushes = new Dictionary<FilterEffectBrush, LoweredBrush>
        {
            [handle] = new LoweredBrush(
                null,
                brush,
                new BrushTileContent(tile, new Rect(0, 0, 8, 8), EffectiveScale.At(1))),
        };
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 4,
            brushes: brushes);

        Assert.That(
            () => DisplacementMapShaderFactory.CreateOrTransparent(
                context,
                handle,
                new Rect(0, 0, 8, 8),
                UnallocatableScale).Dispose(),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartWith("Drawable-brush intermediate allocation failed"));
    }

    [Test]
    public void DisplacementMapTargetTransaction_DrawFailureDisposesReplacementAndPreservesOriginalSlot()
    {
        using var originalBacking = new CpuRenderTarget(3, 2);
        using var replacementBacking = new CpuRenderTarget(3, 2);
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
        using var originalBacking = new CpuRenderTarget(3, 2);
        using var replacementBacking = new CpuRenderTarget(3, 2);
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
            Transform = { CurrentValue = new T() },
        };

    private static float[] ReadPixels(Bitmap bitmap)
        => bitmap.GetPixelSpan<ushort>()
            .ToArray()
            .Select(static bits => (float)BitConverter.UInt16BitsToHalf(bits))
            .ToArray();

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
               ?? throw new InvalidOperationException("A CPU effect-test surface could not be created.");
    }
}

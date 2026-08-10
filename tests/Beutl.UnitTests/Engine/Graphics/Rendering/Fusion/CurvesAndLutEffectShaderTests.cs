using System.Numerics;
using System.Text;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
[NonParallelizable]
public sealed class CurvesAndLutEffectShaderTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    [Test]
    public void Curves_RecordsTypedResourcesAndUsesStandaloneBudgetFallback()
    {
        var effect = new Curves();
        CurveMap masterCurve = effect.MasterCurve.CurrentValue;
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        var context = new FilterEffectContext(s_bounds);
        RenderResource[] tokens = [];

        try
        {
            effect.ApplyTo(context, resource);

            FEItem_Shader item = AssertTypedShader(context);
            ShaderDescription description = item.Description;
            tokens = description.Resources.Select(static binding => binding.Resource).ToArray();
            IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
                [new SkslSnippetStage(description)],
                SkslBackendBudgetResolver.Portable);
            SkslMergedProgram program = programs.Single();

            Assert.Multiple(() =>
            {
                Assert.That(description.Resources, Has.Count.EqualTo(9));
                Assert.That(
                    description.Resources.Select(static binding => binding.CoordinateSpace),
                    Is.All.EqualTo(ShaderResourceCoordinateSpace.Value));
                Assert.That(
                    description.Resources.Select(static binding => binding.CachePolicy),
                    Is.All.EqualTo(ShaderBindingCachePolicy.ReuseFromSnapshot));
                Assert.That(description.Resources[0].Resource.CacheIdentity.Key, Is.SameAs(masterCurve));
                Assert.That(description.Resources[0].Resource.CacheIdentity.Version, Is.Zero);
                Assert.That(program.StageCount, Is.EqualTo(1));
                Assert.That(program.SamplerCount, Is.EqualTo(10));
                Assert.That(program.ChildCount, Is.EqualTo(10));
                Assert.That(program.RequiresStandaloneExecution, Is.True);
                Assert.That(
                    program.OverflowReasons,
                    Is.EqualTo(new[] { SkslBackendLimit.Samplers, SkslBackendLimit.Children }));
            });
        }
        finally
        {
            context.Dispose();
        }

        Assert.That(
            tokens.Select(static token => token.RegistrationState),
            Is.All.EqualTo(RenderResourceRegistrationState.Released));
        using SKShader rebound = masterCurve.ToShader();
        Assert.That(rebound.Handle, Is.Not.EqualTo(IntPtr.Zero));
    }

    [TestCase(CubeFileDimension.OneDimension)]
    [TestCase(CubeFileDimension.ThreeDimension)]
    public void LutEffect_RecordsTypedResourceAndPreservesBorrowedLifetime(
        CubeFileDimension dimension)
    {
        CubeSource source = CreateRedToCyanLutSource(dimension);
        var effect = new LutEffect
        {
            Source = { CurrentValue = source },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        var lutResource = (LutEffect.Resource)resource;
        CubeSource.Resource capturedSource = lutResource.Source!;
        CubeFile cube = capturedSource.Cube!;
        var context = new FilterEffectContext(s_bounds);
        RenderResource? token = null;

        try
        {
            effect.ApplyTo(context, resource);

            FEItem_Shader item = AssertTypedShader(context);
            ShaderDescription description = item.Description;
            ShaderResourceBinding binding = description.Resources.Single();
            token = binding.Resource;

            Assert.Multiple(() =>
            {
                Assert.That(description.Resources, Has.Count.EqualTo(1));
                Assert.That(description.Uniforms.Select(static uniform => uniform.Name),
                    Is.EqualTo(new[] { "lutSize", "strength" }));
                Assert.That(binding.Name, Is.EqualTo("lut"));
                Assert.That(binding.CoordinateSpace, Is.EqualTo(ShaderResourceCoordinateSpace.Value));
                Assert.That(binding.CachePolicy, Is.EqualTo(ShaderBindingCachePolicy.ReuseFromSnapshot));
                Assert.That(binding.Resource.CacheIdentity.Key, Is.Not.EqualTo(source.Id));
                Assert.That(binding.Resource.CacheIdentity.Version, Is.Zero);
                Assert.That(lutResource.Strength, Is.EqualTo(100f));
                Assert.That(lutResource.IsEnabled, Is.True);
                Assert.That(cube.Dimention, Is.EqualTo(dimension));
                if (dimension == CubeFileDimension.OneDimension)
                    Assert.That(cube.Data[0], Is.Not.EqualTo(cube.Data[1]));
            });
        }
        finally
        {
            context.Dispose();
        }

        Assert.Multiple(() =>
        {
            Assert.That(token, Is.Not.Null);
            Assert.That(token!.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Released));
            Assert.That(capturedSource.Cube, Is.SameAs(cube));
            Assert.That(cube.Data, Is.Not.Empty);
        });
    }

    [TestCase(CubeFileDimension.OneDimension)]
    [TestCase(CubeFileDimension.ThreeDimension)]
    public void LutEffect_ReusesParsedSource(CubeFileDimension dimension)
    {
        SkslSource first = RecordLutSource(dimension);
        SkslSource second = RecordLutSource(dimension);

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void Curves_StandaloneCompatibilityExecutionPreservesOutput()
    {
        var effect = new Curves
        {
            MasterCurve =
            {
                CurrentValue = new CurveMap(
                    [new CurveControlPoint(0, 1), new CurveControlPoint(1, 0)]),
            },
        };

        SKColor color = Render(effect);

        AssertCyan(color);
    }

    [TestCase(CubeFileDimension.OneDimension)]
    [TestCase(CubeFileDimension.ThreeDimension)]
    public void LutEffect_CurrentPixelExecutionPreservesOutput(CubeFileDimension dimension)
    {
        var effect = new LutEffect
        {
            Source = { CurrentValue = CreateRedToCyanLutSource(dimension) },
        };

        SKColor color = Render(effect, expectedShaderStages: 1);

        AssertCyan(color);
    }

    [TestCase(CubeFileDimension.OneDimension)]
    [TestCase(CubeFileDimension.ThreeDimension)]
    public void LutEffect_InPlaceCubeDataMutationInvalidatesCachedPixels(CubeFileDimension dimension)
    {
        var effect = new LutEffect
        {
            Source = { CurrentValue = CreateRedToCyanLutSource(dimension) },
        };
        var effectResource = (LutEffect.Resource)effect.ToResource(CompositionContext.Default);
        CubeFile cube = effectResource.Source!.Cube!;
        using var root = new FilterEffectRenderNode(effectResource);
        root.AddChild(new RectangleRenderNode(
            s_bounds,
            Brushes.Resource.Red,
            null));
        root.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            AssertCyan(ReadCenterPixel(first));
        }

        for (int i = 0; i < cube.Data.Length; i++)
        {
            cube.Data[i] = Vector3.One - cube.Data[i];
        }

        using RenderNodeRasterization second = renderer.Rasterize();
        SKColor secondColor = ReadCenterPixel(second);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.Zero);
            Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.EqualTo(1));
            Assert.That(secondColor.Red, Is.GreaterThan(239));
            Assert.That(secondColor.Green, Is.LessThan(16));
            Assert.That(secondColor.Blue, Is.LessThan(16));
            Assert.That(secondColor.Alpha, Is.GreaterThan(239));
        });

        using RenderNodeRasterization third = renderer.Rasterize();
        SKColor thirdColor = ReadCenterPixel(third);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
            Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.Zero);
            Assert.That(thirdColor, Is.EqualTo(secondColor));
        });
    }

    [TestCase(CubeFileDimension.OneDimension)]
    [TestCase(CubeFileDimension.ThreeDimension)]
    public void LutEffect_RecordingReusesSnapshotGenerationByBitwiseContent(CubeFileDimension dimension)
    {
        var effect = new LutEffect
        {
            Source = { CurrentValue = CreateRedToCyanLutSource(dimension) },
        };
        using var effectResource = (LutEffect.Resource)effect.ToResource(CompositionContext.Default);
        CubeFile cube = effectResource.Source!.Cube!;

        (object CacheKey, object Snapshot) first = RecordLutBinding(effect, effectResource);
        (object CacheKey, object Snapshot) second = RecordLutBinding(effect, effectResource);

        Vector3 changed = cube.Data[^1];
        Assert.That(BitConverter.SingleToInt32Bits(changed.X), Is.Zero);
        changed.X = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        cube.Data[^1] = changed;
        (object CacheKey, object Snapshot) negativeZero = RecordLutBinding(effect, effectResource);
        (object CacheKey, object Snapshot) negativeZeroAgain = RecordLutBinding(effect, effectResource);

        changed.X = BitConverter.Int32BitsToSingle(0x7fc00001);
        cube.Data[^1] = changed;
        (object CacheKey, object Snapshot) firstNaN = RecordLutBinding(effect, effectResource);

        changed.X = BitConverter.Int32BitsToSingle(0x7fc00002);
        cube.Data[^1] = changed;
        (object CacheKey, object Snapshot) secondNaN = RecordLutBinding(effect, effectResource);
        (object CacheKey, object Snapshot) secondNaNAgain = RecordLutBinding(effect, effectResource);

        Assert.Multiple(() =>
        {
            Assert.That(second.CacheKey, Is.SameAs(first.CacheKey));
            Assert.That(second.Snapshot, Is.SameAs(first.Snapshot));
            Assert.That(negativeZero.CacheKey, Is.Not.SameAs(first.CacheKey));
            Assert.That(negativeZero.Snapshot, Is.Not.SameAs(first.Snapshot));
            Assert.That(negativeZeroAgain.CacheKey, Is.SameAs(negativeZero.CacheKey));
            Assert.That(negativeZeroAgain.Snapshot, Is.SameAs(negativeZero.Snapshot));
            Assert.That(secondNaN.CacheKey, Is.Not.SameAs(firstNaN.CacheKey));
            Assert.That(secondNaN.Snapshot, Is.Not.SameAs(firstNaN.Snapshot));
            Assert.That(secondNaNAgain.CacheKey, Is.SameAs(secondNaN.CacheKey));
            Assert.That(secondNaNAgain.Snapshot, Is.SameAs(secondNaN.Snapshot));
        });
    }

    [TestCase(CubeFileDimension.OneDimension)]
    [TestCase(CubeFileDimension.ThreeDimension)]
    public void LutEffect_DeferredBindingUsesTheRecordedCubeSnapshot(CubeFileDimension dimension)
    {
        var effect = new LutEffect
        {
            Source = { CurrentValue = CreateRedToCyanLutSource(dimension) },
        };
        using var effectResource = (LutEffect.Resource)effect.ToResource(CompositionContext.Default);
        CubeFile cube = effectResource.Source!.Cube!;
        using var firstContext = new FilterEffectContext(s_bounds);
        effect.ApplyTo(firstContext, effectResource);

        for (int i = 0; i < cube.Data.Length; i++)
        {
            cube.Data[i] = Vector3.One - cube.Data[i];
        }

        using var secondContext = new FilterEffectContext(s_bounds);
        effect.ApplyTo(secondContext, effectResource);

        SKColor first = ExecuteRecordedLut(firstContext);
        SKColor second = ExecuteRecordedLut(secondContext);

        AssertCyan(first);
        AssertRed(second);
    }

    [TestCaseSource(nameof(ResourceBackedEffects))]
    public void ResourceBackedCurrentPixelEffects_DirectCompatibilityExecution_CommitsAndReleasesResources(
        Func<FilterEffect> factory)
    {
        FilterEffect effect = factory();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using RenderTarget backing = new CpuTargetFactory().CreateCpuTarget(new PixelSize(1, 1));
        backing.Value.Canvas.Clear(SKColors.Red);
        backing.Value.Canvas.Flush();
        using var targets = new EffectTargets
        {
            new EffectTarget(
                backing,
                new Rect(0, 0, 1, 1),
                EffectiveScale.At(1),
                new PixelRect(0, 0, 1, 1)),
        };
        var context = new FilterEffectContext(new Rect(0, 0, 1, 1));
        RenderResource[] tokens = [];
        try
        {
            context.ApplyTransactional(effect, resource);
            tokens = ((FEItem_Shader)context.GetOrderedItems().Single())
                .Description.Resources.Select(static binding => binding.Resource).ToArray();
            Assert.That(
                tokens.Select(static token => token.RegistrationState),
                Is.All.EqualTo(RenderResourceRegistrationState.Pending));

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
            Assert.That(
                tokens.Select(static token => token.RegistrationState),
                Is.All.EqualTo(RenderResourceRegistrationState.Committed));
            activator.Flush(false);

            using Bitmap bitmap = targets.Single().RenderTarget!.Snapshot();
            AssertCyan(bitmap.SKBitmap.GetPixel(0, 0));
        }
        finally
        {
            context.Dispose();
        }

        Assert.That(
            tokens.Select(static token => token.RegistrationState),
            Is.All.EqualTo(RenderResourceRegistrationState.Released));
    }

    private static IEnumerable<TestCaseData> ResourceBackedEffects()
    {
        yield return new TestCaseData(
                (Func<FilterEffect>)(() => new Curves
                {
                    MasterCurve =
                    {
                        CurrentValue = new CurveMap(
                            [new CurveControlPoint(0, 1), new CurveControlPoint(1, 0)]),
                    },
                }))
            .SetName("Curves_DirectCompatibilityResourceLifecycle");
        yield return new TestCaseData(
                (Func<FilterEffect>)(() => new LutEffect
                {
                    Source =
                    {
                        CurrentValue = CreateRedToCyanLutSource(CubeFileDimension.OneDimension),
                    },
                }))
            .SetName("LutEffect1D_DirectCompatibilityResourceLifecycle");
        yield return new TestCaseData(
                (Func<FilterEffect>)(() => new LutEffect
                {
                    Source =
                    {
                        CurrentValue = CreateRedToCyanLutSource(CubeFileDimension.ThreeDimension),
                    },
                }))
            .SetName("LutEffect3D_DirectCompatibilityResourceLifecycle");
    }

    private static FEItem_Shader AssertTypedShader(FilterEffectContext context)
    {
        IFEItem item = context.GetOrderedItems().Single();
        Assert.That(item, Is.TypeOf<FEItem_Shader>());
        var shader = (FEItem_Shader)item;
        Assert.That(shader.Description.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
        return shader;
    }

    private static SkslSource RecordLutSource(CubeFileDimension dimension)
    {
        var effect = new LutEffect
        {
            Source = { CurrentValue = CreateRedToCyanLutSource(dimension) },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);

        effect.ApplyTo(context, resource);

        return AssertTypedShader(context).Description.Source;
    }

    private static (object CacheKey, object Snapshot) RecordLutBinding(
        LutEffect effect,
        LutEffect.Resource resource)
    {
        using var context = new FilterEffectContext(s_bounds);
        effect.ApplyTo(context, resource);
        ShaderResourceBinding binding = AssertTypedShader(context).Description.Resources.Single();
        return (binding.Resource.CacheIdentity.Key, binding.Resource.Slot.RawValue);
    }

    private static SKColor ExecuteRecordedLut(FilterEffectContext context)
    {
        using RenderTarget backing = new CpuTargetFactory().CreateCpuTarget(
            new PixelSize((int)s_bounds.Width, (int)s_bounds.Height));
        backing.Value.Canvas.Clear(SKColors.Red);
        backing.Value.Canvas.Flush();
        using var targets = new EffectTargets
        {
            new EffectTarget(
                backing,
                s_bounds,
                EffectiveScale.At(1),
                PixelRect.FromRect(s_bounds, 1)),
        };
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

        using Bitmap bitmap = targets.Single().RenderTarget!.Snapshot();
        return bitmap.SKBitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }

    private static SKColor Render(FilterEffect effect, int? expectedShaderStages = null)
    {
        using var root = new FilterEffectRenderNode(
            effect.ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(
            s_bounds,
            Brushes.Resource.Red,
            null));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        if (expectedShaderStages is int expected)
        {
            Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.EqualTo(expected));
        }
        Bitmap? bitmap = rasterization.Bitmap;
        Assert.That(bitmap, Is.Not.Null);
        return bitmap!.SKBitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }

    private static SKColor ReadCenterPixel(RenderNodeRasterization rasterization)
    {
        Bitmap? bitmap = rasterization.Bitmap;
        Assert.That(bitmap, Is.Not.Null);
        return bitmap!.SKBitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }

    private static void AssertCyan(SKColor color)
    {
        Assert.Multiple(() =>
        {
            Assert.That(color.Red, Is.LessThan(16));
            Assert.That(color.Green, Is.GreaterThan(239));
            Assert.That(color.Blue, Is.GreaterThan(239));
            Assert.That(color.Alpha, Is.GreaterThan(239));
        });
    }

    private static void AssertRed(SKColor color)
    {
        Assert.Multiple(() =>
        {
            Assert.That(color.Red, Is.GreaterThan(239));
            Assert.That(color.Green, Is.LessThan(16));
            Assert.That(color.Blue, Is.LessThan(16));
            Assert.That(color.Alpha, Is.GreaterThan(239));
        });
    }

    private static CubeSource CreateRedToCyanLutSource(CubeFileDimension dimension)
    {
        string cubeText = dimension switch
        {
            CubeFileDimension.OneDimension =>
                """
                TITLE "invert-1d"
                LUT_1D_SIZE 2
                DOMAIN_MIN 0 0 0
                DOMAIN_MAX 1 1 1
                1 1 1
                0 0 0
                """,
            CubeFileDimension.ThreeDimension =>
                """
                TITLE "invert-3d"
                LUT_3D_SIZE 2
                DOMAIN_MIN 0 0 0
                DOMAIN_MAX 1 1 1
                1 1 1
                0 1 1
                1 0 1
                0 0 1
                1 1 0
                0 1 0
                1 0 0
                0 0 0
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        };
        var source = new CubeSource();
        source.ReadFrom(new Uri(
            "data:text/plain;base64,"
            + Convert.ToBase64String(Encoding.ASCII.GetBytes(cubeText + "\n"))));
        return source;
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation)
            => RenderScaleUtilities.MaxBufferDimension;

        public RenderTarget CreateCpuTarget(PixelSize deviceSize)
            => CreateCore(deviceSize);

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => CreateCore(allocation.DeviceSize);

        private static RenderTarget CreateCore(PixelSize deviceSize)
        {
            SKSurface surface = SKSurface.Create(new SKImageInfo(
                    deviceSize.Width,
                    deviceSize.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create the CPU filter-effect test surface.");
            return new CpuRenderTarget(surface, deviceSize);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}

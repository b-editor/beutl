using System.Text;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
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
                    description.Resources.Select(static binding => binding.RuntimeIdentity),
                    Has.All.Not.Null);
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
        long sourceVersion = capturedSource.Version;
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
                Assert.That(binding.RuntimeIdentity, Is.Not.Null);
                Assert.That(binding.Resource.CacheIdentity.Key, Is.EqualTo(source.Id));
                Assert.That(binding.Resource.CacheIdentity.Version, Is.EqualTo(sourceVersion));
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
                    UseRenderCache = false,
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
            + Convert.ToBase64String(Encoding.ASCII.GetBytes(cubeText))));
        return source;
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
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

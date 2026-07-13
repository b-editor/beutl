using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

[NonParallelizable]
[TestFixture]
public class PrimitiveRuntimeContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    private const string CopyShader = """
        #version 450
        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(push_constant) uniform PC { float dummy; } pc;
        void main() { outColor = texture(srcTexture, fragCoord); }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public float Dummy;
    }

    [TestCase(1, 0)]
    [TestCase(1, 2)]
    public void ComputeDispatchCount_MustExactlyMatchDeclaration(int declared, int actual)
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute pass-count contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var shader = GLSLShader.Create(CopyShader);
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                ctx =>
                {
                    ITexture2D depth = ctx.AcquireDepthScratch();
                    for (int i = 0; i < actual; i++)
                    {
                        ctx.Run(shader, ctx.Source, ctx.Destination, depth, new PushConstants());
                    }
                },
                declared,
                ComputeFallback.Identity,
                depthScratchCount: 1,
                structuralToken: $"dispatch-count-{declared}-{actual}");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f).Compute(descriptor));
            using var pool = new RenderTargetPool();

            InvalidOperationException error = Assert.Catch<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool))!;
            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.Contain("dispatch"));
                Assert.That(error.Message, Does.Contain(declared.ToString()));
                Assert.That(pool.LiveLeaseCount, Is.Zero);
            });
        });
    }

    [Test]
    public void ComputeTerminalCopy_CompletesWithoutDeclaredDispatches()
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute terminal-copy contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                static ctx => ctx.CopySourceToDestination(),
                passCount: 3,
                ComputeFallback.Identity,
                structuralToken: "terminal-copy-success");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f).Compute(descriptor));
            using var pool = new RenderTargetPool();

            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool);
            RenderNodeOperation.DisposeAll(outputs);

            Assert.That(pool.LiveLeaseCount, Is.Zero);
        });
    }

    [TestCase("copy-twice")]
    [TestCase("scratch-after-copy")]
    [TestCase("run-after-copy")]
    public void ComputeTerminalCopy_RejectsFurtherWork(string violation)
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute terminal-copy contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var shader = GLSLShader.Create(CopyShader);
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                ctx =>
                {
                    ITexture2D? depth = violation == "run-after-copy" ? ctx.AcquireDepthScratch() : null;
                    ctx.CopySourceToDestination();
                    switch (violation)
                    {
                        case "copy-twice":
                            ctx.CopySourceToDestination();
                            break;
                        case "scratch-after-copy":
                            ctx.AcquireColorScratch();
                            break;
                        case "run-after-copy":
                            ctx.Run(shader, ctx.Source, ctx.Destination, depth!, new PushConstants());
                            break;
                    }
                },
                passCount: 1,
                ComputeFallback.Identity,
                colorScratchCount: 1,
                depthScratchCount: 1,
                structuralToken: "terminal-copy-" + violation);
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f).Compute(descriptor));
            using var pool = new RenderTargetPool();

            InvalidOperationException error = Assert.Catch<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.Contain("terminal").IgnoreCase);
                Assert.That(pool.LiveLeaseCount, Is.Zero);
            });
        });
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public void StaticSplit_EmitCountMustExactlyMatchDeclaration(int declared, int actual)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            SplitNodeDescriptor descriptor = SplitNodeDescriptor.Static(
                emitter =>
                {
                    for (int i = 0; i < actual; i++)
                        emitter.Emit(s_bounds, static _ => { });
                },
                declared,
                structuralToken: $"split-count-{declared}-{actual}");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f).Split(descriptor));
            using var pool = new RenderTargetPool();

            InvalidOperationException error = Assert.Catch<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool))!;
            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.Contain("branch"));
                Assert.That(error.Message, Does.Contain(declared.ToString()));
                Assert.That(pool.LiveLeaseCount, Is.Zero);
            });
        });
    }

    [Test]
    public void ForcedIdentityFallback_BetweenSkiaPassesDoesNotCountSyncs()
    {
        ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
            static _ => throw new AssertionException("dispatch must not run"),
            1,
            ComputeFallback.Identity,
            structuralToken: "forced-identity");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f).Saturate(1.2f).Compute(descriptor).Brightness(1.1f));
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        RenderNodeOperation input = Input();

        PlanExecutor.ForceComputeFallbackForTests();
        try
        {
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics, pool);
            try
            {
                Assert.That(outputs, Has.Length.EqualTo(1));
                Assert.That(diagnostics.Snapshot().FlushSyncs, Is.Zero,
                    "a no-Vulkan identity pass performs no backend transition");
            }
            finally
            {
                RenderNodeOperation.DisposeAll(outputs);
            }
        }
        finally
        {
            PlanExecutor.ResetComputeFallbackForTests();
        }
    }

    [Test]
    public void ForcedIdentityFallback_ReturnsSameOperation()
    {
        ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
            static _ => throw new AssertionException("dispatch must not run"),
            1,
            ComputeFallback.Identity,
            structuralToken: "forced-identity-operation");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f).Compute(descriptor));
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        RenderNodeOperation input = Input();

        PlanExecutor.ForceComputeFallbackForTests();
        try
        {
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics, pool);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(outputs, Has.Length.EqualTo(1));
                    Assert.That(outputs[0], Is.SameAs(input));
                    Assert.That(diagnostics.Snapshot().FlushSyncs, Is.Zero);
                });
            }
            finally
            {
                RenderNodeOperation.DisposeAll(outputs);
            }
        }
        finally
        {
            PlanExecutor.ResetComputeFallbackForTests();
        }
    }

    [Test]
    public void ForcedSkipFallback_DropsAndDisposesInputWithoutSyncs()
    {
        ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
            static _ => throw new AssertionException("dispatch must not run"),
            1,
            ComputeFallback.Skip,
            structuralToken: "forced-skip");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f).Compute(descriptor).Brightness(1.1f));
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        bool disposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds, static _ => { }, onDispose: () => disposed = true);

        PlanExecutor.ForceComputeFallbackForTests();
        try
        {
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics, pool);
            Assert.Multiple(() =>
            {
                Assert.That(outputs, Is.Empty);
                Assert.That(disposed, Is.True);
                Assert.That(diagnostics.Snapshot().FlushSyncs, Is.Zero,
                    "a no-Vulkan skip performs no backend transition");
            });
        }
        finally
        {
            PlanExecutor.ResetComputeFallbackForTests();
        }
    }

    private static (CompiledPlan Plan, FrameResources Resources) Compile(EffectGraphBuilder builder)
    {
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        return (plan, EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, 1f));
    }

    private static RenderNodeOperation Input()
        => RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);
}

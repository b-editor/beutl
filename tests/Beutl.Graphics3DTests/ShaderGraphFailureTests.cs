using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphFailureTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void FailedGraphTargetAllocationReleasesLeasesAndRecovers(bool cancelled)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var factory = new FailingFactory(cancelled);
            var pool = new RenderTargetPool(factory);
            using ProgramCache<ShaderGraphPipeline> programs = ShaderGraphPipeline.CreateCache();
            var effect = (Bloom)ShaderGraphEffectTests.Create(false);
            using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
            using var brush = (Brush.Resource)new SolidColorBrush(Colors.White).ToResource(CompositionContext.Default);
            using var root = new FilterEffectRenderNode(resource);
            root.AddChild(new RectangleRenderNode(new(4, 4, 16, 16), brush, null));
            try
            {
                if (cancelled)
                    Assert.That(Render, Throws.InstanceOf<OperationCanceledException>());
                else
                    Assert.That(Render, Throws.InstanceOf<InvalidOperationException>());
                Assert.That(factory.Attempts, Is.GreaterThanOrEqualTo(3), "Fail after the source and destination have been allocated.");
                Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
                factory.Fail = false;
                float[] recovered = Render();
                Assert.That(recovered, Has.Some.GreaterThan(1f), "The bloom must render after the failed request.");
                Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
                Assert.That(programs.Statistics.RetainedPrograms, Is.GreaterThan(0));
            }
            finally
            {
                pool.Dispose();
                programs.Dispose();
            }
            Assert.That(factory.Targets, Has.All.Matches<RenderTarget>(target => target is { IsDisposed: true }));
            Assert.That(pool.Statistics.OwnedTargets, Is.Zero);
            Assert.That(programs.Statistics.RetainedPrograms, Is.Zero);

            float[] Render()
            {
                Rect bounds = new(0, 0, 24, 24);
                using CompiledRenderRequest request = Graphics3DTestHelpers.Compile(root, new RenderRequestOptions(
                    RenderIntent.Delivery, RenderRequestPurpose.Frame, targetDomain: bounds,
                    maxWorkingScale: 1, cachePolicy: RenderCacheOptions.Disabled));
                using RenderTargetLeaseSession targets = pool.BeginSession(RenderIntent.Delivery);
                using RenderTargetLease output = targets.Acquire(new PixelSize(24, 24));
                using var canvas = new ImmediateCanvas(output.Target, RenderIntent.Delivery);
                canvas.Clear();
                var executor = new RenderRequestExecutor(targets, graphProgramCache: programs);
                executor.Execute(request, canvas);
                using Bitmap snapshot = output.Target.Snapshot();
                return snapshot.GetPixelSpan<ushort>().ToArray().Select(v => (float)BitConverter.UInt16BitsToHalf(v)).ToArray();
            }
        });
    }

    private sealed class FailingFactory(bool cancelled) : IRenderTargetFactory
    {
        public int Attempts { get; private set; }
        public bool Fail { get; set; } = true;
        public List<RenderTarget> Targets { get; } = [];

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            Attempts++;
            if (Fail && Attempts >= 3)
            {
                if (cancelled) throw new OperationCanceledException("Injected graph target cancellation.");
                return null;
            }
            RenderTarget? target = RenderTarget.Create(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
            if (target is not null) Targets.Add(target);
            return target;
        }
    }
}

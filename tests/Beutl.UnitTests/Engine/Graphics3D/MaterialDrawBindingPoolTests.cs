using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Materials;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public sealed class MaterialDrawBindingPoolTests
{
    [Test]
    public void ARetiredBinding_IsNotHandedOutUntilTheWorkRecordedBeforeItsReplacementCompletes()
    {
        var queue = new DeferredWorkQueue();
        using var pool = CreatePool(queue);

        MaterialDrawBindings first = Draw(pool);
        MaterialDrawBindings second = Draw(pool);
        MaterialDrawBindings third = Draw(pool);

        Assert.That(new[] { first, second, third }, Is.Unique, "each draw still pending on the device needs its own bindings");

        queue.CompleteAll();
        MaterialDrawBindings reused = Draw(pool);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reused, Is.Not.SameAs(third), "the bindings a render pass still binds are never free");
            Assert.That(pool.CreatedCount, Is.EqualTo(3));
        }
    }

    [Test]
    public void AFlushBetweenAcquireAndBind_DoesNotFreeTheBindingTheRenderPassReplays()
    {
        var queue = new DeferredWorkQueue();
        using var pool = CreatePool(queue);
        MaterialDrawBindings bound = Draw(pool);

        MaterialDrawBindings replacement = pool.Acquire();
        // A flush here splits the render pass, which replays the bindings it still binds onto a fresh command buffer.
        queue.CompleteAll();
        pool.MarkBound(replacement);

        Assert.That(pool.Acquire(), Is.Not.SameAs(bound));
    }

    [Test]
    public void FramesWhoseWorkCompletes_StopCreatingBindingsAfterTheFirstReuse()
    {
        const int drawsPerFrame = 5;
        var queue = new DeferredWorkQueue();
        using var pool = CreatePool(queue);

        var createdAfterEachFrame = new List<int>();
        for (int frame = 0; frame < 6; frame++)
        {
            for (int draw = 0; draw < drawsPerFrame; draw++)
                Draw(pool);

            queue.CompleteAll();
            createdAfterEachFrame.Add(pool.CreatedCount);
        }

        // The extra binding stands in for the one a frame leaves bound until the next frame replaces it.
        Assert.That(createdAfterEachFrame, Is.EqualTo(new[] { 5, 6, 6, 6, 6, 6 }));
    }

    [Test]
    public void FramesStillPendingOnTheDevice_EachHoldTheirOwnBindings()
    {
        const int pendingDraws = 9;
        var queue = new DeferredWorkQueue();
        using var pool = CreatePool(queue);

        for (int draw = 0; draw < pendingDraws; draw++)
            Draw(pool);

        Assert.That(pool.CreatedCount, Is.EqualTo(pendingDraws));

        queue.CompleteAll();
        for (int draw = 0; draw < pendingDraws - 1; draw++)
            Draw(pool);

        Assert.That(pool.CreatedCount, Is.EqualTo(pendingDraws), "every binding but the bound one came back");
    }

    [Test]
    public void ABindingAcquiredByADrawThatThrew_WaitsForPendingWorkBeforeItIsReused()
    {
        var queue = new DeferredWorkQueue();
        using var pool = CreatePool(queue);

        // The draw threw after Acquire, possibly after recording the bindings, so MarkBound never ran.
        MaterialDrawBindings abandoned = pool.Acquire();
        MaterialDrawBindings next = pool.Acquire();
        pool.MarkBound(next);

        Assert.That(next, Is.Not.SameAs(abandoned));

        queue.CompleteAll();

        Assert.That(pool.Acquire(), Is.SameAs(abandoned));
    }

    [Test]
    public void Dispose_ReleasesIdleAndBoundBindings_AndPendingOnesOnceTheirWorkCompletes()
    {
        var queue = new DeferredWorkQueue();
        var pool = CreatePool(queue);
        MaterialDrawBindings first = Draw(pool);
        MaterialDrawBindings second = Draw(pool);
        MaterialDrawBindings pending = Draw(pool);
        queue.CompleteAll();
        MaterialDrawBindings bound = Draw(pool);
        MaterialDrawBindings idle = ReferenceEquals(bound, first) ? second : first;

        pool.Dispose();

        using (Assert.EnterMultipleScope())
        {
            AssertDisposed(idle, Times.Once());
            AssertDisposed(bound, Times.Once());
            AssertDisposed(pending, Times.Never());
        }

        queue.CompleteAll();

        AssertDisposed(pending, Times.Once());
    }

    [Test]
    public void WithoutADeferredReleaseQueue_ARetiredBindingIsDisposedInsteadOfReused()
    {
        using var pool = CreatePool(queue: null);
        MaterialDrawBindings first = Draw(pool);
        Draw(pool);
        Draw(pool);

        using (Assert.EnterMultipleScope())
        {
            AssertDisposed(first, Times.Once());
            Assert.That(pool.CreatedCount, Is.EqualTo(3));
        }
    }

    [Test]
    public void MarkBound_RejectsBindingsOtherThanTheOnesJustAcquired()
    {
        var queue = new DeferredWorkQueue();
        using var pool = CreatePool(queue);
        MaterialDrawBindings bound = Draw(pool);
        pool.Acquire();

        Assert.Throws<InvalidOperationException>(() => pool.MarkBound(bound));
    }

    [Test]
    public void Create_OnAContextWithoutADeferredReleaseQueue_DisposesRetiredBindingsInsteadOfReusingThem()
    {
        var context = new Mock<IGraphicsContext>();
        context.Setup(static c => c.CreateBuffer(It.IsAny<ulong>(), It.IsAny<BufferUsage>(), It.IsAny<MemoryProperty>()))
            .Returns(static () => Mock.Of<IBuffer>());
        context.Setup(static c => c.CreateDescriptorSet(It.IsAny<IPipeline3D>(), It.IsAny<DescriptorPoolSize[]>()))
            .Returns(static () => Mock.Of<IDescriptorSet>());
        using var pool = MaterialDrawBindingPool.Create<TestUniforms>(context.Object, Mock.Of<IPipeline3D>(), 1);

        MaterialDrawBindings first = Draw(pool);
        MaterialDrawBindings second = Draw(pool);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.Not.SameAs(first));
            AssertDisposed(first, Times.Once());
            Mock.Get(first.Descriptors).Verify(descriptors => descriptors.UpdateBuffer(0, first.Buffer), Times.Once());
            context.Verify(
                static c => c.CreateBuffer(
                    (ulong)System.Runtime.InteropServices.Marshal.SizeOf<TestUniforms>(),
                    BufferUsage.UniformBuffer,
                    It.IsAny<MemoryProperty>()),
                Times.Exactly(2));
        }
    }

    private static MaterialDrawBindingPool CreatePool(DeferredWorkQueue? queue)
        => new(
            static () => new MaterialDrawBindings(Mock.Of<IBuffer>(), Mock.Of<IDescriptorSet>()),
            queue is null ? null : queue.Defer);

    private static MaterialDrawBindings Draw(MaterialDrawBindingPool pool)
    {
        MaterialDrawBindings bindings = pool.Acquire();
        pool.MarkBound(bindings);
        return bindings;
    }

    private static void AssertDisposed(MaterialDrawBindings bindings, Times times)
    {
        Mock.Get(bindings.Buffer).Verify(static buffer => buffer.Dispose(), times);
        Mock.Get(bindings.Descriptors).Verify(static descriptors => descriptors.Dispose(), times);
    }

    private readonly record struct TestUniforms(float Value);

    /// <summary>Stands in for the backend queue that runs a release once the submissions recorded so far complete.</summary>
    private sealed class DeferredWorkQueue
    {
        private readonly List<Action> _pending = [];

        public void Defer(Action release) => _pending.Add(release);

        public void CompleteAll()
        {
            Action[] completed = [.. _pending];
            _pending.Clear();
            foreach (Action release in completed)
                release();
        }
    }
}

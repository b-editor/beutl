using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

/// <summary>
/// <see cref="EffectTargets"/> owns the render target behind every element, so every member that drops an
/// element has to release it, and <see cref="EffectTargets.DetachAt"/> is the one way to take an element out
/// alive. A disposed <see cref="EffectTarget"/> reports <see cref="EffectTarget.IsEmpty"/>.
/// </summary>
[TestFixture]
public sealed class EffectTargetsOwnershipTests
{
    private static readonly Rect s_bounds = new(0, 0, 4, 4);

    [Test]
    public void IndexerSetter_DisposesTheTargetItReplaces()
    {
        using var targets = new EffectTargets();
        EffectTarget original = CreateTarget();
        EffectTarget replacement = CreateTarget();
        targets.Add(original);

        targets[0] = replacement;

        Assert.Multiple(() =>
        {
            Assert.That(original.IsEmpty, Is.True, "the replaced target must be disposed");
            Assert.That(replacement.IsEmpty, Is.False, "the new target must stay alive");
            Assert.That(targets[0], Is.SameAs(replacement));
        });
    }

    [Test]
    public void IndexerSetter_LeavesTheSameInstanceAlive()
    {
        using var targets = new EffectTargets();
        EffectTarget target = CreateTarget();
        targets.Add(target);

        targets[0] = target;

        Assert.That(target.IsEmpty, Is.False, "assigning a target to its own slot must not dispose it");
    }

    [Test]
    public void RemoveAt_DisposesTheRemovedTarget()
    {
        using var targets = new EffectTargets();
        EffectTarget first = CreateTarget();
        EffectTarget second = CreateTarget();
        targets.Add(first);
        targets.Add(second);

        targets.RemoveAt(0);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsEmpty, Is.True);
            Assert.That(second.IsEmpty, Is.False);
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0], Is.SameAs(second));
        });
    }

    [Test]
    public void Remove_DisposesOnlyTheTargetItRemoves()
    {
        using var targets = new EffectTargets();
        EffectTarget kept = CreateTarget();
        EffectTarget removed = CreateTarget();
        using EffectTarget absent = CreateTarget();
        targets.Add(kept);
        targets.Add(removed);

        bool removedResult = targets.Remove(removed);
        bool absentResult = targets.Remove(absent);

        Assert.Multiple(() =>
        {
            Assert.That(removedResult, Is.True);
            Assert.That(absentResult, Is.False);
            Assert.That(removed.IsEmpty, Is.True);
            Assert.That(kept.IsEmpty, Is.False);
            Assert.That(absent.IsEmpty, Is.False, "a target the list never held must not be touched");
            Assert.That(targets, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Clear_DisposesEveryTarget()
    {
        using var targets = new EffectTargets();
        EffectTarget first = CreateTarget();
        EffectTarget second = CreateTarget();
        targets.Add(first);
        targets.Add(second);

        targets.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(first.IsEmpty, Is.True);
            Assert.That(second.IsEmpty, Is.True);
            Assert.That(targets, Is.Empty);
        });
    }

    [Test]
    public void DetachAt_HandsTheTargetBackAlive()
    {
        using var targets = new EffectTargets();
        EffectTarget first = CreateTarget();
        EffectTarget second = CreateTarget();
        targets.Add(first);
        targets.Add(second);

        using EffectTarget detached = targets.DetachAt(0);

        Assert.Multiple(() =>
        {
            Assert.That(detached, Is.SameAs(first));
            Assert.That(detached.IsEmpty, Is.False, "detaching must not dispose");
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0], Is.SameAs(second));
        });
    }

    [Test]
    public void ForEach_ReplacingOverload_DisposesOnlyTheTargetsItReplaces()
    {
        EffectTarget kept = CreateTarget();
        EffectTarget replaced = CreateTarget();
        EffectTarget replacement = CreateTarget();
        using var targets = new EffectTargets { kept, replaced };
        (bool keptAlive, bool replacedDisposed, bool replacementAlive, EffectTarget? slot1)? observed = null;

        RunCustomEffect(targets, execution =>
        {
            execution.ForEach((index, target) => index == 1 ? replacement : target);
            observed = (!kept.IsEmpty, replaced.IsEmpty, !replacement.IsEmpty, execution.Targets[1]);
        });

        Assert.That(observed, Is.Not.Null, "the custom effect must run");
        Assert.Multiple(() =>
        {
            Assert.That(observed!.Value.keptAlive, Is.True, "a target returned unchanged must stay alive");
            Assert.That(observed.Value.replacedDisposed, Is.True, "a replaced target must be disposed");
            Assert.That(observed.Value.replacementAlive, Is.True);
            Assert.That(observed.Value.slot1, Is.SameAs(replacement));
        });
    }

    [Test]
    public void ForEach_ExpandingOverload_DisposesTheOriginalAndOwnsTheReturnedTargets()
    {
        EffectTarget original = CreateTarget();
        EffectTarget firstPart = CreateTarget();
        EffectTarget secondPart = CreateTarget();
        using var targets = new EffectTargets { original };
        (bool originalDisposed, bool partsAlive, int count)? observed = null;

        RunCustomEffect(targets, execution =>
        {
            execution.ForEach((_, clone) =>
            {
                clone.Dispose();
                return new EffectTargets { firstPart, secondPart };
            });
            observed = (original.IsEmpty, !firstPart.IsEmpty && !secondPart.IsEmpty, execution.Targets.Count);
        });

        Assert.That(observed, Is.Not.Null, "the custom effect must run");
        Assert.Multiple(() =>
        {
            Assert.That(observed!.Value.originalDisposed, Is.True, "the expanded original must be disposed");
            Assert.That(observed.Value.partsAlive, Is.True, "the returned targets now belong to the list");
            Assert.That(observed.Value.count, Is.EqualTo(2));
        });
    }

    [Test]
    public void ForEach_ExpandingOverload_RejectsTheContextsOwnList()
    {
        EffectTarget first = CreateTarget();
        EffectTarget second = CreateTarget();
        using var targets = new EffectTargets { first, second };
        (int count, bool alive)? observed = null;

        RunCustomEffect(targets, execution =>
        {
            // Returning the live list would alias source and destination of the move; it must be refused
            // before anything is detached.
            Assert.Throws<InvalidOperationException>(() => execution.ForEach((_, _) => execution.Targets));
            observed = (execution.Targets.Count, !first.IsEmpty && !second.IsEmpty);
        });

        Assert.That(observed, Is.Not.Null, "the custom effect must run");
        Assert.Multiple(() =>
        {
            Assert.That(observed!.Value.count, Is.EqualTo(2), "the list must be left as it was");
            Assert.That(observed.Value.alive, Is.True, "no target may be disposed by the refused call");
        });
    }

    private static void RunCustomEffect(EffectTargets targets, Action<CustomFilterEffectContext> effect)
    {
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Frame,
            drawableBrushMaterializer: null);
        using var context = new FilterEffectContext(s_bounds);
        context.CustomEffect(
            0,
            (_, execution) => effect(execution),
            static (_, bounds) => bounds);

        activator.Apply(context);
    }

    private static EffectTarget CreateTarget()
        => new(new CpuRenderTarget(4, 4), s_bounds, EffectiveScale.At(1));

    private sealed class CpuRenderTarget : RenderTarget
    {
        public CpuRenderTarget(int width, int height)
            : base(CreateSurface(width, height), width, height)
        {
        }

        private static SKSurface CreateSurface(int width, int height)
            => SKSurface.Create(new SKImageInfo(
                   width,
                   height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   SKColorSpace.CreateSrgbLinear()))
               ?? throw new InvalidOperationException("Failed to create a CPU surface.");
    }
}

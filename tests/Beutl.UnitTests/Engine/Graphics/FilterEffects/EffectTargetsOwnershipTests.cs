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
    public void InsertRange_MovesTheTargetsOutOfAnotherList()
    {
        using var destination = new EffectTargets { CreateTarget() };
        EffectTarget first = CreateTarget();
        EffectTarget second = CreateTarget();
        var source = new EffectTargets { first, second };

        destination.InsertRange(0, source);
        // The source no longer owns anything, so disposing it must not reach the moved targets.
        source.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(destination, Has.Count.EqualTo(3));
            Assert.That(destination[0], Is.SameAs(first));
            Assert.That(destination[1], Is.SameAs(second));
            Assert.That(first.IsEmpty, Is.False, "a moved target must stay alive");
            Assert.That(second.IsEmpty, Is.False, "a moved target must stay alive");
        });
    }

    [Test]
    public void AddRange_MovesTheTargetsOutOfAnotherList()
    {
        using var destination = new EffectTargets();
        EffectTarget moved = CreateTarget();
        using var source = new EffectTargets { moved };

        destination.AddRange(source);

        Assert.Multiple(() =>
        {
            Assert.That(source, Is.Empty, "the source must be left empty");
            Assert.That(destination[0], Is.SameAs(moved));
            Assert.That(moved.IsEmpty, Is.False);
        });
    }

    [Test]
    public void InsertRange_RejectsTheListItself()
    {
        EffectTarget target = CreateTarget();
        using var targets = new EffectTargets { target };

        Assert.Throws<InvalidOperationException>(() => targets.InsertRange(0, targets));
        Assert.Multiple(() =>
        {
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(target.IsEmpty, Is.False);
        });
    }

    [Test]
    public void InsertRange_RefusesATargetTheListAlreadyOwns()
    {
        EffectTarget owned = CreateTarget();
        using var targets = new EffectTargets { owned };
        using EffectTarget other = CreateTarget();

        // A deferred query over the list itself is materialized before anything changes, and the target it
        // yields is already owned, so the call is refused and the list stays as it was.
        Assert.Throws<InvalidOperationException>(() => targets.InsertRange(0, targets.Where(_ => true)));
        Assert.Throws<InvalidOperationException>(() => targets.InsertRange(0, new[] { other, owned }));
        Assert.Multiple(() =>
        {
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0], Is.SameAs(owned));
            Assert.That(owned.IsEmpty, Is.False);
            Assert.That(other.IsEmpty, Is.False, "a refused insertion must not take the other targets");
        });
    }

    [Test]
    public void Add_Insert_AndTheIndexer_RefuseATargetHeldInAnotherSlot()
    {
        EffectTarget first = CreateTarget();
        EffectTarget second = CreateTarget();
        using var targets = new EffectTargets { first, second };

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => targets.Add(first));
            Assert.Throws<InvalidOperationException>(() => targets.Insert(0, second));
            Assert.Throws<InvalidOperationException>(() => targets[0] = second);
            Assert.That(targets, Has.Count.EqualTo(2));
            Assert.That(targets[0], Is.SameAs(first));
            Assert.That(first.IsEmpty, Is.False);
            Assert.That(second.IsEmpty, Is.False);
        });
    }

    [Test]
    public void InsertRange_RefusesMovingATargetTheDestinationAlreadyHolds()
    {
        EffectTarget shared = CreateTarget();
        EffectTarget other = CreateTarget();
        using var destination = new EffectTargets { shared };
        using var source = new EffectTargets { other, shared };

        // Two lists that were handed the same instance is already a contract violation; the move must not
        // turn it into two slots of one list, and it must not move anything before refusing.
        Assert.Throws<InvalidOperationException>(() => destination.InsertRange(0, source));
        Assert.Multiple(() =>
        {
            Assert.That(destination, Has.Count.EqualTo(1));
            Assert.That(source, Has.Count.EqualTo(2));
            Assert.That(shared.IsEmpty, Is.False);
            Assert.That(other.IsEmpty, Is.False);
        });

        // Detach the shared instance from one list so the two disposals below do not overlap.
        source.DetachAt(1);
    }

    [Test]
    public void InsertRange_RefusesASequenceThatRepeatsATarget()
    {
        using var targets = new EffectTargets();
        using EffectTarget repeated = CreateTarget();

        Assert.Throws<InvalidOperationException>(() => targets.InsertRange(0, new[] { repeated, repeated }));
        Assert.Multiple(() =>
        {
            Assert.That(targets, Is.Empty);
            Assert.That(repeated.IsEmpty, Is.False);
        });
    }

    [Test]
    public void InsertRange_ValidatesTheIndexBeforeEnumerating()
    {
        using var targets = new EffectTargets();
        bool enumerated = false;
        IEnumerable<EffectTarget> Deferred()
        {
            enumerated = true;
            yield return CreateTarget();
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => targets.InsertRange(1, Deferred()));
        Assert.That(enumerated, Is.False, "an invalid index must be rejected before the sequence runs");
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

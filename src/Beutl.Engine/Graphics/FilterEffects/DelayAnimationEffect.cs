using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Proxy;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.DelayAnimationEffect), ResourceType = typeof(GraphicsStrings))]
[SuppressResourceClassGeneration]
public partial class DelayAnimationEffect : FilterEffect
{
    public DelayAnimationEffect()
    {
        ScanProperties<DelayAnimationEffect>();
        Effect.Value = new FilterEffectGroup();
    }

    [Display(Name = nameof(GraphicsStrings.DelayAnimationEffect_Delay), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Delay { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.DelayAnimationEffect_Order), Description = nameof(GraphicsStrings.DelayAnimationEffect_Order_Description),
        ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<DelayAnimationOrder> Order { get; } = Property.CreateAnimatable(DelayAnimationOrder.Forward);

    [Display(Name = nameof(GraphicsStrings.DelayAnimationEffect_RandomSeed), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<int> RandomSeed { get; } = Property.CreateAnimatable(0);

    [Display(Name = nameof(GraphicsStrings.DelayAnimationEffect_Effect), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<FilterEffect?> Effect { get; } = Property.CreateExpression<FilterEffect?>();

    /// <summary>
    /// Applies the child effect to each target at a time held back by <c>delay</c> milliseconds multiplied by its rank,
    /// keeping one child resource per target in <c>cache</c>.
    /// </summary>
    private static void ApplyDelayed(
        (float delay, DelayAnimationOrder order, int randomSeed, MediaTime globalTime,
            FilterEffect childEffect, List<FilterEffect.Resource> cache,
            bool disableResourceShare, CompositionConditions conditions) data,
        CustomFilterEffectContext effectContext)
    {
        int targetCount = effectContext.Targets.Count;
        Span<float> ranks = targetCount <= 128 ? stackalloc float[targetCount] : new float[targetCount];
        Rect?[]? bounds = null;
        if (data.order is DelayAnimationOrder.LeftToRight or DelayAnimationOrder.RightToLeft
            or DelayAnimationOrder.TopToBottom or DelayAnimationOrder.BottomToTop)
        {
            bounds = new Rect?[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                EffectTarget target = effectContext.Targets[i];
                if (!target.IsEmpty)
                    bounds[i] = target.Bounds;
            }
        }

        DelayAnimationOrdering.ComputeRanks(data.order, data.randomSeed, bounds, ranks);

        for (int i = data.cache.Count; i < targetCount; i++)
        {
            MediaTime delayedTime = GetDelayedTime(data.globalTime, data.delay, ranks[i]);
            data.cache.Add(data.childEffect.ToResource(CreateDelayedContext(
                delayedTime,
                data.disableResourceShare,
                data.conditions)));
        }

        while (data.cache.Count > targetCount)
        {
            data.cache[^1].Dispose();
            data.cache.RemoveAt(data.cache.Count - 1);
        }

        // i follows the live list after splits, while j follows the original ranks and resources.
        for (int i = 0, j = 0; j < targetCount; i++, j++)
        {
            EffectTarget target = effectContext.Targets[i];
            if (target.IsEmpty) continue;

            MediaTime delayedTime = GetDelayedTime(data.globalTime, data.delay, ranks[j]);
            var delayedContext = CreateDelayedContext(
                delayedTime,
                data.disableResourceShare,
                data.conditions);
            var versionBumped = false;
            data.cache[j].Reconcile(data.childEffect, delayedContext, ref versionBumped);

            if (!data.cache[j].IsEnabled) continue;

            // Forward output scale and working density into the nested re-application.
            using var childFEContext = new FilterEffectContext(
                target.Bounds, effectContext.OutputScale, effectContext.WorkingScale);
            childFEContext.ApplyTransactional(data.cache[j]);

            target.OriginalBounds = target.Bounds.WithX(0).WithY(0);
            using var singleTargets = new EffectTargets();
            singleTargets.Add(target.Clone());
            using var builder = new SKImageFilterBuilder();
            using FilterEffectExecutor executor =
                effectContext.CreateExecutor(singleTargets, builder);
            executor.Apply(childFEContext);
            executor.Flush(false);

            if (singleTargets.Count > 0)
            {
                effectContext.Targets[i] = singleTargets[0].Clone();

                for (int k = 1; k < singleTargets.Count; k++)
                {
                    effectContext.Targets.Insert(i + k, singleTargets[k].Clone());
                }

                i += singleTargets.Count - 1;
            }
        }
    }

    private static MediaTime GetDelayedTime(MediaTime globalTime, float delay, float rank)
    {
        float milliseconds = delay * rank;
        return float.IsFinite(milliseconds) ? globalTime - MediaTime.FromSeconds(milliseconds / 1000d) : globalTime;
    }

    /// <summary>
    /// Creates the context a delayed copy of the child effect is resolved in, carrying the host's output conditions.
    /// </summary>
    private static CompositionContext CreateDelayedContext(
        MediaTime delayedTime,
        bool disableResourceShare,
        CompositionConditions conditions)
        => new(delayedTime)
        {
            DisableResourceShare = disableResourceShare,
            Conditions = conditions,
        };

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    public new class Resource : FilterEffect.Resource
    {
        private float _delay;
        private DelayAnimationOrder _order;
        private int _randomSeed;
        private FilterEffect.Resource? _effect;
        private MediaTime _globalTime;
        private bool _disableResourceShare;
        private CompositionConditions _conditions = CompositionConditions.Default;
        private long _fontRevision;
        private readonly List<FilterEffect.Resource> _delayedResources = [];

        public float Delay => _delay;

        public DelayAnimationOrder Order => _order;

        public int RandomSeed => _randomSeed;

        public FilterEffect.Resource? Effect => _effect;

        public MediaTime GlobalTime => _globalTime;

        public bool DisableResourceShare => _disableResourceShare;

        public bool PreferProxy => _conditions.PreferProxy;

        public ProxyPreset PreferredProxyPreset => _conditions.PreferredProxyPreset;

        public List<FilterEffect.Resource> DelayedResources => _delayedResources;

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);

            var typed = (DelayAnimationEffect)obj;
            ResourceReconciler.ReconcileValue(this, context, typed.Delay, ref _delay, ref versionBumped);
            ResourceReconciler.ReconcileValue(this, context, typed.Order, ref _order, ref versionBumped);
            ResourceReconciler.ReconcileValue(this, context, typed.RandomSeed, ref _randomSeed, ref versionBumped);
            ResourceReconciler.ReconcileChild(this, context, typed.Effect, ref _effect, ref versionBumped);
            if (_delayedResources.Count > 0 && _delayedResources[0].GetOriginal() != Effect?.GetOriginal())
            {
                foreach (var r in _delayedResources)
                    r.Dispose();
                _delayedResources.Clear();
            }

            MediaTime oldTime = _globalTime;
            CompositionConditions oldConditions = _conditions;
            long oldFontRevision = _fontRevision;
            _globalTime = context.Time;
            _disableResourceShare = context.DisableResourceShare;
            _conditions = context.Conditions;
            _fontRevision = context.Fonts?.Revision ?? 0;

            // Catalog and proxy changes must invalidate the cached delayed targets.
            bool selectionChanged = oldConditions.PreferProxy != _conditions.PreferProxy
                || oldConditions.PreferredProxyPreset != _conditions.PreferredProxyPreset
                || !ReferenceEquals(oldConditions.Fonts, _conditions.Fonts)
                || !ReferenceEquals(oldConditions.Decoders, _conditions.Decoders)
                || oldFontRevision != _fontRevision;
            if (!versionBumped && (oldTime != _globalTime || selectionChanged))
            {
                Version++;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _effect?.Dispose();
                _effect = null;

                foreach (var r in _delayedResources)
                    r.Dispose();
                _delayedResources.Clear();
            }

            base.Dispose(disposing);
        }

        protected override void ApplyTo(FilterEffectContext context)
        {
            if (Effect == null) return;

            FilterEffect childEffect = Effect.RequireOriginal();

            context.CustomEffect(
                (delay: Delay, order: Order, randomSeed: RandomSeed, globalTime: GlobalTime,
                 childEffect, cache: DelayedResources,
                 disableResourceShare: DisableResourceShare,
                 conditions: _conditions),
                ApplyDelayed);
        }
    }
}

internal static class DelayAnimationOrdering
{
    // Positional orders read one bounds entry per rank. Null marks an empty target.
    internal static void ComputeRanks(
        DelayAnimationOrder order, int seed, ReadOnlySpan<Rect?> bounds, Span<float> ranks)
    {
        if (ranks.Length <= 1)
        {
            ranks.Clear();
            return;
        }

        switch (order)
        {
            case DelayAnimationOrder.Reverse:
                for (int i = 0; i < ranks.Length; i++)
                    ranks[i] = ranks.Length - 1 - i;
                break;
            case DelayAnimationOrder.FromCenter:
                float center = (ranks.Length - 1) / 2f;
                for (int i = 0; i < ranks.Length; i++)
                    ranks[i] = MathF.Abs(i - center);
                break;
            case DelayAnimationOrder.Random:
                ComputeRandomRanks(seed, ranks);
                break;
            case DelayAnimationOrder.LeftToRight:
            case DelayAnimationOrder.RightToLeft:
            case DelayAnimationOrder.TopToBottom:
            case DelayAnimationOrder.BottomToTop:
                ComputePositionRanks(bounds, ranks,
                    horizontal: order is DelayAnimationOrder.LeftToRight or DelayAnimationOrder.RightToLeft,
                    reverse: order is DelayAnimationOrder.RightToLeft or DelayAnimationOrder.BottomToTop);
                break;
            default:
                for (int i = 0; i < ranks.Length; i++)
                    ranks[i] = i;
                break;
        }
    }

    private static void ComputeRandomRanks(int seed, Span<float> ranks)
    {
        Span<(uint Hash, int Index)> keys = ranks.Length <= 128
            ? stackalloc (uint, int)[ranks.Length]
            : new (uint, int)[ranks.Length];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = (Hash(seed, i), i);

        keys.Sort();
        for (int rank = 0; rank < keys.Length; rank++)
            ranks[keys[rank].Index] = rank;
    }

    private static uint Hash(int seed, int index)
    {
        unchecked
        {
            uint hash = (uint)seed * 0x9E3779B9u ^ (uint)index * 0x85EBCA6Bu;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            return hash ^ (hash >> 16);
        }
    }

    private static void ComputePositionRanks(
        ReadOnlySpan<Rect?> bounds, Span<float> ranks, bool horizontal, bool reverse)
    {
        Span<float> sizes = ranks.Length <= 128 ? stackalloc float[ranks.Length] : new float[ranks.Length];
        int sizeCount = 0;
        float edge = reverse ? float.NegativeInfinity : float.PositiveInfinity;

        for (int i = 0; i < ranks.Length; i++)
        {
            if (bounds[i] is not { } part)
            {
                ranks[i] = float.NaN;
                continue;
            }

            float center = horizontal ? part.Center.X : part.Center.Y;
            ranks[i] = center;
            if (float.IsFinite(center))
                edge = reverse ? Math.Max(edge, center) : Math.Min(edge, center);

            float size = horizontal ? part.Width : part.Height;
            if (size > 0 && float.IsFinite(size))
                sizes[sizeCount++] = size;
        }

        sizes[..sizeCount].Sort();
        double unit = sizeCount == 0 ? 1 : ((double)sizes[(sizeCount - 1) / 2] + sizes[sizeCount / 2]) / 2;
        for (int i = 0; i < ranks.Length; i++)
        {
            double distance = reverse ? (double)edge - ranks[i] : (double)ranks[i] - edge;
            float rank = (float)(distance / unit);
            ranks[i] = float.IsFinite(rank) ? rank : 0;
        }
    }
}

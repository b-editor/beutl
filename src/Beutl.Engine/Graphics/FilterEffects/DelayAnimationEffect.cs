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
        Effect.CurrentValue = new FilterEffectGroup();
    }

    [Display(Name = nameof(GraphicsStrings.DelayAnimationEffect_Delay), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Delay { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.DelayAnimationEffect_Effect), ResourceType = typeof(GraphicsStrings))]
    public IProperty<FilterEffect?> Effect { get; } = Property.Create<FilterEffect?>();

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Effect is not { } child) return;

        // Release per-branch resources left over from an earlier describe that fanned out into more branches than the
        // most recent one produced: the cache only grows in the branch callback, so a shrinking branch count (a split
        // that emitted fewer parts) would otherwise retain the stale resources until the effect is disposed. The trim
        // target is the branch count observed during the previous describe's callbacks.
        r.TrimDelayedResourcesToObservedBranchCount();

        // A nested graph per branch (research D8): branch i re-describes the child effect at the clock delayed by
        // delay × i, so a split fan-out gets the staggered animation the legacy per-target pull produced. Branch 0's
        // delayed clock is the current clock, so the single-input path equals describing the child directly.
        var childEffect = child.GetOriginal();
        float delay = r.Delay;
        TimeSpan globalTime = r.GlobalTime;
        bool disableResourceShare = r.DisableResourceShare;
        bool preferProxy = r.PreferProxy;
        ProxyPreset preferredProxyPreset = r.PreferredProxyPreset;
        List<FilterEffect.Resource> cache = r.DelayedResources;

        builder.NestedGraph(NestedGraphNodeDescriptor.Create(
            (branchBuilder, i) =>
            {
                r.ObserveBranch(i);
                TimeSpan delayedTime = globalTime - TimeSpan.FromMilliseconds(delay * i);
                var delayedContext = new CompositionContext(delayedTime)
                {
                    DisableResourceShare = disableResourceShare,
                    PreferProxy = preferProxy,
                    PreferredProxyPreset = preferredProxyPreset,
                };
                while (cache.Count <= i)
                {
                    cache.Add(childEffect.ToResource(delayedContext));
                }

                bool updateOnly = false;
                cache[i].Update(childEffect, delayedContext, ref updateOnly);
                if (cache[i].IsEnabled)
                {
                    childEffect.Describe(branchBuilder, cache[i]);
                }
            },
            structuralToken: nameof(DelayAnimationEffect)));
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new class Resource : FilterEffect.Resource
    {
        private float _delay;
        private FilterEffect.Resource? _effect;
        private TimeSpan _globalTime;
        private bool _disableResourceShare;
        private bool _preferProxy;
        private ProxyPreset _preferredProxyPreset = ProxyPreset.Quarter;
        private readonly List<FilterEffect.Resource> _delayedResources = [];
        private int _branchHighWater;

        public float Delay => _delay;

        public FilterEffect.Resource? Effect => _effect;

        public TimeSpan GlobalTime => _globalTime;

        public bool DisableResourceShare => _disableResourceShare;

        public bool PreferProxy => _preferProxy;

        public ProxyPreset PreferredProxyPreset => _preferredProxyPreset;

        public List<FilterEffect.Resource> DelayedResources => _delayedResources;

        // Records that branch `index` was described this pass; the trim at the next describe keeps the cache no larger
        // than the highest branch count seen.
        internal void ObserveBranch(int index)
        {
            if (index + 1 > _branchHighWater)
                _branchHighWater = index + 1;
        }

        // Disposes and removes cache entries above the previous pass's observed branch count, then resets the mark so
        // the current pass's callbacks re-accumulate it.
        internal void TrimDelayedResourcesToObservedBranchCount()
        {
            while (_delayedResources.Count > _branchHighWater)
            {
                _delayedResources[^1].Dispose();
                _delayedResources.RemoveAt(_delayedResources.Count - 1);
            }

            _branchHighWater = 0;
        }

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            var typed = (DelayAnimationEffect)obj;
            CompareAndUpdate(context, typed.Delay, ref _delay, ref updateOnly);
            CompareAndUpdateObject(context, typed.Effect, ref _effect, ref updateOnly);
            if (_delayedResources.Count > 0 && _delayedResources[0].GetOriginal() != Effect?.GetOriginal())
            {
                foreach (var r in _delayedResources)
                    r.Dispose();
                _delayedResources.Clear();
            }

            TimeSpan oldTime = _globalTime;
            bool oldPreferProxy = _preferProxy;
            ProxyPreset oldPreferredPreset = _preferredProxyPreset;
            _globalTime = context.Time;
            _disableResourceShare = context.DisableResourceShare;
            _preferProxy = context.PreferProxy;
            _preferredProxyPreset = context.PreferredProxyPreset;

            // Bump Version on a proxy-selection change too, or RenderNodeCache would replay stale
            // tiles and a preview proxy-mode toggle would not reach the delayed sub-effects until an
            // unrelated invalidation (e.g. a time change) happens.
            bool proxyChanged = oldPreferProxy != _preferProxy || oldPreferredPreset != _preferredProxyPreset;
            if (!updateOnly && (oldTime != _globalTime || proxyChanged))
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
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.ExceptionServices;
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

        // A nested graph per branch (research D8): branch i re-describes the child effect at the clock delayed by
        // delay × i, so a split fan-out gets the staggered animation the legacy per-target pull produced. Branch 0's
        // delayed clock is the current clock, so the single-input path equals describing the child directly.
        var childEffect = child.GetOriginal();
        float delay = r.Delay;
        TimeSpan globalTime = r.GlobalTime;
        bool disableResourceShare = r.DisableResourceShare;
        bool preferProxy = r.PreferProxy;
        ProxyPreset preferredProxyPreset = r.PreferredProxyPreset;

        builder.NestedGraph(NestedGraphNodeDescriptor.CreateStateful(
            (branchBuilder, i) =>
            {
                TimeSpan delayedTime = globalTime - TimeSpan.FromMilliseconds(delay * i);
                var delayedContext = new CompositionContext(
                    delayedTime,
                    branchBuilder.RenderIntent,
                    branchBuilder.PullPurpose)
                {
                    DisableResourceShare = disableResourceShare,
                    PreferProxy = preferProxy,
                    PreferredProxyPreset = preferredProxyPreset,
                };
                FilterEffect.Resource delayedResource =
                    r.GetOrCreateDelayedResource(i, childEffect, delayedContext);

                bool updateOnly = false;
                delayedResource.Update(childEffect, delayedContext, ref updateOnly);
                if (delayedResource.IsEnabled)
                {
                    branchBuilder.Effect(delayedResource);
                }
            },
            r.PruneDelayedResources,
            structuralToken: nameof(DelayAnimationEffect)));
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        try
        {
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }
        catch
        {
            try
            {
                resource.Dispose();
            }
            catch
            {
                // Preserve the acquisition failure while reclaiming every partially initialized child resource.
            }

            throw;
        }
    }

    public new class Resource : FilterEffect.Resource
    {
        private static readonly IReadOnlyDictionary<int, FilterEffect.Resource> s_emptyDelayedResources
            = new ReadOnlyDictionary<int, FilterEffect.Resource>(new Dictionary<int, FilterEffect.Resource>());

        private float _delay;
        private FilterEffect.Resource? _effect;
        private TimeSpan _globalTime;
        private bool _disableResourceShare;
        private bool _preferProxy;
        private ProxyPreset _preferredProxyPreset = ProxyPreset.Quarter;
        private readonly Dictionary<int, FilterEffect.Resource> _delayedResources = [];
        private IReadOnlyDictionary<int, FilterEffect.Resource> _delayedResourcesSnapshot = s_emptyDelayedResources;
        private EngineObject? _delayedResourceOriginal;

        public float Delay => ReadGeneratedResourceState(ref _delay);

        public FilterEffect.Resource? Effect => ReadGeneratedResourceState(ref _effect);

        public TimeSpan GlobalTime => ReadGeneratedResourceState(ref _globalTime);

        public bool DisableResourceShare => ReadGeneratedResourceState(ref _disableResourceShare);

        public bool PreferProxy => ReadGeneratedResourceState(ref _preferProxy);

        public ProxyPreset PreferredProxyPreset
            => ReadGeneratedResourceState(ref _preferredProxyPreset);

        public IReadOnlyDictionary<int, FilterEffect.Resource> DelayedResources
            => ReadGeneratedResourceState(ref _delayedResourcesSnapshot);

        internal FilterEffect.Resource GetOrCreateDelayedResource(
            int branchOrdinal,
            EngineObject childEffect,
            CompositionContext context)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            ArgumentOutOfRangeException.ThrowIfNegative(branchOrdinal);
            if (!_delayedResources.TryGetValue(branchOrdinal, out FilterEffect.Resource? resource))
            {
                resource = (FilterEffect.Resource)childEffect.ToResource(context);
                _delayedResources.Add(branchOrdinal, resource);
                _delayedResourceOriginal ??= childEffect;
                PublishDelayedResourcesSnapshot();
            }

            return resource;
        }

        internal void PruneDelayedResources(IReadOnlySet<int> liveBranchOrdinals)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            List<KeyValuePair<int, FilterEffect.Resource>>? stale = null;
            foreach (KeyValuePair<int, FilterEffect.Resource> entry in _delayedResources)
            {
                if (!liveBranchOrdinals.Contains(entry.Key))
                    (stale ??= []).Add(entry);
            }

            if (stale == null)
                return;

            var roots = new FilterEffect.Resource[stale.Count];
            for (int i = 0; i < stale.Count; i++)
                roots[i] = stale[i].Value;

            ExceptionDispatchInfo? cleanupFailure = RetireOwnedResourceGraphs(roots);
            foreach (KeyValuePair<int, FilterEffect.Resource> entry in stale)
                _delayedResources.Remove(entry.Key);
            if (_delayedResources.Count == 0)
                _delayedResourceOriginal = null;
            PublishDelayedResourcesSnapshot();
            cleanupFailure?.Throw();
        }

        public sealed override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            var typed = (DelayAnimationEffect)obj;
            if (!IsCompatibleUpdateOwner(typed))
            {
                throw new InvalidCastException(
                    $"{GetType().FullName} cannot update from {typed.GetType().FullName}.");
            }

            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(typed);
            UpdateCore(typed, context, ref updateOnly);
        }

        /// <summary>
        /// Purely validates the owner type before the update lease is acquired or the published original changes.
        /// A resource paired with a derived effect overrides this predicate with its exact compatible owner type.
        /// </summary>
        protected virtual bool IsCompatibleUpdateOwner(DelayAnimationEffect obj) => true;

        protected virtual void UpdateCore(DelayAnimationEffect obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            CompareAndUpdate(context, obj.Delay, ref _delay, ref updateOnly);
            UpdateEffect(context, obj, ref updateOnly);

            TimeSpan oldTime = _globalTime;
            bool oldDisableResourceShare = _disableResourceShare;
            bool oldPreferProxy = _preferProxy;
            ProxyPreset oldPreferredPreset = _preferredProxyPreset;
            _globalTime = context.Time;
            _disableResourceShare = context.DisableResourceShare;
            _preferProxy = context.PreferProxy;
            _preferredProxyPreset = context.PreferredProxyPreset;

            // Bump Version when resource-sharing or proxy selection changes too, or RenderNodeCache would replay
            // stale tiles and the new delayed-child CompositionContext would not reach its media resources until an
            // unrelated invalidation (e.g. a time change) happens.
            bool resourceShareChanged = oldDisableResourceShare != _disableResourceShare;
            bool proxyChanged = oldPreferProxy != _preferProxy || oldPreferredPreset != _preferredProxyPreset;
            if (!updateOnly && (oldTime != _globalTime || resourceShareChanged || proxyChanged))
            {
                Version++;
            }
        }

        protected override void PrepareGeneratedResourceCleanupCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                context.Reserve(_effect);
                foreach (FilterEffect.Resource resource in _delayedResources.Values)
                {
                    context.Reserve(resource);
                }
            }

            base.PrepareGeneratedResourceCleanupCore(disposing, context);
        }

        protected override void CleanupGeneratedResourceCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            try
            {
                FilterEffect.Resource? effect = _effect;
                _effect = null;
                FilterEffect.Resource[] delayed = DetachDelayedResources();

                if (disposing)
                {
                    context.DisposeOwned(effect);
                    foreach (FilterEffect.Resource resource in delayed)
                    {
                        context.DisposeOwned(resource);
                    }
                }
            }
            finally
            {
                base.CleanupGeneratedResourceCore(disposing, context);
            }
        }

        private FilterEffect.Resource[] DetachDelayedResources()
        {
            FilterEffect.Resource[] resources = [.. _delayedResources.Values];
            _delayedResources.Clear();
            _delayedResourceOriginal = null;
            _delayedResourcesSnapshot = s_emptyDelayedResources;
            return resources;
        }

        private void PublishDelayedResourcesSnapshot()
        {
            _delayedResourcesSnapshot = _delayedResources.Count == 0
                ? s_emptyDelayedResources
                : new ReadOnlyDictionary<int, FilterEffect.Resource>(
                    new Dictionary<int, FilterEffect.Resource>(_delayedResources));
        }

        private void UpdateEffect(
            CompositionContext context,
            DelayAnimationEffect owner,
            ref bool updateOnly)
        {
            FilterEffect? value = context.Get(owner.Effect);
            FilterEffect.Resource? current = _effect;
            if (current != null && ReferenceEquals(current.GetOriginal(), value))
            {
                int oldVersion = current.Version;
                bool childUpdateOnly = false;
                current.Update(value!, context, ref childUpdateOnly);
                if (!updateOnly && oldVersion != current.Version)
                {
                    Version++;
                    updateOnly = true;
                }

                if (_delayedResources.Count > 0 && !ReferenceEquals(_delayedResourceOriginal, value))
                    RetireAllDelayedResources();
                return;
            }

            FilterEffect.Resource? replacement = value == null
                ? null
                : (FilterEffect.Resource)value.ToResource(context);
            var roots = new FilterEffect.Resource[(current == null ? 0 : 1) + _delayedResources.Count];
            int rootIndex = 0;
            if (current != null)
                roots[rootIndex++] = current;
            foreach (FilterEffect.Resource delayed in _delayedResources.Values)
                roots[rootIndex++] = delayed;

            ExceptionDispatchInfo? cleanupFailure;
            try
            {
                cleanupFailure = RetireOwnedResourceGraphs(roots);
            }
            catch
            {
                try
                {
                    replacement?.Dispose();
                }
                catch
                {
                    // Preserve the reservation failure while reclaiming the unpublished replacement best-effort.
                }

                throw;
            }

            _effect = replacement;
            DetachDelayedResources();
            if (!updateOnly)
            {
                Version++;
                updateOnly = true;
            }

            cleanupFailure?.Throw();
        }

        private void RetireAllDelayedResources()
        {
            FilterEffect.Resource[] roots = [.. _delayedResources.Values];
            ExceptionDispatchInfo? cleanupFailure = RetireOwnedResourceGraphs(roots);
            DetachDelayedResources();
            cleanupFailure?.Throw();
        }
    }
}

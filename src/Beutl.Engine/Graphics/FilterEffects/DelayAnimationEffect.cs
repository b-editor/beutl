using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.DelayAnimationEffect), ResourceType = typeof(Strings))]
[SuppressResourceClassGeneration]
public class DelayAnimationEffect : FilterEffect
{
    public DelayAnimationEffect()
    {
        ScanProperties<DelayAnimationEffect>();
    }

    [Display(Name = nameof(Strings.Delay), ResourceType = typeof(Strings))]
    public IProperty<float> Delay { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(Strings.FilterEffect), ResourceType = typeof(Strings))]
    public IProperty<FilterEffect?> Effect { get; } = Property.Create<FilterEffect?>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Effect == null) return;

        var childEffect = r.Effect.GetOriginal();

        context.CustomEffect(
            (delay: r.Delay, globalTime: r.GlobalTime, childEffect, cache: r.DelayedResources),
            static (data, effectContext) =>
            {
                int targetCount = effectContext.Targets.Count;

                // キャッシュを必要数まで成長
                for (int i = data.cache.Count; i < targetCount; i++)
                {
                    TimeSpan delayedTime = data.globalTime - TimeSpan.FromMilliseconds(data.delay * i);
                    data.cache.Add(data.childEffect.ToResource(new CompositionContext(delayedTime)));
                }

                // 余分なキャッシュを縮小
                while (data.cache.Count > targetCount)
                {
                    data.cache[^1].Dispose();
                    data.cache.RemoveAt(data.cache.Count - 1);
                }

                for (int i = 0; i < targetCount; i++)
                {
                    EffectTarget target = effectContext.Targets[i];
                    if (target.IsEmpty) continue;

                    // 既存Resourceを遅延時刻で更新
                    TimeSpan delayedTime = data.globalTime - TimeSpan.FromMilliseconds(data.delay * i);
                    var delayedContext = new CompositionContext(delayedTime);
                    var updateOnly = false;
                    data.cache[i].Update(data.childEffect, delayedContext, ref updateOnly);

                    if (!data.cache[i].IsEnabled) continue;

                    using var childFEContext = new FilterEffectContext(target.Bounds);
                    data.childEffect.ApplyTo(childFEContext, data.cache[i]);

                    using var singleTargets = new EffectTargets { target.Clone() };
                    using var builder = new SKImageFilterBuilder();
                    using var activator = new FilterEffectActivator(singleTargets, builder);
                    activator.Apply(childFEContext);
                    activator.Flush();

                    if (singleTargets.Count > 0)
                    {
                        target.Dispose();
                        effectContext.Targets[i] = singleTargets[0];

                        for (int j = 1; j < singleTargets.Count; j++)
                        {
                            effectContext.Targets.Insert(i + j, singleTargets[j]);
                        }

                        i += singleTargets.Count - 1;
                        targetCount = effectContext.Targets.Count;
                    }
                }
            });
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
        private readonly List<FilterEffect.Resource> _delayedResources = [];

        public float Delay => _delay;

        public FilterEffect.Resource? Effect => _effect;

        public TimeSpan GlobalTime => _globalTime;

        public List<FilterEffect.Resource> DelayedResources => _delayedResources;

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            var typed = (DelayAnimationEffect)obj;
            CompareAndUpdate(context, typed.Delay, ref _delay, ref updateOnly);
            CompareAndUpdateObject(context, typed.Effect, ref _effect, ref updateOnly);

            TimeSpan oldTime = _globalTime;
            _globalTime = context.Time;
            if (!updateOnly && oldTime != _globalTime)
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

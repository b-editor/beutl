using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

[SuppressResourceClassGeneration]
public sealed class FilterEffectPresenter : FilterEffect
{
    public FilterEffectPresenter()
    {
        ScanProperties<FilterEffectPresenter>();
    }

    public IProperty<Reference<FilterEffect>> Target { get; } = Property.Create<Reference<FilterEffect>>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        r.Target?.GetOriginal().ApplyTo(context, r.Target);
    }

    public override FilterEffect.Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        private Reference<FilterEffect> _targetRef;

        public FilterEffect.Resource? Target { get; set; }

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var presenter = (FilterEffectPresenter)obj;
            var newTarget = context.Get(presenter.Target);

            if (_targetRef != newTarget)
            {
                _targetRef = newTarget;
                Target?.Dispose();
                Target = newTarget.Value?.ToResource(context);
                if (!updateOnly)
                {
                    Version++;
                    updateOnly = true;
                }
            }
            else if (Target != null && newTarget.Value != null)
            {
                var oldVersion = Target.Version;
                bool _ = false;
                Target.Update(newTarget.Value, context, ref _);
                if (!updateOnly && oldVersion != Target.Version)
                {
                    Version++;
                    updateOnly = true;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Target?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

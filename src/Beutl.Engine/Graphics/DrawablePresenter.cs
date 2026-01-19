using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics;

[SuppressResourceClassGeneration]
public sealed class DrawablePresenter : Drawable
{
    public DrawablePresenter()
    {
        ScanProperties<DrawablePresenter>();
    }

    public IProperty<Reference<Drawable>> Target { get; } = Property.Create<Reference<Drawable>>();

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        r.Target?.GetOriginal().Render(context, r.Target);
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        return r.Target?.GetOriginal().MeasureInternal(availableSize, r.Target) ?? Size.Empty;
    }

    public override Drawable.Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : Drawable.Resource
    {
        private Reference<Drawable> _targetRef;

        public Drawable.Resource? Target { get; set; }

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var presenter = (DrawablePresenter)obj;
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

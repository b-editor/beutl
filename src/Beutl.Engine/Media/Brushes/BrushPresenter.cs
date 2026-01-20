using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Media;

[SuppressResourceClassGeneration]
public sealed class BrushPresenter : Brush, IPresenter<Brush>
{
    public BrushPresenter()
    {
        ScanProperties<BrushPresenter>();
    }

    public IProperty<Reference<Brush>> Target { get; } = Property.Create<Reference<Brush>>();

    public override Brush.Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : Brush.Resource
    {
        private Reference<Brush> _targetRef;

        public Brush.Resource? Target { get; set; }

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var presenter = (BrushPresenter)obj;
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

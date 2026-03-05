using Beutl.Composition;
using Beutl.Engine;

namespace Beutl.Graphics.Transformation;

[SuppressResourceClassGeneration]
[PresenterType(typeof(TransformPresenter))]
public abstract class Transform : EngineObject
{
    public abstract Matrix CreateMatrix(CompositionContext context);

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : EngineObject.Resource
    {
        public Matrix Matrix { get; set; } = Matrix.Identity;

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var transform = (Transform)obj;

            var oldMatrix = Matrix;
            Matrix = transform.CreateMatrix(context);
            if (updateOnly) return;

            if (oldMatrix != Matrix)
            {
                updateOnly = true;
                Version++;
            }
        }
    }
}

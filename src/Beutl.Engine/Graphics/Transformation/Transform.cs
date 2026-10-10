using Beutl.Composition;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.Graphics.Transformation;

public sealed partial class FallbackTransform : Transform, IFallback;

[SuppressResourceClassGeneration]
[FallbackType(typeof(FallbackTransform))]
[PresenterType(typeof(TransformPresenter))]
public abstract class Transform : EngineObject
{
    public abstract Matrix CreateMatrix(CompositionContext context);

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    public new sealed class Resource : EngineObject.Resource
    {
        public Matrix Matrix { get; set; } = Matrix.Identity;

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);
            var transform = (Transform)obj;

            var oldMatrix = Matrix;
            Matrix = transform.CreateMatrix(context);
            if (versionBumped) return;

            if (oldMatrix != Matrix)
            {
                versionBumped = true;
                Version++;
            }
        }
    }
}

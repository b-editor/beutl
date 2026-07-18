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
                // Preserve the acquisition failure while reclaiming the partially initialized resource.
            }

            throw;
        }
    }

    public new sealed class Resource : EngineObject.Resource
    {
        private Matrix _matrix = Matrix.Identity;

        public Matrix Matrix
        {
            get => ReadGeneratedResourceState(ref _matrix);
            set => WriteGeneratedResourceState(ref _matrix, value);
        }

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            var transform = (Transform)obj;
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(transform);
            base.Update(obj, context, ref updateOnly);

            Matrix oldMatrix = _matrix;
            _matrix = transform.CreateMatrix(context);
            if (updateOnly) return;

            if (oldMatrix != _matrix)
            {
                updateOnly = true;
                Version++;
            }
        }
    }
}

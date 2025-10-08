using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Transformation;

[SuppressResourceClassGeneration]
public abstract class Transform : EngineObject
{
    public abstract Matrix CreateMatrix(RenderContext context);

    public override Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : EngineObject.Resource
    {
        public Matrix Matrix { get; private set; } = Matrix.Identity;

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
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

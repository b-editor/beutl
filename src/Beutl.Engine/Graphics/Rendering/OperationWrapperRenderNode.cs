using System.Runtime.ExceptionServices;
using Beutl.Media.Source;

namespace Beutl.Graphics.Rendering;

public class OperationWrapperRenderNode : RenderNode
{
    private Ref<RenderNodeOperation>[] _operations = [];

    public void SetOperations(RenderNodeOperation[] operations)
    {
        var refs = new Ref<RenderNodeOperation>[operations.Length];
        for (int i = 0; i < operations.Length; i++)
            refs[i] = Ref<RenderNodeOperation>.Create(operations[i]);

        Ref<RenderNodeOperation>[] previous = _operations;
        _operations = refs;
        HasChanges = true;

        DisposeReferences(previous);
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        var result = new RenderNodeOperation[_operations.Length];
        for (int i = 0; i < _operations.Length; i++)
            result[i] = new RefCountedProxy(_operations[i].Clone());

        return result;
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        if (disposing)
        {
            Ref<RenderNodeOperation>[] previous = _operations;
            _operations = [];
            DisposeReferences(previous);
        }
    }

    private static void DisposeReferences(Ref<RenderNodeOperation>[] references)
    {
        Exception? failure = null;
        foreach (Ref<RenderNodeOperation> reference in references)
        {
            try
            {
                reference.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class RefCountedProxy(Ref<RenderNodeOperation> inner) : RenderNodeOperation
    {
        public override Rect Bounds => inner.Value.Bounds;

        // Forward the wrapped op's supply density verbatim.
        public override EffectiveScale EffectiveScale => inner.Value.EffectiveScale;

        public override void Render(ImmediateCanvas canvas) => inner.Value.Render(canvas);

        public override bool HitTest(Point point) => inner.Value.HitTest(point);

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
        }
    }
}

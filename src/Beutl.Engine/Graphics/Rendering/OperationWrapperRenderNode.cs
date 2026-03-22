using Beutl.Media.Source;

namespace Beutl.Graphics.Rendering;

public class OperationWrapperRenderNode : RenderNode
{
    private Ref<RenderNodeOperation>[] _operations = [];

    public void SetOperations(RenderNodeOperation[] operations)
    {
        foreach (var r in _operations)
            r.Dispose();

        var refs = new Ref<RenderNodeOperation>[operations.Length];
        for (int i = 0; i < operations.Length; i++)
            refs[i] = Ref<RenderNodeOperation>.Create(operations[i]);

        _operations = refs;
        HasChanges = true;
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
            foreach (var r in _operations)
                r.Dispose();
            _operations = [];
        }
    }

    private sealed class RefCountedProxy(Ref<RenderNodeOperation> inner) : RenderNodeOperation
    {
        public override Rect Bounds => inner.Value.Bounds;

        public override void Render(ImmediateCanvas canvas) => inner.Value.Render(canvas);

        public override bool HitTest(Point point) => inner.Value.HitTest(point);

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
        }
    }
}

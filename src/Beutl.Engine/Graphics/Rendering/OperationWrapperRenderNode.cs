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

        // The proxy applies no transform, so it forwards the wrapped op's supply density verbatim (FR-016/FR-036).
        // Otherwise the struct default (Unbounded) leaks at the node-graph boundary: a filter fed a concrete-density
        // input (a transformed bitmap At(0.5), a cached tile) would lose that density and rasterize at s_out.
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

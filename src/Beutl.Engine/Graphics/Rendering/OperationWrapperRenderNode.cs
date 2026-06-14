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

        // The proxy applies no geometric transform, so it must forward the wrapped op's supply density
        // verbatim (FR-016/FR-036). Without this the struct default (Unbounded) would be reported, and a
        // node-graph filter fed a concrete-density input (e.g. a transformed bitmap At(0.5) or a cached tile)
        // would lose that density at the node-graph boundary and rasterize at s_out instead of the true supply.
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

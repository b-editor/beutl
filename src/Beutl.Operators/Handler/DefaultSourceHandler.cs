using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Animation;

using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl.Operators.Handler;

public sealed class DefaultSourceHandler : SourceOperator, ISourceHandler
{
    private Layer? _layer;

    public void Handle(IList<Renderable> renderables, IClock clock)
    {
        if (_layer != null)
        {
            RenderLayerSpan span = _layer.Span;

            span.Value.Replace(renderables);

            foreach (Renderable item in span.Value.GetMarshal().Value)
            {
                item.ApplyStyling(clock);
                item.ApplyAnimations(clock);
                item.IsVisible = _layer.IsEnabled;
                while (!item.EndBatchUpdate())
                {
                }
            }

            renderables.Clear();
        }
    }

    public override void Exit()
    {
        base.Exit();

        if (_layer != null)
        {
            RenderLayerSpan span = _layer.Span;
            span.Value.Clear();
        }
    }

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        _layer = args.Parent as Layer;
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);

        if (_layer != null)
        {
            RenderLayerSpan span = _layer.Span;
            span.Value.Clear();
            _layer = null;
        }
    }
}

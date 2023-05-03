using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Graphics.Filters;

namespace Beutl.NodeTree.Nodes.Filters;

public sealed class ImageFilterNodeEvaluationState
{
    public ImageFilterNodeEvaluationState(IImageFilter? created)
    {
        Created = created;
    }

    public IImageFilter? Created { get; set; }

    public ComposedImageFilter? AddtionalState { get; set; }
}

public abstract class ImageFilterNode : Node
{
    public ImageFilterNode()
    {
        OutputSocket = AsOutput<IImageFilter?>("ImageFilter");
        InputSocket = AsInput<IImageFilter?>("ImageFilter");
    }

    protected OutputSocket<IImageFilter?> OutputSocket { get; }

    protected InputSocket<IImageFilter?> InputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        IImageFilter? input = InputSocket.Value;
        if (context.State is not ImageFilterNodeEvaluationState state)
        {
            context.State = state = new ImageFilterNodeEvaluationState(null);
        }

        EvaluateCore(state.Created);
        if (input != null)
        {
            state.AddtionalState ??= new ComposedImageFilter();
            state.AddtionalState.Outer = state.Created;
            state.AddtionalState.Inner = input;
            OutputSocket.Value = state.AddtionalState;
        }
        else
        {
            OutputSocket.Value = state.Created;
        }
    }

    protected abstract void EvaluateCore(IImageFilter? state);
}

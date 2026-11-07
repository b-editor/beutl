using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>A node that can say why the picture made from its image output failed.</summary>
internal interface IShaderPictureOwner
{
    void ReportPictureFailure(string? message);
}

/// <summary>What a node's error monitor shows: its own problem first, then why the picture made from it failed.</summary>
/// <remarks>Kept apart because each is cleared on its own schedule, and sharing one value would make it flicker.</remarks>
internal sealed class ShaderErrorReports(NodeMonitor<string?> monitor)
{
    private string? _own;
    private string? _picture;

    public void ReportOwn(string? message)
    {
        _own = message;
        Show();
    }

    public void ReportPicture(string? message)
    {
        _picture = message;
        Show();
    }

    private void Show()
    {
        string? shown = _own ?? _picture;
        if (monitor.Value != shown) monitor.Value = shown;
    }
}

/// <summary>The picture form of an image wire: one recording of what the wire reaches, run over the pictures it reads.</summary>
/// <remarks>This is the boundary of a recording. A wire can be followed into any number of passes, but each output
/// that ends in a picture records and runs them once more.</remarks>
internal sealed class ShaderGraphPicture : IWirePicture
{
    private readonly ShaderGraphEffect _effect = new();
    private ShaderGraphEffect.Resource? _effectResource;
    private ShaderGraphRenderNode? _renderNode;
    private string? _recordingError;
    private string? _executionError;

    public RenderNode? RenderNode => _renderNode;

    public bool HasFailed => _recordingError != null || _executionError != null;

    public void Update(ShaderGraphValue<ShaderImage> image, GraphNode owner, INodeMember output, GraphCompositionContext context)
    {
        IShaderPictureOwner? explainer = FindExplainer(owner, output);
        ShaderGraphDescription? description = null;
        RenderNode? source = null;
        IReadOnlyList<RenderNode> externals = [];
        _recordingError = null;
        try
        {
            description = ShaderGraphRecordingContext.Build(image, out source, out externals);
            if (!IsRecorded(description)) ShaderGraphPreflight.Validate(description);
        }
        catch (Exception ex)
        {
            description = null;
            source = null;
            externals = [];
            _recordingError = ex.Message;
        }

        // What a render refused stands until a graph runs, which takes the renderer to find out.
        if (description == null) _executionError = null;
        explainer?.ReportPictureFailure(_recordingError ?? _executionError);

        // The same graph again keeps what the renderer has cached for it.
        if (description == null || !IsRecorded(description))
        {
            description?.ExecutionObserver = failure => OnExecuted(explainer, failure);
            _effect.Description.Value = description;
        }

        bool changed = false;
        ResourceReconciler.ReconcileResource(context, _effect, ref _effectResource, ref changed);
        _renderNode ??= new ShaderGraphRenderNode(_effectResource!);
        _renderNode.Update(_effectResource!);
        _renderNode.SetGraph(_effect.Description.Value, _recordingError != null, source, externals);
    }

    private bool IsRecorded(ShaderGraphDescription description)
        => _effect.Description.Value?.OutputIdentity.Equals(description.OutputIdentity) == true;

    private void OnExecuted(IShaderPictureOwner? explainer, Exception? failure)
    {
        _executionError = failure?.Message;
        explainer?.ReportPictureFailure(_recordingError ?? _executionError);
    }

    // The node whose output became the picture, or, through a plain group that shows nothing of its own, the node inside
    // whose output reaches the group's.
    private static IShaderPictureOwner? FindExplainer(GraphNode node, INodeMember output)
    {
        while (node is not IShaderPictureOwner)
        {
            if (node is not GroupNode group || group.FindInnerSource(output) is not { } inner) return null;
            (node, output) = inner;
        }

        return (IShaderPictureOwner)node;
    }

    public void Dispose()
    {
        _renderNode?.Dispose();
        _renderNode = null;
        _effectResource?.Dispose();
        _effectResource = null;
    }
}

/// <summary>Runs a recorded graph over the picture it treats as its source, with the other pictures it reads.</summary>
/// <remarks>Pictures are children, so that a change in one is seen and each is recorded where the graph sits: in an
/// effect, reading what the effect was given.</remarks>
internal sealed class ShaderGraphRenderNode(FilterEffect.Resource effect) : FilterEffectRenderNode(effect)
{
    private ShaderGraphDescription? _description;
    private bool _failed;
    private RenderNode? _source;
    private IReadOnlyList<RenderNode> _externals = [];

    internal ShaderGraphDescription? Description => _description;

    public void SetGraph(ShaderGraphDescription? description, bool failed, RenderNode? source, IReadOnlyList<RenderNode> externals)
    {
        bool changed = !ReferenceEquals(_description, description) || _failed != failed;
        _description = description;
        _failed = failed;
        if (!ReferenceEquals(_source, source) || !SamePictures(_externals, externals))
        {
            ReplacePictures(source, externals);
            changed = true;
        }

        foreach (RenderNode child in Children)
        {
            changed |= child switch
            {
                PictureInput input => input.Refresh(),
                ReferencesChildRenderNode reference => reference.Update(reference.Child),
                _ => false
            };
        }

        if (changed) MarkChanged();
    }

    private static bool SamePictures(IReadOnlyList<RenderNode> current, IReadOnlyList<RenderNode> next)
    {
        if (current.Count != next.Count) return false;
        for (int i = 0; i < current.Count; i++)
        {
            if (!ReferenceEquals(current[i], next[i])) return false;
        }

        return true;
    }

    // The source stays one child when nothing else is read, for the same path a script effect takes.
    private void ReplacePictures(RenderNode? source, IReadOnlyList<RenderNode> externals)
    {
        RenderNode[] children = [.. Children];
        RemoveRange(0, children.Length);
        foreach (RenderNode child in children) child.Dispose();

        _source = source;
        // The recording that made the list has finished with it.
        _externals = externals;
        if (source != null)
            AddChild(externals.Count == 0 ? new ReferencesChildRenderNode(source) : new PictureInput(source, single: false));
        foreach (RenderNode external in externals)
            AddChild(new PictureInput(external, single: true));
    }

    public override void Process(RenderNodeContext context)
    {
        // A failure publishes nothing, so that a mistake does not pass for a working result.
        if (_failed) return;

        Rect? frame = context.TargetDomain;
        bool generates = _source == null && frame.HasValue;
        if (_description is not { } description || (!generates && _externals.Count == 0))
        {
            base.Process(context);
            return;
        }

        IReadOnlyList<RenderFragmentHandle> inputs = context.Inputs;
        int sourceCount = inputs.Count - _externals.Count;
        int firstExternal = Math.Max(sourceCount, 0);
        var externals = new RenderFragmentHandle[inputs.Count - firstExternal];
        for (int i = 0; i < externals.Length; i++)
            externals[i] = inputs[firstExternal + i];

        if (generates)
        {
            context.Publish(context.ShaderGraph(PictureInput.Empty(context, frame!.Value), description, externals));
        }
        else
        {
            for (int i = 0; i < sourceCount; i++)
                context.Publish(context.ShaderGraph(inputs[i], description, externals));
        }
    }

    // Keeps pictures apart among a node's inputs, which arrive as one list: an external image is always one value.
    private sealed class PictureInput : ContainerRenderNode
    {
        private readonly ReferencesChildRenderNode _picture;
        private readonly bool _single;

        public PictureInput(RenderNode picture, bool single)
        {
            _picture = new ReferencesChildRenderNode(picture);
            _single = single;
            AddChild(_picture);
        }

        public bool Refresh()
        {
            if (_picture.Update(_picture.Child)) MarkChanged();
            return HasChanges;
        }

        public override void Process(RenderNodeContext context)
        {
            IReadOnlyList<RenderFragmentHandle> inputs = context.Inputs;
            Rect domain = context.TryCalculateFiniteIsolationDomain(out Rect isolation) && isolation is { Width: > 0, Height: > 0 }
                ? isolation
                : context.TargetDomain ?? new Rect(0, 0, 1, 1);
            if (!_single)
            {
                foreach (RenderFragmentHandle input in inputs)
                    context.Publish(input.CanBeUsedAsValueInput ? input : context.Layer([input], domain));
            }
            else if (inputs is [{ CanBeUsedAsValueInput: true } value])
            {
                context.Publish(value);
            }
            else
            {
                context.Publish(inputs.Count == 0 ? Empty(context, domain) : context.Layer(inputs, domain));
            }
        }

        public static RenderFragmentHandle Empty(RenderNodeContext context, Rect bounds)
            => context.PaintedSource(0, static (_, _, _, _) => { }, null, null, bounds,
                RenderHitTestContract.OutputBounds, RenderScaleContract.Vector);
    }
}

// The renderer takes a graph through a filter effect; a picture owns this one and it is never saved.
internal sealed partial class ShaderGraphEffect : FilterEffect
{
    public ShaderGraphEffect()
    {
        ScanProperties<ShaderGraphEffect>();
    }

    [NotAutoSerialized]
    public IProperty<ShaderGraphDescription?> Description { get; } = new ValueOnlyProperty<ShaderGraphDescription?>(null);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (Description is { } description)
                context.ShaderGraph(description);
        }
    }
}

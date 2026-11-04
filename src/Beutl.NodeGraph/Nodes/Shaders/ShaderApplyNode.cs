using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>Gives the incoming picture to the shader wires as their source, and passes on the image they end in.</summary>
public partial class ShaderApplyNode : ConfigureNode
{
    public ShaderApplyNode()
    {
        Image = AddInput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
        Color = AddInput<ColorEncoding>("Color", ShaderPortDisplays.Get("ColorEncoding"));
        Error = AddTextMonitor("Error", NodePortDisplays.Error);
    }

    public InputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public InputPort<ColorEncoding> Color { get; }

    public NodeMonitor<string?> Error { get; }

    public partial class Resource
    {
        private readonly ShaderApplyEffect _effect = new();
        private ShaderApplyEffect.Resource? _effectResource;
        private string? _executionError;

        internal ShaderApplyEffect Effect => _effect;

        protected override void UpdateCore(GraphCompositionContext context)
        {
            ShaderApplyNode node = RequireOriginal();
            ShaderGraphDescription? description = null;
            IReadOnlyList<RenderNode> externalImages = [];
            string? error = null;
            if (Image is { } image)
            {
                try
                {
                    description = ShaderGraphRecordingContext.Build(image, Color, out externalImages);
                    if (!IsRecorded(description)) ShaderGraphPreflight.Validate(description);
                }
                catch (Exception ex)
                {
                    description = null;
                    externalImages = [];
                    error = ex.Message;
                }
            }

            // What a render refused stands until a graph runs, which takes the renderer to find out.
            if (description == null) _executionError = null;
            string? shown = error ?? _executionError;
            if (node.Error.Value != shown) node.Error.Value = shown;

            // The same graph again keeps what the renderer has cached for it.
            if (description == null || !IsRecorded(description))
            {
                description?.ExecutionObserver = failure => OnExecuted(node, failure);
                _effect.Description.Value = description;
            }

            if (_effectResource == null)
            {
                _effectResource = _effect.ToResource(context);
                OutputPort = new ShaderApplyRenderNode(_effectResource);
            }
            else
            {
                bool changed = false;
                _effectResource.Reconcile(_effect, context, ref changed);
            }

            var output = (ShaderApplyRenderNode)OutputPort!;
            output.Update(_effectResource);
            output.SetGraph(_effect.Description.Value, error != null, node.InputPort.Connections.Count == 0, externalImages);
        }

        private bool IsRecorded(ShaderGraphDescription description)
            => _effect.Description.Value?.OutputIdentity.Equals(description.OutputIdentity) == true;

        private void OnExecuted(ShaderApplyNode node, Exception? failure)
        {
            _executionError = failure?.Message;
            if (node.Error.Value != _executionError) node.Error.Value = _executionError;
        }

        partial void PostDispose(bool disposing)
        {
            OutputPort?.Dispose();
            OutputPort = null;
            _effectResource?.Dispose();
            _effectResource = null;
        }
    }
}

/// <summary>Runs a recorded graph over the picture coming in, or the pictures and frame it needs beyond that.</summary>
internal sealed class ShaderApplyRenderNode(FilterEffect.Resource effect) : FilterEffectRenderNode(effect)
{
    private ShaderGraphDescription? _description;
    private bool _failed;
    private bool _hasNoInput;
    private RenderNode[] _externalImages = [];

    public void SetGraph(ShaderGraphDescription? description, bool failed, bool hasNoInput, IReadOnlyList<RenderNode> externalImages)
    {
        if (!ReferenceEquals(_description, description) || _failed != failed || _hasNoInput != hasNoInput
            || !_externalImages.AsSpan().SequenceEqual(externalImages.ToArray())
            || externalImages.Any(static image => image.HasChanges))
            MarkChanged();
        _description = description;
        _failed = failed;
        _hasNoInput = hasNoInput;
        _externalImages = [.. externalImages];
    }

    public override void Process(RenderNodeContext context)
    {
        // A failure publishes nothing, so that a mistake does not pass for a working result.
        if (_failed) return;

        Rect? frame = context.TargetDomain;
        bool generates = _hasNoInput && frame.HasValue;
        if (_description is not { } description || (!generates && _externalImages.Length == 0))
        {
            base.Process(context);
            return;
        }

        IReadOnlyList<RenderFragmentHandle> inputs = generates ? [Empty(context, frame!.Value)] : context.Inputs;
        Rect domain = context.TryCalculateFiniteIsolationDomain(out Rect isolation) && isolation is { Width: > 0, Height: > 0 }
            ? isolation
            : frame ?? new Rect(0, 0, 1, 1);
        IReadOnlyList<RenderFragmentHandle>[] pictures = [.. _externalImages
            .Select(image => image.IsDisposed ? [] : context.RecordSubtree(image))];
        foreach (RenderFragmentHandle input in inputs)
        {
            RenderFragmentHandle[] externals = [.. pictures.Select(picture => AsSingleValue(context, picture, domain))];
            RenderFragmentHandle source = input.CanBeUsedAsValueInput ? input : context.Layer([input], domain);
            context.Publish(context.ShaderGraph(source, description, externals));
        }
    }

    // A picture of nothing still has to be a value, and one with several parts a single value.
    private static RenderFragmentHandle AsSingleValue(RenderNodeContext context, IReadOnlyList<RenderFragmentHandle> picture, Rect domain)
    {
        if (picture is [{ CanBeUsedAsValueInput: true } single]) return single;
        return picture.Count == 0 ? Empty(context, domain) : context.Layer(picture, domain);
    }

    private static RenderFragmentHandle Empty(RenderNodeContext context, Rect bounds)
        => context.PaintedSource(0, static (_, _, _, _) => { }, null, null, bounds,
            RenderHitTestContract.OutputBounds, RenderScaleContract.Vector);
}

// The renderer takes a graph through a filter effect; a node owns this one and it is never saved.
internal sealed partial class ShaderApplyEffect : FilterEffect
{
    public ShaderApplyEffect()
    {
        ScanProperties<ShaderApplyEffect>();
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

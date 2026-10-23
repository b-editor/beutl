using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

/// <summary>
/// Draws another scene at the absolute moment its time input names, as the video node draws a clip. Its output is an ordinary picture: it can be transformed, filtered
/// or handed to an AI node, which captures it as it looks when the AI nodes run.
/// </summary>
public partial class SceneNode : GraphNode
{
    public SceneNode()
    {
        ScanProperties<SceneNode>();
        HideProperty(Object);
        Output = AddOutput<DrawableRenderNode?>("Output", NodePortDisplays.Output);
        Object.Value = new SceneDrawable();
        Object.Value.AlignmentX.Value = Media.AlignmentX.Left;
        Object.Value.AlignmentY.Value = Media.AlignmentY.Top;
        PinTimeToReferencedScene();
        AddInput(Object.Value, Object.Value.ReferencedScene);
        Time = AddInput<MediaTime>("Time", NodePortDisplays.Time);
        ErrorMonitor = AddTextMonitor("Error", NodePortDisplays.Error);
    }

    public OutputPort<DrawableRenderNode?> Output { get; }

    /// <summary>The moment of the referenced scene to draw; animate it to play the scene.</summary>
    public InputPort<MediaTime> Time { get; }

    public NodeMonitor<string?> ErrorMonitor { get; }

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<SceneDrawable> Object { get; } = new ValueOnlyProperty<SceneDrawable>(null!) { AffectsRender = false };

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("Object", Object.Value);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        context.Populate("Object", Object.Value);
        PinTimeToReferencedScene();
    }

    /// <summary>
    /// Makes <see cref="Time"/> the referenced scene's own time. A nested object otherwise takes
    /// the time range of the element hosting the graph, and the scene drawable subtracts that
    /// start; anchored at zero, the time it is given is the time it draws, whatever the element.
    /// </summary>
    private void PinTimeToReferencedScene()
    {
        Object.Value.IsTimeAnchor.Value = true;
        Object.Value.TimeRange.Value = new Media.TimeRange(MediaTime.Zero, MediaTime.FromSeconds(3650 * 86400));
    }

    public partial class Resource
    {
        private SceneDrawable.Resource? _sceneResource;

        /// <summary>The scene's resource as last updated, for tests.</summary>
        internal SceneDrawable.Resource? SceneResource => _sceneResource;

        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            // The referenced scene is evaluated at the absolute time Time names, with this
            // render's settings, whether Time is typed or comes from another node.
            var sceneContext = new CompositionContext(Time)
            {
                DisableResourceShare = context.DisableResourceShare,
                PreferProxy = context.PreferProxy,
                PreferredProxyPreset = context.PreferredProxyPreset,
                TargetDomain = context.TargetDomain,
            };

            try
            {
                if (_sceneResource is null)
                {
                    _sceneResource = (SceneDrawable.Resource)node.Object.Value.ToResource(sceneContext);
                }
                else
                {
                    bool versionBumped = false;
                    _sceneResource.Reconcile(node.Object.Value, sceneContext, ref versionBumped);
                }
            }
            catch (InvalidOperationException)
            {
                // A scene that contains this graph would draw itself forever; the scene
                // element refuses the same way, and here it must not stop the whole render.
                ReleaseOutput();
                node.ErrorMonitor.Value = NodeGraphStrings.Scene_CircularReference;
                return;
            }

            node.ErrorMonitor.Value = null;
            DrawableRenderNode? output = Output;
            if (output == null || output.IsDisposed)
                output = new DrawableRenderNode(_sceneResource);
            else
                output.Update(_sceneResource);

            Size size = node.Object.Value.MeasureInternal(Size.Infinity, _sceneResource);
            using (var gc2d = new GraphicsContext2D(output, size))
            {
                node.Object.Value.Render(gc2d, _sceneResource);
            }

            Output = output;
        }

        private void ReleaseOutput()
        {
            Output?.Dispose();
            Output = null;
            _sceneResource?.Dispose();
            _sceneResource = null;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                ReleaseOutput();
        }
    }
}

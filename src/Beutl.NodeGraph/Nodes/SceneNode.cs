using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

/// <summary>
/// Draws another scene at the moment its time input names, as the video node draws a clip. Its output is an ordinary picture: it can be transformed, filtered
/// or handed to an AI node, which captures it as it looks when the AI nodes run.
/// </summary>
public partial class SceneNode : GraphNode
{
    public static readonly CoreProperty<SceneDrawable> ObjectProperty;

    static SceneNode()
    {
        ObjectProperty = ConfigureProperty<SceneDrawable, SceneNode>(nameof(Object))
            .Accessor(o => o.Object, (o, v) => o.Object = v)
            .Register();

        Hierarchy<SceneNode>(ObjectProperty);
    }

    public SceneNode()
    {
        Output = AddOutput<DrawableRenderNode?>("Output", NodePortDisplays.Output);
        Object = new SceneDrawable();
        Object.AlignmentX.CurrentValue = Media.AlignmentX.Left;
        Object.AlignmentY.CurrentValue = Media.AlignmentY.Top;
        AddInput(Object, Object.ReferencedScene);
        Time = AddInput<TimeSpan>("Time", NodePortDisplays.Time);
        ErrorMonitor = AddTextMonitor("Error", NodePortDisplays.Error);
    }

    public OutputPort<DrawableRenderNode?> Output { get; }

    /// <summary>The moment of the referenced scene to draw; animate it to play the scene.</summary>
    public InputPort<TimeSpan> Time { get; }

    public NodeMonitor<string?> ErrorMonitor { get; }

    [NotAutoSerialized]
    public SceneDrawable Object
    {
        get;
        set => SetAndRaise(ObjectProperty, ref field, value);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("Object", Object);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        context.Populate("Object", Object);
    }

    public partial class Resource
    {
        private SceneDrawable.Resource? _sceneResource;

        internal SceneDrawable.Resource? SceneResource => _sceneResource;

        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            // The referenced scene is evaluated at its own time, with this render's settings.
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
                    _sceneResource = (SceneDrawable.Resource)node.Object.ToResource(sceneContext);
                }
                else
                {
                    bool updateOnly = false;
                    _sceneResource.Update(node.Object, sceneContext, ref updateOnly);
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

            Size size = node.Object.MeasureInternal(Size.Infinity, _sceneResource);
            using (var gc2d = new GraphicsContext2D(output, size))
            {
                node.Object.Render(gc2d, _sceneResource);
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

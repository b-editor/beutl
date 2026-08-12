using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Runtime.ExceptionServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.ProjectSystem;

[Display(Name = nameof(GraphicsStrings.SceneDrawable), ResourceType = typeof(GraphicsStrings))]
public sealed partial class SceneDrawable : Drawable
{
    public SceneDrawable()
    {
        ScanProperties<SceneDrawable>();
    }

    [Display(Name = nameof(GraphicsStrings.SceneDrawable_ReferencedScene), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Scene?> ReferencedScene { get; } = Property.Create<Scene?>();

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.ReferencedScene != null)
        {
            return new Size(r.ReferencedScene.FrameSize.Width, r.ReferencedScene.FrameSize.Height);
        }

        return Size.Empty;
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        var parameters = new SceneBitmapParameters(r, context.OutputScale);
        context.DrawNode(
            parameters,
            static parameters => SceneBitmapRenderNode.Create(parameters),
            static (node, parameters) => node.Update(parameters));
    }

    private readonly record struct SceneBitmapParameters(Resource Resource, float OutputScale);

    private readonly struct CapturedCompositionFrame(CompositionFrame frame)
    {
        public readonly ImmutableArray<(EngineObject.Resource Resource, int Version)> Objects = [.. frame.Objects.Select(r => (r, r.Version))];
        public readonly PixelSize Size = frame.Size;

        public bool IsSame(CompositionFrame frame)
        {
            if (Size != frame.Size)
                return false;

            if (Objects.Length != frame.Objects.Length)
                return false;

            for (int i = 0; i < Objects.Length; i++)
            {
                var (resource, version) = Objects[i];

                var otherResource = frame.Objects[i];
                if (resource != otherResource || version != otherResource.Version)
                    return false;
            }

            return true;
        }
    }

    public partial class Resource
    {
        private static readonly AsyncLocal<HashSet<Scene>?> s_evaluatingScenes = new();
        private SceneCompositor? _compositor;
        private TimeSpan _start;

        public CompositionFrame? Frame { get; set; }

        private static bool Enter(Scene scene)
        {
            var set = s_evaluatingScenes.Value ??= new(ReferenceEqualityComparer.Instance);
            return set.Add(scene);
        }

        private static void Exit(Scene scene)
        {
            s_evaluatingScenes.Value?.Remove(scene);
        }

        partial void PostUpdate(SceneDrawable obj, CompositionContext context)
        {
            bool changed = false;
            bool forceOriginalSource = !context.PreferProxy;
            if (_start != obj.Start)
            {
                _start = obj.Start;
                changed = true;
            }

            if (_compositor?.Scene != ReferencedScene
                || _compositor?.DisableResourceShare != context.DisableResourceShare
                || _compositor?.ForceOriginalSource != forceOriginalSource)
            {
                _compositor?.Dispose();
                _compositor = null;
            }

            if (ReferencedScene != null && _compositor == null)
            {
                _compositor = new SceneCompositor(ReferencedScene)
                {
                    DisableResourceShare = context.DisableResourceShare,
                    ForceOriginalSource = forceOriginalSource,
                };
            }

            if (ReferencedScene != null && !Enter(ReferencedScene))
            {
                throw new InvalidOperationException("A circular reference was detected.");
            }

            try
            {
                CapturedCompositionFrame? oldFrame = Frame != null ? new CapturedCompositionFrame(Frame.Value) : null;
                Frame = _compositor?.EvaluateGraphics(context.Time - obj.Start);

                if (oldFrame.HasValue && Frame.HasValue)
                {
                    changed |= !oldFrame.Value.IsSame(Frame.Value);
                }
                else if (oldFrame.HasValue != Frame.HasValue)
                {
                    changed = true;
                }

                if (changed)
                {
                    Version++;
                }
            }
            finally
            {
                if (ReferencedScene != null)
                    Exit(ReferencedScene);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _compositor?.Dispose();
                _compositor = null;
                Frame = null;
            }
        }
    }

    private class SceneBitmapRenderNode : ContainerRenderNode
    {
        private float _outputScale;
        private PixelSize _frameSize;

        public (Resource Resource, int Version)? Scene { get; private set; }

        public static SceneBitmapRenderNode Create(SceneBitmapParameters parameters)
        {
            var node = new SceneBitmapRenderNode();
            try
            {
                node.Update(parameters);
                return node;
            }
            catch
            {
                node.Dispose();
                throw;
            }
        }

        public bool Update(SceneBitmapParameters parameters)
        {
            Resource resource = parameters.Resource;
            float outputScale = parameters.OutputScale;
            bool sceneChanged = !resource.Compare(Scene);
            bool scaleChanged = _outputScale != outputScale;
            if (!sceneChanged && !scaleChanged)
                return false;

            CompositionFrame? frame = resource.Frame;
            PixelSize frameSize = frame?.Size ?? default;
            bool rebuildAll = scaleChanged || frameSize != _frameSize;
            ReconcileChildren(frame, outputScale, rebuildAll);

            Scene = resource.Capture();
            _outputScale = outputScale;
            _frameSize = frameSize;
            HasChanges = true;
            return true;
        }

        private void ReconcileChildren(
            CompositionFrame? frame,
            float outputScale,
            bool rebuildAll)
        {
            int childIndex = 0;
            if (frame is { } currentFrame)
            {
                Size canvasSize = currentFrame.Size.ToSize(1);
                foreach (EngineObject.Resource item in currentFrame.Objects)
                {
                    if (item is not Drawable.Resource drawableResource)
                        continue;

                    Drawable drawable = drawableResource.GetOriginal();
                    DrawableRenderNode? node = childIndex < Children.Count
                        ? Children[childIndex] as DrawableRenderNode
                        : null;
                    bool canReuse = node?.Drawable is { } captured
                                    && ReferenceEquals(captured.Resource.GetOriginal(), drawable);
                    if (canReuse)
                    {
                        bool resourceChanged = !drawableResource.Compare(node!.Drawable);
                        if (resourceChanged || rebuildAll)
                        {
                            RebuildChildTransactionally(
                                node,
                                drawable,
                                drawableResource,
                                canvasSize,
                                outputScale);
                        }
                    }
                    else
                    {
                        node = new DrawableRenderNode(drawableResource);
                        bool installed = false;
                        try
                        {
                            using var graphics = new GraphicsContext2D(
                                node,
                                canvasSize,
                                outputScale);
                            drawable.Render(graphics, drawableResource);

                            if (childIndex < Children.Count)
                            {
                                installed = true;
                                SetChild(childIndex, node);
                            }
                            else
                            {
                                AddChild(node);
                                installed = true;
                            }
                        }
                        catch
                        {
                            if (!installed)
                                node.Dispose();
                            throw;
                        }
                    }

                    childIndex++;
                }
            }

            if (childIndex < Children.Count)
            {
                RenderNode[] removed = [.. Children.Skip(childIndex)];
                RemoveRange(childIndex, Children.Count - childIndex);
                DisposeAll(removed);
            }
        }

        private static void RebuildChildTransactionally(
            DrawableRenderNode destination,
            Drawable drawable,
            Drawable.Resource resource,
            Size canvasSize,
            float outputScale)
        {
            using var candidate = new DrawableRenderNode(resource);
            using (var graphics = new GraphicsContext2D(
                       candidate,
                       canvasSize,
                       outputScale))
            {
                drawable.Render(graphics, resource);
            }

            RenderNode[] previous = [.. destination.Children];
            destination.BringFrom(candidate);
            DisposeAll(previous);
            destination.Update(resource);
            destination.HasChanges = true;
        }

        private static void DisposeAll(IEnumerable<RenderNode> nodes)
        {
            List<Exception>? failures = null;
            foreach (RenderNode node in nodes)
            {
                try
                {
                    node.Dispose();
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }

            if (failures is [var failure])
                ExceptionDispatchInfo.Capture(failure).Throw();
            if (failures is { Count: > 1 })
            {
                throw new AggregateException(
                    "One or more nested-scene nodes failed to dispose.",
                    failures);
            }
        }

        public override void Process(RenderNodeContext context)
        {
            var frame = Scene?.Resource.Frame;
            if (frame == null)
                return;

            PixelSize size = frame.Value.Size;
            var domain = new Rect(0, 0, size.Width, size.Height);
            context.Publish(context.Layer(context.Inputs, domain));
        }

        protected override void OnDispose(bool disposing)
        {
            Scene = null;
            base.OnDispose(disposing);
        }
    }
}

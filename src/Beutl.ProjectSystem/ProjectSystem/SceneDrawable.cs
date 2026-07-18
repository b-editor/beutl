using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
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
        context.DrawNode(
            r,
            static r => new SceneBitmapRenderNode(r),
            static (node, r) => node.Update(r));
    }

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
                || _compositor?.ForceOriginalSource != forceOriginalSource
                || _compositor?.RenderIntent != context.RenderIntent)
            {
                _compositor?.Dispose();
                _compositor = null;
            }

            if (ReferencedScene != null && _compositor == null)
            {
                _compositor = new SceneCompositor(ReferencedScene, context.RenderIntent)
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
                Frame = _compositor?.EvaluateGraphics(
                    context.Time - obj.Start,
                    context.PullPurpose);

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
            if (!disposing)
                return;

            SceneCompositor? compositor = _compositor;
            _compositor = null;
            Frame = null;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, compositor);
            ThrowIfCleanupFailed(failure);
        }
    }

    private class SceneBitmapRenderNode(Resource resource) : RenderNode
    {
        private Renderer? _renderer;

        public (Resource Resource, int Version)? Scene { get; set; } = resource.Capture();

        public bool Update(Resource resource)
        {
            if (!resource.Compare(Scene))
            {
                Scene = resource.Capture();
                HasChanges = true;
                return true;
            }

            return false;
        }

        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            var frame = Scene?.Resource.Frame;
            if (frame == null)
                return [];

            // Inherit the outer render scale so nested scenes are not rasterized at 1x and upscaled.
            float w = context.OutputScale;
            var size = frame.Value.Size;
            if (!context.IsAuxiliaryPull
                && (_renderer == null
                    || _renderer.FrameSize != size
                    || _renderer.OutputScale != w
                    || _renderer.MaxWorkingScale != context.MaxWorkingScale
                    || _renderer.RenderIntent != context.RenderIntent))
            {
                _renderer?.Dispose();
                _renderer = new Renderer(
                    size.Width, size.Height, context.RenderIntent, w, context.MaxWorkingScale);
            }

            Renderer? renderer = context.IsAuxiliaryPull ? null : _renderer;
            RenderIntent renderIntent = context.RenderIntent;
            float maxWorkingScale = context.MaxWorkingScale;
            RenderPullPurpose pullPurpose = context.PullPurpose;
            Renderer? auxiliaryRenderer = null;
            var bounds = new Rect(0, 0, size.Width, size.Height);
            return
            [
                RenderNodeOperation.CreateLambda(
                    bounds,
                    canvas =>
                    {
                        if (pullPurpose == RenderPullPurpose.Auxiliary)
                        {
                            auxiliaryRenderer ??= new Renderer(
                                size.Width, size.Height, renderIntent, w, maxWorkingScale,
                                RenderPullPurpose.Auxiliary);
                        }

                        Renderer activeRenderer = auxiliaryRenderer ?? renderer
                            ?? throw new InvalidOperationException("The nested-scene renderer is unavailable.");
                        activeRenderer.Render(frame.Value);
                        RenderTarget renderTarget = Renderer.GetInternalRenderTarget(activeRenderer);
                        // Point-blit only when both buffer and canvas are at density 1; otherwise use scaled blit.
                        if (w == 1f && canvas.Density == 1f)
                        {
                            canvas.DrawRenderTarget(renderTarget, default);
                        }
                        else
                        {
                            canvas.DrawRenderTargetScaled(renderTarget, bounds);
                        }
                    },
                    onDispose: () => auxiliaryRenderer?.Dispose(),
                    effectiveScale: EffectiveScale.At(w))
            ];
        }

        protected override void OnDispose(bool disposing)
        {
            base.OnDispose(disposing);
            Scene = null;
            _renderer?.Dispose();
            _renderer = null;
        }
    }
}

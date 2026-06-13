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
            if (_start != obj.Start)
            {
                _start = obj.Start;
                changed = true;
            }

            if (_compositor?.Scene != ReferencedScene
                || _compositor?.DisableResourceShare != context.DisableResourceShare)
            {
                _compositor?.Dispose();
                _compositor = null;
            }

            if (ReferencedScene != null && _compositor == null)
            {
                _compositor = new SceneCompositor(ReferencedScene)
                {
                    DisableResourceShare = context.DisableResourceShare,
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

            // feature 003 (FR-022/FR-019b): the nested scene MUST inherit the outer render scale and working-scale
            // ceiling — otherwise a supersampled export rasterizes the inner scene at 1x and the root CTM upscales
            // it (soft). OutputScale/MaxWorkingScale are immutable per outer renderer, so within one outer renderer
            // this rebuild fires at most once; on an outer-scale change the whole node tree is rebuilt anyway.
            float w = context.OutputScale;
            var size = frame.Value.Size;
            if (_renderer == null
                || _renderer.FrameSize != size
                || _renderer.RenderScale != w
                || _renderer.MaxWorkingScale != context.MaxWorkingScale)
            {
                _renderer?.Dispose();
                _renderer = new Renderer(size.Width, size.Height, w, context.MaxWorkingScale);
            }

            Renderer renderer = _renderer;
            var bounds = new Rect(0, 0, size.Width, size.Height);
            return
            [
                RenderNodeOperation.CreateLambda(
                    bounds,
                    canvas =>
                    {
                        renderer.Render(frame.Value);
                        RenderTarget renderTarget = Renderer.GetInternalRenderTarget(renderer);
                        // The inner surface is ceil(size × w) device px. At w == 1 keep the bare point blit
                        // (byte-identical); at w != 1 draw it into the LOGICAL bounds so the draw canvas's baked
                        // base CTM CreateScale(w) maps the denser buffer 1:1 onto the device surface (crisp under
                        // SSAA export). NOTE: a bare point-blit is device-1:1-correct only when BOTH the buffer is
                        // density-1 AND the draw canvas is device-1:1. We gate on the inner-renderer scale `w`
                        // because FR-022 wires it to equal the draw-time canvas.Density (the nested scene inherits
                        // the outer scale), so `w == 1f` is the device-1:1 condition. If that coupling ever breaks,
                        // switch this to `canvas.Density == 1f` (the draw-time signal RenderNodeOperation uses).
                        if (w == 1f)
                        {
                            canvas.DrawRenderTarget(renderTarget, default);
                        }
                        else
                        {
                            canvas.DrawRenderTargetScaled(renderTarget, bounds);
                        }
                    },
                    // A fixed-resolution nested-scene buffer is concrete bitmap supply at density w (FR-019b),
                    // never the re-rasterizable Unbounded — so a parent boundary reconciles it honestly.
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

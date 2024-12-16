using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SkiaSharp;

namespace Beutl.Graphics3D;

public class Scene3D : Drawable
{
    public static readonly CoreProperty<int> WidthProperty;
    public static readonly CoreProperty<int> HeightProperty;
    public static readonly CoreProperty<Camera?> CameraProperty;
    private int _width = -1;
    private int _height = -1;
    private Camera? _camera;
    private SceneNode? _root;

    static Scene3D()
    {
        WidthProperty = ConfigureProperty<int, Scene3D>(nameof(Width))
            .Accessor(o => o.Width, (o, v) => o.Width = v)
            .DefaultValue(-1)
            .Register();

        HeightProperty = ConfigureProperty<int, Scene3D>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .DefaultValue(-1)
            .Register();

        CameraProperty = ConfigureProperty<Camera?, Scene3D>(nameof(Camera))
            .Accessor(o => o.Camera, (o, v) => o.Camera = v)
            .Register();


        AffectsRender<Scene3D>(
            WidthProperty, HeightProperty, CameraProperty);
    }

    public Scene3D()
    {
        Camera = new PerspectiveCamera { AspectRatio = 1 };
    }

    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    [Range(-1, int.MaxValue)]
    public int Width
    {
        get => _width;
        set => SetAndRaise(WidthProperty, ref _width, value);
    }

    [Display(Name = nameof(Strings.Height), ResourceType = typeof(Strings))]
    [Range(-1, int.MaxValue)]
    public int Height
    {
        get => _height;
        set => SetAndRaise(HeightProperty, ref _height, value);
    }

    public Camera? Camera
    {
        get => _camera;
        set => SetAndRaise(CameraProperty, ref _camera, value);
    }


    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        Camera?.ApplyAnimations(clock);
    }

    protected override Size MeasureCore(Size availableSize)
    {
        if (Width > 0 && Height > 0)
        {
            return new Size(Width, Height);
        }

        return availableSize;
    }

    protected override void OnDraw(GraphicsContext2D canvas)
    {
        if (_root?.IsDisposed == true)
        {
            _root = null;
        }

        var size = canvas.Size;
        if (Width > 0 && Height > 0)
        {
            size = new PixelSize(Width, Height);
        }

        switch (Camera)
        {
            case PerspectiveCamera persp:
                persp.AspectRatio = size.Width / (float)size.Height;
                break;
            case OrthographicCamera ortho:
                ortho.Width = size.Width;
                ortho.Height = size.Height;
                break;

            default:
                Camera = new PerspectiveCamera { AspectRatio = size.Width / (float)size.Height };
                break;
        }

        _root ??= new SceneNode(new Rect(size.ToSize(1)), this);
        canvas.DrawNode(_root);
    }

    private class SceneNode(Rect bounds, Scene3D scene) : RenderNode, IEquatable<SceneNode?>
    {
        private ColorBuffer? _colorBuffer;
        private DepthBuffer? _depthBuffer;
        private FrameBuffer? _frameBuffer;
        private GRBackendRenderTarget? _renderTarget;
        private SKSurface? _surface;

        private Rect Bounds => bounds;

        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            context.IsRenderCacheEnabled = false;
            return [RenderNodeOperation.CreateLambda(bounds, Render, HitTest)];
        }

        protected override void OnDispose(bool disposing)
        {
            base.OnDispose(disposing);
            _surface?.Dispose();
            _renderTarget?.Dispose();
            _frameBuffer?.Dispose();
            _depthBuffer?.Dispose();
            _colorBuffer?.Dispose();
            (_surface, _renderTarget, _frameBuffer, _depthBuffer, _colorBuffer) = (null, null, null, null, null);
        }

        private bool HitTest(Point point)
        {
            return bounds.ContainsExclusive(point);
        }

        private unsafe void Render(ImmediateCanvas canvas)
        {
            SharedGRContext.MakeCurrent();
            if (_surface is null || _colorBuffer is null || _depthBuffer is null || _frameBuffer is null)
            {
                Init();
            }

            int oldFbo = 0;
            GL.GetIntegerv(GetPName.DrawFramebufferBinding, &oldFbo);
            _frameBuffer!.Bind();
            int width = (int)bounds.Width;
            int height = (int)bounds.Height;
            GL.Viewport(0, 0, width, height);
            // アンチエイリアス
            GL.Enable(EnableCap.LineSmooth);
            GL.Enable(EnableCap.PolygonSmooth);
            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.TextureCompressionHint, HintMode.Nicest);
            GL.Disable(EnableCap.DepthTest);
            GL.ClearColor(Color4.Red);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GlErrorHelper.CheckGlError();

            _frameBuffer!.Unbind();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, oldFbo);

            canvas.DrawSurface(_surface!, default);
        }

        private void Init()
        {
            SharedGRContext.MakeCurrent();
            SharedGRContext.GRContext!.ResetContext();
            int width = (int)bounds.Width;
            int height = (int)bounds.Height;
            _colorBuffer = new ColorBuffer(width, height, InternalFormat.Rgba, PixelFormat.Rgba,
                PixelType.UnsignedByte);
            _depthBuffer = new DepthBuffer(width, height);
            _frameBuffer = new FrameBuffer(_colorBuffer, _depthBuffer);

            _renderTarget = new GRBackendRenderTarget(
                (int)bounds.Width,
                (int)bounds.Height,
                sampleCount: 0,
                stencilBits: 8,
                new GRGlFramebufferInfo((uint)_frameBuffer.Handle, SKColorType.Rgba8888.ToGlSizedFormat())
            );

            _surface = SKSurface.Create(SharedGRContext.GRContext, _renderTarget, GRSurfaceOrigin.TopLeft,
                SKColorType.Rgba8888);
        }

        public bool Equals(SceneNode? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return bounds.Equals(other.Bounds);
        }
    }
}

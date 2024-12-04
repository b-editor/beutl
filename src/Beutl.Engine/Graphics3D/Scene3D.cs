using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SkiaSharp;

namespace Beutl.Graphics3D;

public class Scene3D : Drawable
{
    private SceneNode? _root;

    protected override Size MeasureCore(Size availableSize)
    {
        return availableSize;
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (_root?.IsDisposed == true)
        {
            _root = null;
        }

        _root ??= new SceneNode(new Rect(canvas.Size.ToSize(1)));
        canvas.DrawNode(_root);
    }

    private class SceneNode : DrawNode, IEquatable<SceneNode?>
    {
        private ColorBuffer? _colorBuffer;
        private DepthBuffer? _depthBuffer;
        private FrameBuffer? _frameBuffer;
        private GRBackendRenderTarget? _renderTarget;
        private SKSurface? _surface;

        public SceneNode(Rect bounds) : base(bounds)
        {
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

        public override bool HitTest(Point point)
        {
            return Bounds.ContainsExclusive(point);
        }

        public override unsafe void Render(ImmediateCanvas canvas)
        {
            SharedGRContext.MakeCurrent();
            if (_surface is null || _colorBuffer is null || _depthBuffer is null || _frameBuffer is null)
            {
                Init();
            }

            int oldFbo = 0;
            GL.GetIntegerv(GetPName.DrawFramebufferBinding, &oldFbo);
            _frameBuffer!.Bind();
            int width = (int)Bounds.Width;
            int height = (int)Bounds.Height;
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
            int width = (int)Bounds.Width;
            int height = (int)Bounds.Height;
            _colorBuffer = new ColorBuffer(width, height, InternalFormat.Rgba, PixelFormat.Rgba,
                PixelType.UnsignedByte);
            _depthBuffer = new DepthBuffer(width, height);
            _frameBuffer = new FrameBuffer(_colorBuffer, _depthBuffer);

            _renderTarget = new GRBackendRenderTarget(
                (int)Bounds.Width,
                (int)Bounds.Height,
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
            return Bounds.Equals(other.Bounds);
        }
    }
}

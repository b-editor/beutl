using BeUtl.Graphics.Filters;
using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.Rendering;

namespace BeUtl.Graphics;

public abstract class Drawable : IDrawable, IRenderable
{
    ~Drawable()
    {
        if (!IsDisposed)
        {
            Dispose();
        }
    }

    public abstract PixelSize Size { get; }

    public Transforms Transform { get; } = new();

    public AlignmentX HorizontalAlignment { get; set; }

    public AlignmentY VerticalAlignment { get; set; }

    public AlignmentX HorizontalContentAlignment { get; set; }

    public AlignmentY VerticalContentAlignment { get; set; }

    public ImageFilters Filters { get; } = new();

    public bool IsDisposed { get; protected set; }

    public IBrush Foreground { get; set; } = Colors.White.ToBrush();

    public IBrush? OpacityMask { get; set; }

    public BlendMode BlendMode { get; set; } = BlendMode.SrcOver;

    public void Initialize()
    {
        OnInitialize();
        HorizontalAlignment = AlignmentX.Left;
        VerticalAlignment = AlignmentY.Top;
        HorizontalContentAlignment = AlignmentX.Left;
        VerticalContentAlignment = AlignmentY.Top;
        Foreground = Colors.White.ToBrush();
        OpacityMask = null;
        BlendMode = BlendMode.SrcOver;
        Transform.Clear();
        Filters.Clear();
    }

    public IBitmap ToBitmap()
    {
        VerifyAccess();
        PixelSize pixelSize = Size;
        using var canvas = new Canvas(pixelSize.Width, pixelSize.Height);

        OnDraw(canvas);

        return canvas.GetBitmap();
    }

    public abstract void Dispose();

    public void Draw(ICanvas canvas)
    {
        VerifyAccess();
        Size size = Size.ToSize(1);
        Matrix transform = Transform.Calculate();
        Vector pt = CreatePoint(canvas.Size);
        Vector relpt = CreateRelPoint(size);

        using (canvas.PushForeground(Foreground))
        using (canvas.PushBlendMode(BlendMode))
        using (canvas.PushFilters(Filters))
        using (canvas.PushTransform(Matrix.CreateTranslation(relpt) * transform * Matrix.CreateTranslation(pt)))
        using (OpacityMask == null ? new() : canvas.PushOpacityMask(OpacityMask, new Rect(size)))
        {
            OnDraw(canvas);
        }
    }

    public void Render(IRenderer renderer)
    {
        Draw(renderer.Graphics);
    }

    public void VerifyAccess()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    protected abstract void OnDraw(ICanvas canvas);

    protected virtual void OnInitialize()
    {
    }

    private Point CreateRelPoint(Size size)
    {
        float x = 0;
        float y = 0;

        if (HorizontalContentAlignment == AlignmentX.Center)
        {
            x -= size.Width / 2;
        }
        else if (HorizontalContentAlignment == AlignmentX.Right)
        {
            x -= size.Width;
        }

        if (VerticalContentAlignment == AlignmentY.Center)
        {
            y -= size.Height / 2;
        }
        else if (VerticalContentAlignment == AlignmentY.Bottom)
        {
            y -= size.Height;
        }

        return new Point(x, y);
    }

    private Point CreatePoint(PixelSize canvasSize)
    {
        float x = 0;
        float y = 0;

        if (HorizontalAlignment == AlignmentX.Center)
        {
            x += canvasSize.Width / 2;
        }
        else if (HorizontalAlignment == AlignmentX.Right)
        {
            x += canvasSize.Width;
        }

        if (VerticalAlignment == AlignmentY.Center)
        {
            y += canvasSize.Height / 2;
        }
        else if (VerticalAlignment == AlignmentY.Bottom)
        {
            y += canvasSize.Height;
        }

        return new Point(x, y);
    }
}

using System.Numerics;

using BEditorNext.Graphics.Effects;

using BEditorNext.Media;
using BEditorNext.Media.Pixel;
using BEditorNext.Rendering;

namespace BEditorNext.Graphics;

public abstract class Drawable : IDrawable, IRenderable
{
    public abstract PixelSize Size { get; }

    public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;

    public AlignmentX HorizontalAlignment { get; set; }

    public AlignmentY VerticalAlignment { get; set; }

    public AlignmentX HorizontalContentAlignment { get; set; }

    public AlignmentY VerticalContentAlignment { get; set; }

    public EffectCollection Effects { get; } = new();

    public bool IsDisposed { get; protected set; }

    public Color Foreground { get; set; } = Colors.White;

    public bool IsAntialias { get; set; } = true;

    public void Initialize()
    {
        OnInitialize();
        Transform = Matrix3x2.Identity;
        HorizontalAlignment = AlignmentX.Left;
        VerticalAlignment = AlignmentY.Top;
        HorizontalContentAlignment = AlignmentX.Left;
        VerticalContentAlignment = AlignmentY.Top;
        Foreground = Colors.White;
        IsAntialias = true;
        Effects.Clear();
    }

    public Bitmap<Bgra8888> ToBitmap()
    {
        VerifyAccess();
        PixelSize pixelSize = Size;
        Size size = pixelSize.ToSize(1);
        Rect bounds = BitmapEffect.MeasureAll(new Rect(size), Effects);
        using var canvas = new Canvas(pixelSize.Width, pixelSize.Height);

        canvas.PushMatrix();

        canvas.SetMatrix(Transform * canvas.TotalMatrix);
        Point pt = CreatePoint(size, canvas.Size) + bounds.Position;
        canvas.Translate(pt);
        OnDraw(canvas);
        canvas.PopMatrix();

        return canvas.GetBitmap();
    }

    public abstract void Dispose();

    public void Draw(ICanvas canvas)
    {
        VerifyAccess();
        Size size = Size.ToSize(1);
        Rect bounds = BitmapEffect.MeasureAll(new Rect(size), Effects);
        Color oldcolor = canvas.Color;
        bool oldIsAntialias = canvas.IsAntialias;

        canvas.Color = Foreground;
        canvas.IsAntialias = IsAntialias;
        canvas.PushMatrix();

        canvas.SetMatrix(Transform * canvas.TotalMatrix);
        Point pt = CreatePoint(size, canvas.Size) + bounds.Position;
        canvas.Translate(pt);
        OnDraw(canvas);
        canvas.PopMatrix();

        canvas.Color = oldcolor;
        canvas.IsAntialias = oldIsAntialias;
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

    private Point CreatePoint(Size size, PixelSize canvasSize)
    {
        var drawable = this as IDrawable;
        float x = 0;
        float y = 0;

        if (drawable.HorizontalContentAlignment == AlignmentX.Center)
        {
            x -= size.Width / 2;
        }
        else if (drawable.HorizontalContentAlignment == AlignmentX.Right)
        {
            x -= size.Width;
        }

        if (drawable.VerticalContentAlignment == AlignmentY.Center)
        {
            y -= size.Height / 2;
        }
        else if (drawable.VerticalContentAlignment == AlignmentY.Bottom)
        {
            y -= size.Height;
        }

        if (drawable.HorizontalAlignment == AlignmentX.Center)
        {
            x += canvasSize.Width / 2;
        }
        else if (drawable.HorizontalAlignment == AlignmentX.Right)
        {
            x += canvasSize.Width;
        }

        if (drawable.VerticalAlignment == AlignmentY.Center)
        {
            y += canvasSize.Height / 2;
        }
        else if (drawable.VerticalAlignment == AlignmentY.Bottom)
        {
            y += canvasSize.Height;
        }

        return new Point(x, y);
    }
}

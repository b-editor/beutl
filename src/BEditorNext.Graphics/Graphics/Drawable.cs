using BEditorNext.Graphics.Effects;
using BEditorNext.Graphics.Transformation;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;
using BEditorNext.Rendering;

namespace BEditorNext.Graphics;

public abstract class Drawable : IDrawable, IRenderable
{
    public abstract PixelSize Size { get; }

    public Transforms Transform { get; } = new();

    public AlignmentX HorizontalAlignment { get; set; }

    public AlignmentY VerticalAlignment { get; set; }

    public AlignmentX HorizontalContentAlignment { get; set; }

    public AlignmentY VerticalContentAlignment { get; set; }

    public EffectCollection Effects { get; } = new();

    public bool IsDisposed { get; protected set; }

    public IBrush Foreground { get; set; } = Colors.White.ToBrush();

    public BlendMode BlendMode { get; set; } = BlendMode.SrcOver;

    public bool IsAntialias { get; set; } = true;

    public void Initialize()
    {
        OnInitialize();
        HorizontalAlignment = AlignmentX.Left;
        VerticalAlignment = AlignmentY.Top;
        HorizontalContentAlignment = AlignmentX.Left;
        VerticalContentAlignment = AlignmentY.Top;
        Foreground = Colors.White.ToBrush();
        IsAntialias = true;
        Transform.Clear();
        Effects.Clear();
    }

    public IBitmap ToBitmap()
    {
        VerifyAccess();
        PixelSize pixelSize = Size;
        Size size = pixelSize.ToSize(1);
        Rect bounds = BitmapEffect.MeasureAll(new Rect(size), Effects);
        using var canvas = new Canvas((int)bounds.Right, (int)bounds.Bottom);

        OnDraw(canvas);

        return canvas.GetBitmap();
    }

    public abstract void Dispose();

    public void Draw(ICanvas canvas)
    {
        VerifyAccess();
        Size size = Size.ToSize(1);
        Rect bounds = BitmapEffect.MeasureAll(new Rect(size), Effects);

        using (canvas.PushState())
        {
            canvas.Foreground = Foreground;
            canvas.BlendMode = BlendMode;
            canvas.IsAntialias = IsAntialias;

            Vector pt = CreatePoint(canvas.Size) + bounds.Position;
            Vector relpt = CreateRelPoint(size);

            canvas.SetMatrix(Matrix.CreateTranslation(relpt) *
                Transform.Calculate() *
                Matrix.CreateTranslation(pt) *
                canvas.TotalMatrix);

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

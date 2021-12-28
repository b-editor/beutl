using System.Numerics;

using BEditorNext.Graphics;
using BEditorNext.Graphics.Effects;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Rendering;

public class RenderableBitmap : IRenderableBitmap
{
    private readonly List<BitmapEffect> _effects = new();
    private Matrix3x2 _transform = Matrix3x2.Identity;

    public RenderableBitmap(Bitmap<Bgra8888> bitmap)
    {
        Bitmap = bitmap;
    }

    ~RenderableBitmap()
    {
        Bitmap.Dispose();
    }

    public Bitmap<Bgra8888> Bitmap { get; private set; }

    public bool IsDisposed => Bitmap.IsDisposed;

    public ref Matrix3x2 Transform => ref _transform;

    public IList<BitmapEffect> Effects => _effects;

    public PixelSize Size => new(Bitmap.Width, Bitmap.Height);

    public AlignmentX HorizontalAlignment { get; set; }

    public AlignmentY VerticalAlignment { get; set; }

    public AlignmentX HorizontalContentAlignment { get; set; }

    public AlignmentY VerticalContentAlignment { get; set; }

    public void Update(Bitmap<Bgra8888> bitmap)
    {
        Bitmap.Dispose();
        Bitmap = bitmap;
        Transform = Matrix3x2.Identity;
        HorizontalAlignment = AlignmentX.Left;
        VerticalAlignment = AlignmentY.Top;
        HorizontalContentAlignment = AlignmentX.Left;
        VerticalContentAlignment = AlignmentY.Top;
        Effects.Clear();
    }

    public void Dispose()
    {
        Bitmap.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Render(IRenderer renderer)
    {
        Draw(renderer.Graphics);
    }

    public void Draw(ICanvas canvas)
    {
        if (!IsDisposed)
        {
            if (Effects.Count == 0)
            {
                DrawCore(canvas, Bitmap);
            }
            else
            {
                using Bitmap<Bgra8888> bitmap = ToBitmap();
                using Bitmap<Bgra8888> bitmap2 = BitmapEffect.ApplyAll(bitmap, _effects);

                DrawCore(canvas, bitmap2);
            }
        }
    }

    private void DrawCore(ICanvas canvas, Bitmap<Bgra8888> bitmap)
    {
        canvas.PushMatrix();

        canvas.SetMatrix(Transform * canvas.TotalMatrix);

        Point pt = CreatePoint(new Size(bitmap.Width, bitmap.Height), canvas.Size);
        canvas.Translate(pt);
        canvas.DrawBitmap(bitmap);

        canvas.PopMatrix();
    }

    public Bitmap<Bgra8888> ToBitmap()
    {
        return (Bitmap<Bgra8888>)Bitmap.Clone();
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

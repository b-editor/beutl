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

    public (AlignmentX X, AlignmentY Y) Alignment { get; set; }

    public PixelSize Size => new(Bitmap.Width, Bitmap.Height);

    public ref Matrix3x2 Transform => ref _transform;

    public IList<BitmapEffect> Effects => _effects;

    public void Update(Bitmap<Bgra8888> bitmap)
    {
        Bitmap.Dispose();
        Bitmap = bitmap;
        Transform = Matrix3x2.Identity;
        Alignment = (AlignmentX.Left, AlignmentY.Top);
        Effects.Clear();
    }

    public void Dispose()
    {
        Bitmap.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Render(IRenderer renderer)
    {
        if (!IsDisposed)
        {
            if (Effects.Count == 0)
            {
                RenderCore(renderer, Bitmap);
            }
            else
            {
                using Bitmap<Bgra8888> bitmap = ToBitmap();
                using Bitmap<Bgra8888> bitmap2 = BitmapEffect.ApplyAll(bitmap, _effects);

                RenderCore(renderer, bitmap2);
            }
        }
    }

    private void RenderCore(IRenderer renderer, Bitmap<Bgra8888> bitmap)
    {
        renderer.Graphics.PushMatrix();

        renderer.Graphics.SetMatrix(Transform * renderer.Graphics.TotalMatrix);

        Point pt = CreatePoint(bitmap.Width, bitmap.Height);
        renderer.Graphics.Translate(pt);
        renderer.Graphics.DrawBitmap(bitmap);

        renderer.Graphics.PopMatrix();
    }

    public Bitmap<Bgra8888> ToBitmap()
    {
        return (Bitmap<Bgra8888>)Bitmap.Clone();
    }

    private Point CreatePoint(float width, float height)
    {
        float x = 0;
        float y = 0;

        if (Alignment.X == AlignmentX.Center)
        {
            x -= width / 2;
        }
        else if (Alignment.X == AlignmentX.Right)
        {
            x -= width;
        }

        if (Alignment.Y == AlignmentY.Center)
        {
            y -= height / 2;
        }
        else if (Alignment.Y == AlignmentY.Bottom)
        {
            y -= height;
        }

        return new Point(x, y);
    }
}

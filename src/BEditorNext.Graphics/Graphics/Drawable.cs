using System.Runtime.CompilerServices;

using BEditorNext.Graphics.Filters;
using BEditorNext.Graphics.Transformation;
using BEditorNext.Media;
using BEditorNext.Rendering;

namespace BEditorNext.Graphics;

public abstract class Drawable : IDrawable, IRenderable
{
    public Rect _prevBounds;
    private BlendMode _blendMode = BlendMode.SrcOver;
    private IBrush? _opacityMask;
    private IBrush _foreground = Colors.White.ToBrush();
    private AlignmentX _horizontalAlignment;
    private AlignmentY _verticalAlignment;
    private AlignmentX _horizontalContentAlignment;
    private AlignmentY _verticalContentAlignment;

    protected Drawable()
    {
        Filters = new(this);
        Transform = new(this);
    }

    ~Drawable()
    {
        if (!IsDisposed)
        {
            Dispose();
        }
    }

    public abstract PixelSize Size { get; }

    public Transforms Transform { get; }

    public AlignmentX HorizontalAlignment
    {
        get => _horizontalAlignment;
        set => SetProperty(ref _horizontalAlignment, value);
    }

    public AlignmentY VerticalAlignment
    {
        get => _verticalAlignment;
        set => SetProperty(ref _verticalAlignment, value);
    }

    public AlignmentX HorizontalContentAlignment
    {
        get => _horizontalContentAlignment;
        set => SetProperty(ref _horizontalContentAlignment, value);
    }

    public AlignmentY VerticalContentAlignment
    {
        get => _verticalContentAlignment;
        set => SetProperty(ref _verticalContentAlignment, value);
    }

    public ImageFilters Filters { get; }

    public IBrush Foreground
    {
        get => _foreground;
        set => SetProperty(ref _foreground, value);
    }

    public IBrush? OpacityMask
    {
        get => _opacityMask;
        set => SetProperty(ref _opacityMask, value);
    }

    public BlendMode BlendMode
    {
        get => _blendMode;
        set => SetProperty(ref _blendMode, value);
    }

    public bool IsDisposed { get; protected set; }

    public bool IsDirty { get; private set; }

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

    public Rect Measure(PixelSize canvasSize)
    {
        Size size = Size.ToSize(1);
        Vector pt = CreatePoint(canvasSize);
        Vector relpt = CreateRelPoint(size);
        Matrix transform = Matrix.CreateTranslation(relpt) * Transform.Calculate() * Matrix.CreateTranslation(pt);

        return Filters.TransformBounds(new Rect(size)).TransformToAABB(transform);
    }

    public void Draw(ICanvas canvas)
    {
        VerifyAccess();
        Size size = Size.ToSize(1);
        Vector pt = CreatePoint(canvas.Size);
        Vector relpt = CreateRelPoint(size);
        Matrix transform = Matrix.CreateTranslation(relpt) * Transform.Calculate() * Matrix.CreateTranslation(pt);

        using (canvas.PushForeground(Foreground))
        using (canvas.PushBlendMode(BlendMode))
        using (canvas.PushFilters(Filters))
        using (canvas.PushTransform(transform))
        using (OpacityMask == null ? new() : canvas.PushOpacityMask(OpacityMask, new Rect(size)))
        {
            OnDraw(canvas);
        }

        _prevBounds = Filters.TransformBounds(new Rect(size)).TransformToAABB(transform);
        IsDirty = false;
#if DEBUG
        //Rect bounds = _prevBounds;
        //using (canvas.PushTransform(Matrix.CreateTranslation(bounds.Position)))
        //using (canvas.PushStrokeWidth(5))
        //{
        //    canvas.DrawRect(bounds.Size);
        //}
#endif
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

    public void InvalidateVisual()
    {
        IsDirty = true;
    }

    protected bool SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            IsDirty = true;

            return true;
        }
        else
        {
            return false;
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

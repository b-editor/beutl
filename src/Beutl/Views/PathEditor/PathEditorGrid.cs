using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Views;

public sealed class PathEditorGrid : Control
{
    public static readonly StyledProperty<Matrix> MatrixProperty =
        AvaloniaProperty.Register<PathGeometryControl, Matrix>(nameof(Matrix), Matrix.Identity);

    public static readonly StyledProperty<IBrush?> BorderBrushProperty
        = Border.BorderBrushProperty.AddOwner<PathEditorGrid>();

    static PathEditorGrid()
    {
        AffectsArrange<PathEditorGrid>(MatrixProperty, BorderBrushProperty);
    }

    public Matrix Matrix
    {
        get => GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Matrix matrix = Matrix;
        const double DotSize = 10;
        double scaledDotSize = matrix.M11 * DotSize;

        foreach (double item in (double[])([0.01, 0.02, 0.05, 0.1, 0.5, 1, 5, 10]))
        {
            double u = matrix.M11 * item;
            if (u > 100)
            {
                scaledDotSize = u;
                matrix = Matrix.CreateScale(DotSize / scaledDotSize, DotSize / scaledDotSize) * matrix;
                break;
            }
        }

        double width = Bounds.Width;
        double height = Bounds.Height;

        int hsplit = (int)Math.Ceiling(width / scaledDotSize) + 1;
        int vsplit = (int)Math.Ceiling(height / scaledDotSize) + 1;

        double offsetX = matrix.M31;
        double offsetY = matrix.M32;
        double amariX = offsetX % scaledDotSize;
        double amariY = offsetY % scaledDotSize;

        Matrix m = Matrix.CreateTranslation(amariX, amariY);
        IBrush brush = BorderBrush ?? Brushes.Gray;
        var pen = new ImmutablePen(brush.ToImmutable());

        if (scaledDotSize > 5)
        {
            for (int i = -1; i < hsplit; i++)
            {
                double x = i * scaledDotSize;
                Point p1 = new Point(x, -scaledDotSize).Transform(m);
                Point p2 = new Point(x, height + scaledDotSize).Transform(m);
                context.DrawLine(pen, p1, p2);
            }

            for (int i = -1; i < vsplit; i++)
            {
                double y = i * scaledDotSize;
                Point p1 = new Point(-scaledDotSize, y).Transform(m);
                Point p2 = new Point(width + scaledDotSize, y).Transform(m);
                context.DrawLine(pen, p1, p2);
            }

            pen = new ImmutablePen(Brushes.DarkGray.ToImmutable());
        }

        scaledDotSize *= 10;
        amariX = offsetX % scaledDotSize;
        amariY = offsetY % scaledDotSize;
        m = Matrix.CreateTranslation(amariX, amariY);
        hsplit = (int)Math.Ceiling(width / scaledDotSize) + 1;
        vsplit = (int)Math.Ceiling(height / scaledDotSize) + 1;
        if (scaledDotSize > 10)
        {
            for (int i = -1; i < hsplit; i++)
            {
                double x = i * scaledDotSize;
                Point p1 = new Point(x, -scaledDotSize).Transform(m);
                Point p2 = new Point(x, height + scaledDotSize).Transform(m);
                context.DrawLine(pen, p1, p2);
            }

            for (int i = -1; i < vsplit; i++)
            {
                double y = i * scaledDotSize;
                Point p1 = new Point(-scaledDotSize, y).Transform(m);
                Point p2 = new Point(width + scaledDotSize, y).Transform(m);
                context.DrawLine(pen, p1, p2);
            }
        }

        pen = new ImmutablePen(Brushes.DeepSkyBlue.ToImmutable());
        using (context.PushTransform(Matrix.CreateTranslation(offsetX, 0)))
        {
            var p1 = new Point(0, 0);
            var p2 = new Point(0, height);
            context.DrawLine(pen, p1, p2);
        }

        using (context.PushTransform(Matrix.CreateTranslation(0, offsetY)))
        {
            var p1 = new Point(0, 0);
            var p2 = new Point(width, 0);
            context.DrawLine(pen, p1, p2);
        }

    }
}

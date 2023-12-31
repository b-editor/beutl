using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace KeySplineEditor;

public partial class MainWindow : Window
{
    private readonly KeySpline _keySpline = new();
    private readonly KeySplineDrawing _keySplineDrawing;
    private bool _isPoint1MouseDown;
    private bool _isPoint2MouseDown;
    private Point _pointStartAbs;
    private Point _point1Start;
    private Point _point2Start;

    public MainWindow()
    {
        InitializeComponent();

        SetControlPoint(_keySpline);
        panel.AddHandler(PointerMovedEvent, Panel_PointerMoved, RoutingStrategies.Tunnel);
        controlPt1.AddHandler(PointerPressedEvent, Point1_PointerPressed, RoutingStrategies.Tunnel);
        controlPt1.AddHandler(PointerReleasedEvent, Point1_PointerReleased, RoutingStrategies.Tunnel);
        controlPt2.AddHandler(PointerPressedEvent, Point2_PointerPressed, RoutingStrategies.Tunnel);
        controlPt2.AddHandler(PointerReleasedEvent, Point2_PointerReleased, RoutingStrategies.Tunnel);

        _keySplineDrawing = new KeySplineDrawing(panel, _keySpline);
        panel.Children.Insert(0, _keySplineDrawing);
#if DEBUG
        this.AttachDevTools();
#endif
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        SetControlPoint(_keySpline);
        return base.ArrangeOverride(finalSize);
    }

    private void Panel_PointerMoved(object? sender, PointerEventArgs e)
    {
        Point point = e.GetPosition(panel);
        point = point.WithX(Math.Clamp(point.X, 0, panel.Bounds.Width))
            .WithY(Math.Clamp(point.Y, 0, panel.Bounds.Height));

        Thickness pt1 = controlPt1.Margin;
        Thickness pt2 = controlPt2.Margin;

        if (_isPoint1MouseDown)
        {
            Point newPoint = _pointStartAbs - _point1Start;
            pt1 = new Thickness(newPoint.X, newPoint.Y, 0, 0);
        }

        if (_isPoint2MouseDown)
        {
            Point newPoint = _pointStartAbs - _point2Start;
            pt2 = new Thickness(newPoint.X, newPoint.Y, 0, 0);
        }

        controlPt1.Margin = pt1;
        controlPt2.Margin = pt2;
        SetControlPoint(pt1, pt2);
        _keySplineDrawing.InvalidateVisual();

        _pointStartAbs = point;
    }

    private void Point1_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isPoint1MouseDown = true;
        _pointStartAbs = e.GetPosition(panel);
        _point1Start = e.GetPosition(controlPt1);
    }

    private void Point1_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPoint1MouseDown = false;
    }

    private void Point2_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isPoint2MouseDown = true;
        _pointStartAbs = e.GetPosition(panel);
        _point2Start = e.GetPosition(controlPt2);
    }

    private void Point2_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPoint2MouseDown = false;
    }

    private void SetControlPoint(KeySpline keySpline)
    {
        double width = panel.Bounds.Width;
        double height = panel.Bounds.Height;

        controlPt1.Margin = new Thickness(
            keySpline.ControlPointX1 * width,
            Math.Abs(keySpline.ControlPointY1 - 1) * height,
            0,
            0);
        controlPt2.Margin = new Thickness(
            keySpline.ControlPointX2 * width,
            Math.Abs(keySpline.ControlPointY2 - 1) * height,
            0,
            0);
    }

    private void SetControlPoint(Thickness ctrlPt1, Thickness ctrlPt2)
    {
        double width = panel.Bounds.Width;
        double height = panel.Bounds.Height;

        _keySpline.ControlPointX1 = Math.Clamp(ctrlPt1.Left / width, 0, 1);
        _keySpline.ControlPointY1 = Math.Clamp(Math.Abs(ctrlPt1.Top / height - 1), 0, 1);
        _keySpline.ControlPointX2 = Math.Clamp(ctrlPt2.Left / width, 0, 1);
        _keySpline.ControlPointY2 = Math.Clamp(Math.Abs(ctrlPt2.Top / height - 1), 0, 1);
    }

    private sealed class KeySplineDrawing(Panel panel, KeySpline keySpline) : Control
    {
        private readonly Pen _pen = new()
        {
            Brush = Brushes.DarkGray,
            LineJoin = PenLineJoin.Round,
            LineCap = PenLineCap.Round,
            Thickness = 2.5,
        };

        public override void Render(DrawingContext context)
        {
            Size size = panel.Bounds.Size;

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctxt = geometry.Open())
            {
                double width = size.Width;
                double height = size.Height;
                ctxt.BeginFigure(new Point(0, height), false);

                ctxt.CubicBezierTo(
                    new Point(keySpline.ControlPointX1 * width, (1 - keySpline.ControlPointY1) * height),
                    new Point(keySpline.ControlPointX2 * width, (1 - keySpline.ControlPointY2) * height),
                    new Point(width, 0));

                ctxt.EndFigure(false);
            }

            context.DrawGeometry(Brushes.White, _pen, geometry);
            //for (int i = 0; i < 100; i++)
            //{
            //    double value = Math.Abs(_keySpline.GetSplineProgress(i / 100d) - 1);
            //    double after = Math.Abs(_keySpline.GetSplineProgress((i + 1) / 100d) - 1);

            //    context.DrawLine(
            //        _pen,
            //        new Point(i / 100d * size.Width, value * size.Height),
            //        new Point((i + 1) / 100d * size.Width, after * size.Height));
            //}
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using AvaPoint = Avalonia.Point;
using AvaRect = Avalonia.Rect;
using AvaSize = Avalonia.Size;
using AvaVector = Avalonia.Vector;
using BtlDrawable = Beutl.Graphics.Drawable;
using BtlMatrix = Beutl.Graphics.Matrix;
using BtlPoint = Beutl.Graphics.Point;
using BtlSize = Beutl.Graphics.Size;
using Element = Beutl.ProjectSystem.Element;

namespace Beutl.Views;

internal sealed class TransformHandlesOverlay : Control
{
    private static readonly ILogger s_logger = Log.CreateLogger<TransformHandlesOverlay>();

    private const double HandleSize = 8.0;
    private const double EdgeHandleLength = 14.0;
    private const double RotateOuterDistance = 18.0;
    private const double CenterMarkerRadius = 4.0;

    private static readonly ImmutableSolidColorBrush s_handleFill = new(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly ImmutableSolidColorBrush s_accentFill = new(Color.FromRgb(0x1E, 0x90, 0xFF));
    private static readonly IPen s_handleStroke = new ImmutablePen(s_accentFill, 1.5);
    private static readonly IPen s_boxStroke = new ImmutablePen(s_accentFill, 1.0);

    private static readonly Cursor s_cursorCorner1 = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor s_cursorCorner2 = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor s_cursorNS = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor s_cursorWE = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor s_cursorRotate = new(StandardCursorType.Hand);
    private static readonly Cursor s_cursorMove = new(StandardCursorType.SizeAll);

    private BtlDrawable? _drawable;
    private Element? _element;
    private BtlSize _localSize;
    private BtlMatrix _userMatrix = BtlMatrix.Identity;
    private double _frameScale = 1.0;
    private readonly AvaPoint[] _imageCorners = new AvaPoint[4];
    private AvaPoint _pivotImage;

    private bool HasShape =>
        _drawable != null
        && _localSize.Width > 0
        && _localSize.Height > 0
        && _frameScale > 0
        && _userMatrix.HasInverse;

    public enum HandleKind
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left,
        Rotate,
        Center,
    }

    public TransformHandlesOverlay()
    {
        IsHitTestVisible = false;
    }

    public BtlDrawable? Drawable => _drawable;

    public Element? Element => _element;

    public BtlSize LocalSize => _localSize;

    public BtlMatrix UserMatrix => _userMatrix;

    public double FrameScale => _frameScale;

    public BtlPoint PivotLocal { get; private set; }

    public AvaPoint PivotImage => _pivotImage;

    public void Update(
        BtlDrawable? drawable,
        Element? element,
        BtlSize localSize,
        BtlMatrix userMatrix,
        BtlPoint pivotLocal,
        AvaSize imageSize,
        double frameScale)
    {
        if (ReferenceEquals(_drawable, drawable)
            && ReferenceEquals(_element, element)
            && _localSize == localSize
            && _userMatrix == userMatrix
            && _frameScale == frameScale
            && PivotLocal == pivotLocal)
        {
            return;
        }

        _drawable = drawable;
        _element = element;
        _localSize = localSize;
        _userMatrix = userMatrix;
        _frameScale = frameScale;
        PivotLocal = pivotLocal;

        if (drawable != null && localSize.Width > 0 && localSize.Height > 0 && frameScale > 0 && !userMatrix.HasInverse)
        {
            s_logger.LogDebug(
                "TransformHandlesOverlay: userMatrix non-invertible, hiding overlay. Drawable={DrawableType}",
                drawable.GetType().Name);
        }

        RecomputeImageGeometry();
        InvalidateVisual();
    }

    public void Clear()
    {
        _drawable = null;
        _element = null;
        _localSize = default;
        _userMatrix = BtlMatrix.Identity;
        InvalidateVisual();
    }

    private void RecomputeImageGeometry()
    {
        if (!HasShape) return;

        double w = _localSize.Width;
        double h = _localSize.Height;
        _imageCorners[0] = LocalToImage(0, 0);
        _imageCorners[1] = LocalToImage(w, 0);
        _imageCorners[2] = LocalToImage(w, h);
        _imageCorners[3] = LocalToImage(0, h);
        _pivotImage = LocalToImage(PivotLocal.X, PivotLocal.Y);
    }

    private AvaPoint LocalToImage(double lx, double ly)
    {
        BtlPoint p = _userMatrix.Transform(new BtlPoint((float)lx, (float)ly));
        return new AvaPoint(p.X * _frameScale, p.Y * _frameScale);
    }

    private AvaPoint LocalEdgeMid(int cornerA, int cornerB)
    {
        AvaPoint a = _imageCorners[cornerA];
        AvaPoint b = _imageCorners[cornerB];
        return new AvaPoint((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!HasShape) return;

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(_imageCorners[0], isFilled: false);
            ctx.LineTo(_imageCorners[1]);
            ctx.LineTo(_imageCorners[2]);
            ctx.LineTo(_imageCorners[3]);
            ctx.EndFigure(isClosed: true);
        }
        context.DrawGeometry(null, s_boxStroke, geom);

        context.DrawEllipse(s_accentFill, null,
            new AvaRect(
                _pivotImage.X - CenterMarkerRadius,
                _pivotImage.Y - CenterMarkerRadius,
                CenterMarkerRadius * 2,
                CenterMarkerRadius * 2));

        for (int i = 0; i < 4; i++)
        {
            DrawCornerHandle(context, _imageCorners[i]);
        }

        DrawEdgeHandle(context, LocalEdgeMid(0, 1), _imageCorners[1] - _imageCorners[0]);
        DrawEdgeHandle(context, LocalEdgeMid(1, 2), _imageCorners[2] - _imageCorners[1]);
        DrawEdgeHandle(context, LocalEdgeMid(2, 3), _imageCorners[3] - _imageCorners[2]);
        DrawEdgeHandle(context, LocalEdgeMid(3, 0), _imageCorners[0] - _imageCorners[3]);
    }

    private static void DrawCornerHandle(DrawingContext context, AvaPoint p)
    {
        var r = new AvaRect(p.X - HandleSize / 2, p.Y - HandleSize / 2, HandleSize, HandleSize);
        context.DrawRectangle(s_handleFill, s_handleStroke, r);
    }

    // axis is the corner→corner direction vector. Draws a slim rectangle along that axis (so it tracks rotation).
    private static void DrawEdgeHandle(DrawingContext context, AvaPoint p, Avalonia.Vector axis)
    {
        double len = axis.Length;
        if (len < 1e-6)
        {
            DrawCornerHandle(context, p);
            return;
        }
        double angle = Math.Atan2(axis.Y, axis.X);
        using (context.PushTransform(Matrix.CreateRotation(angle) * Matrix.CreateTranslation(p.X, p.Y)))
        {
            var r = new AvaRect(-EdgeHandleLength / 2, -HandleSize / 2, EdgeHandleLength, HandleSize);
            context.DrawRectangle(s_handleFill, s_handleStroke, r);
        }
    }

    public HandleKind HitTest(AvaPoint imagePoint)
    {
        if (!HasShape) return HandleKind.None;

        double tol = HandleSize / 2 + 2;
        double centerTol = CenterMarkerRadius + 2;

        if (AvaPoint.Distance(imagePoint, _pivotImage) <= centerTol)
            return HandleKind.Center;

        if (AvaPoint.Distance(imagePoint, _imageCorners[0]) <= tol) return HandleKind.TopLeft;
        if (AvaPoint.Distance(imagePoint, _imageCorners[1]) <= tol) return HandleKind.TopRight;
        if (AvaPoint.Distance(imagePoint, _imageCorners[2]) <= tol) return HandleKind.BottomRight;
        if (AvaPoint.Distance(imagePoint, _imageCorners[3]) <= tol) return HandleKind.BottomLeft;

        if (HitEdge(imagePoint, _imageCorners[0], _imageCorners[1])) return HandleKind.Top;
        if (HitEdge(imagePoint, _imageCorners[1], _imageCorners[2])) return HandleKind.Right;
        if (HitEdge(imagePoint, _imageCorners[2], _imageCorners[3])) return HandleKind.Bottom;
        if (HitEdge(imagePoint, _imageCorners[3], _imageCorners[0])) return HandleKind.Left;

        // Rotate: within RotateOuterDistance from a corner and outside the polygon.
        if (!IsPointInsidePolygon(imagePoint))
        {
            for (int i = 0; i < 4; i++)
            {
                if (AvaPoint.Distance(imagePoint, _imageCorners[i]) <= RotateOuterDistance)
                    return HandleKind.Rotate;
            }
        }

        // Return None for clicks inside the polygon so they pass through to the normal renderer hit-test
        // (toggling selection between overlapping objects, double-click on a shape to start path editing).
        // Translate operations go through the central pivot marker (Center handle).
        return HandleKind.None;
    }

    private static bool HitEdge(AvaPoint p, AvaPoint a, AvaPoint b)
    {
        AvaVector edge = b - a;
        double len = edge.Length;
        if (len < 1e-6) return false;
        AvaVector u = edge / len;
        AvaPoint mid = new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        AvaVector d = p - mid;
        double along = d.X * u.X + d.Y * u.Y;
        double perp = d.X * -u.Y + d.Y * u.X;
        return Math.Abs(along) <= EdgeHandleLength / 2 + 2 && Math.Abs(perp) <= HandleSize / 2 + 2;
    }

    private bool IsPointInsidePolygon(AvaPoint p)
    {
        bool inside = false;
        for (int i = 0, j = 3; i < 4; j = i++)
        {
            AvaPoint pi = _imageCorners[i];
            AvaPoint pj = _imageCorners[j];
            if (((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y + 1e-12) + pi.X))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    public static Cursor GetCursorForHandle(HandleKind kind)
    {
        return kind switch
        {
            HandleKind.TopLeft or HandleKind.BottomRight => s_cursorCorner1,
            HandleKind.TopRight or HandleKind.BottomLeft => s_cursorCorner2,
            HandleKind.Top or HandleKind.Bottom => s_cursorNS,
            HandleKind.Left or HandleKind.Right => s_cursorWE,
            HandleKind.Rotate => s_cursorRotate,
            HandleKind.Center => s_cursorMove,
            _ => Cursor.Default
        };
    }
}


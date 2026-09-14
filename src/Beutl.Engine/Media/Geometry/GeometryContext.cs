using Beutl.Graphics;

using SkiaSharp;

namespace Beutl.Media;

public sealed class GeometryContext : IGeometryContext, IDisposable
{
    private SKPathBuilder _builder = new();
    // Holds the geometry, with the builder left empty, once it has been detached to be read or transformed.
    private SKPath? _path;
    private bool _pathHandedOut;
    // Paths NativeObject handed out before a later edit replaced them; a caller may still hold one.
    private List<SKPath>? _supersededPaths;

    public GeometryContext()
    {

    }

    /// <summary>
    /// Gets the path built so far.
    /// </summary>
    /// <remarks>
    /// The context owns the returned path. An edit made after reading this property continues from it but is not
    /// reflected in the instance already returned, so read the property again to observe the edit.
    /// </remarks>
    public SKPath NativeObject
    {
        get
        {
            _pathHandedOut = true;
            return _path ??= _builder.Detach();
        }
    }

    public bool IsDisposed { get; private set; }

    public PathFillType FillType
    {
        get => (PathFillType)(_path?.FillType ?? _builder.FillType);
        set
        {
            // A path NativeObject handed out keeps its fill type, so the edit resumes the builder from a copy.
            if (_path != null && !_pathHandedOut)
                _path.FillType = (SKPathFillType)value;
            else
                Builder.FillType = (SKPathFillType)value;
        }
    }

    public Point LastPoint
    {
        get
        {
            if (_path != null)
                return _path.LastPoint.ToGraphicsPoint();

            using SKPath snapshot = _builder.Snapshot();
            return snapshot.LastPoint.ToGraphicsPoint();
        }
    }

    public Rect Bounds
    {
        get
        {
            if (_path != null)
                return _path.TightBounds.ToGraphicsRect();

            using SKPath snapshot = _builder.Snapshot();
            return snapshot.TightBounds.ToGraphicsRect();
        }
    }

    // The builder to edit, resumed from the detached path when there is one.
    private SKPathBuilder Builder
    {
        get
        {
            if (_path != null)
            {
                var resumed = new SKPathBuilder(_path);
                _builder.Dispose();
                _builder = resumed;
                ReleasePath();
            }

            return _builder;
        }
    }

    public void ArcTo(Size radius, float angle, bool isLargeArc, bool sweepClockwise, Point point)
    {
        Builder.ArcTo(
            r: new SKPoint(radius.Width, radius.Height),
            xAxisRotate: angle,
            largeArc: isLargeArc ? SKPathArcSize.Large : SKPathArcSize.Small,
            sweep: sweepClockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
            xy: point.ToSKPoint());
    }

    public void Clear()
    {
        ReleasePath();
        _builder.Reset();
    }

    public void Close()
    {
        Builder.Close();
    }

    public void ConicTo(Point controlPoint, Point endPoint, float weight)
    {
        Builder.ConicTo(controlPoint.ToSKPoint(), endPoint.ToSKPoint(), weight);
    }

    public void CubicTo(Point controlPoint1, Point controlPoint2, Point endPoint)
    {
        Builder.CubicTo(controlPoint1.ToSKPoint(), controlPoint2.ToSKPoint(), endPoint.ToSKPoint());
    }

    public void LineTo(Point point)
    {
        Builder.LineTo(point.ToSKPoint());
    }

    public void MoveTo(Point point)
    {
        Builder.MoveTo(point.ToSKPoint());
    }

    public void QuadraticTo(Point controlPoint, Point endPoint)
    {
        Builder.QuadTo(controlPoint.ToSKPoint(), endPoint.ToSKPoint());
    }

    public void Transform(Matrix matrix)
    {
        // SKPathBuilder cannot transform, so the geometry is transformed as a path, and a path NativeObject handed
        // out is copied first so it keeps its geometry.
        if (_path == null)
        {
            _path = _builder.Detach();
        }
        else if (_pathHandedOut)
        {
            var copy = new SKPath(_path);
            ReleasePath();
            _path = copy;
        }

        _path.Transform(matrix.ToSKMatrix());
    }

    internal void AddPath(SKPath path)
    {
        Builder.AddPath(path);
    }

    private void ReleasePath()
    {
        if (_path == null)
            return;

        if (_pathHandedOut)
            (_supersededPaths ??= []).Add(_path);
        else
            _path.Dispose();

        _path = null;
        _pathHandedOut = false;
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _builder.Dispose();
            _path?.Dispose();
            if (_supersededPaths != null)
            {
                foreach (SKPath path in _supersededPaths)
                    path.Dispose();
            }

            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }
}

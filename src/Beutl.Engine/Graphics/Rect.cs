using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.Unicode;

using Beutl.Converters;
using Beutl.Utilities;

namespace Beutl.Graphics;

[JsonConverter(typeof(ParsableJsonConverter<Rect>))]
[TypeConverter(typeof(ParsableTypeConverter<Rect>))]
public readonly struct Rect
    : IEquatable<Rect>,
      IParsable<Rect>,
      ISpanParsable<Rect>,
      ISpanFormattable,
      IUtf8SpanParsable<Rect>,
      IUtf8SpanFormattable,
      IEqualityOperators<Rect, Rect, bool>,
      IMultiplyOperators<Rect, Vector, Rect>,
      IMultiplyOperators<Rect, float, Rect>,
      IDivisionOperators<Rect, Vector, Rect>,
      ITupleConvertible<Rect, float>
{
    /// <summary>The default homogeneous clipping divisor used by <see cref="TransformToAABB"/>.</summary>
    /// <remarks>
    /// This allocation-safe bound is larger than <see cref="RasterizerNearPlane"/> and may exclude pixels drawn
    /// close to the camera plane. Use <see cref="TransformToDeliveredAABB"/> when the delivery region is known.
    /// </remarks>
    public const float DefaultNearPlane = 0.05f;

    /// <summary>
    /// The homogeneous divisor the rasterizer itself clips perspective geometry at — Skia's
    /// <c>SkPathPriv::kW0PlaneDistance</c>, <c>1 / (1 &lt;&lt; 14)</c>, applied by both
    /// <c>SkPathPriv::PerspectiveClip</c> and Ganesh's <c>GrQuadUtils::ClipToW0</c>. A box clipped here
    /// contains every pixel that can be drawn; see <see cref="DefaultNearPlane"/> for why that exactness
    /// is not affordable as the default.
    /// </summary>
    public const float RasterizerNearPlane = 1f / 16384f;

    public static readonly Rect Empty;

    /// <summary>
    /// All values are NaN rectangles.
    /// </summary>
    public static readonly Rect Invalid = new(float.NaN, float.NaN, float.NaN, float.NaN);

    public Rect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public Rect(Size size)
    {
        X = 0;
        Y = 0;
        Width = size.Width;
        Height = size.Height;
    }

    public Rect(Point position, Size size)
    {
        X = position.X;
        Y = position.Y;
        Width = size.Width;
        Height = size.Height;
    }

    public Rect(Point topLeft, Point bottomRight)
    {
        X = topLeft.X;
        Y = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public Point Position => new(X, Y);

    public Size Size => new(Width, Height);

    public float Right => X + Width;

    public float Bottom => Y + Height;

    public float Left => X;

    public float Top => Y;

    public Point TopLeft => new(X, Y);

    public Point TopRight => new(Right, Y);

    public Point BottomLeft => new(X, Bottom);

    public Point BottomRight => new(Right, Bottom);

    public Point Center => new(X + (Width / 2), Y + (Height / 2));

    public bool IsEmpty => Width == 0 && Height == 0;

    /// <summary>
    /// Gets the value of whether any of the properties are NaN.
    /// </summary>
    public bool IsInvalid => float.IsNaN(Width) || float.IsNaN(Height) || float.IsNaN(X) || float.IsNaN(Y);

    static int ITupleConvertible<Rect, float>.TupleLength => 4;

    public static bool operator ==(Rect left, Rect right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Rect left, Rect right)
    {
        return !(left == right);
    }

    public static Rect operator *(Rect rect, Vector scale)
    {
        return new Rect(
            rect.X * scale.X,
            rect.Y * scale.Y,
            rect.Width * scale.X,
            rect.Height * scale.Y);
    }

    public static Rect operator *(Rect rect, float scale)
    {
        return new Rect(
            rect.X * scale,
            rect.Y * scale,
            rect.Width * scale,
            rect.Height * scale);
    }

    public static Rect operator /(Rect rect, Vector scale)
    {
        return new Rect(
            rect.X / scale.X,
            rect.Y / scale.Y,
            rect.Width / scale.X,
            rect.Height / scale.Y);
    }

    public bool Contains(Point p)
    {
        return p.X >= X && p.X <= X + Width &&
               p.Y >= Y && p.Y <= Y + Height;
    }

    /// <summary>
    /// Determines whether a point is in the bounds of the rectangle, exclusive of the
    /// rectangle's bottom/right edge.
    /// </summary>
    /// <param name="p">The point.</param>
    /// <returns>true if the point is in the bounds of the rectangle; otherwise false.</returns>
    public bool ContainsExclusive(Point p)
    {
        return p.X >= X && p.X < X + Width &&
               p.Y >= Y && p.Y < Y + Height;
    }

    public bool Contains(Rect r)
    {
        return Contains(r.TopLeft) && Contains(r.BottomRight);
    }

    /// <summary>
    /// Centers another rectangle in this rectangle.
    /// </summary>
    /// <param name="rect">The rectangle to center.</param>
    /// <returns>The centered rectangle.</returns>
    public Rect CenterRect(Rect rect)
    {
        return new Rect(
            X + ((Width - rect.Width) / 2),
            Y + ((Height - rect.Height) / 2),
            rect.Width,
            rect.Height);
    }

    public Rect Inflate(float thickness)
    {
        return Inflate(new Thickness(thickness));
    }

    public Rect Inflate(Thickness thickness)
    {
        return new Rect(
            new Point(X - thickness.Left, Y - thickness.Top),
            Size.Inflate(thickness));
    }

    public Rect Deflate(float thickness)
    {
        return Deflate(new Thickness(thickness));
    }

    public Rect Deflate(Thickness thickness)
    {
        return new Rect(
            new Point(X + thickness.Left, Y + thickness.Top),
            Size.Deflate(thickness));
    }

    public bool Equals(Rect other)
    {
        return X == other.X &&
               Y == other.Y &&
               Width == other.Width &&
               Height == other.Height;
    }

    public override bool Equals(object? obj)
    {
        return obj is Rect other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Width, Height);
    }

    public Rect Intersect(Rect rect)
    {
        float newLeft = (rect.X > X) ? rect.X : X;
        float newTop = (rect.Y > Y) ? rect.Y : Y;
        float newRight = (rect.Right < Right) ? rect.Right : Right;
        float newBottom = (rect.Bottom < Bottom) ? rect.Bottom : Bottom;

        if ((newRight > newLeft) && (newBottom > newTop))
        {
            return new Rect(newLeft, newTop, newRight - newLeft, newBottom - newTop);
        }
        else
        {
            return Empty;
        }
    }

    public bool Intersects(Rect rect)
    {
        return (rect.X < Right) && (X < rect.Right) && (rect.Y < Bottom) && (Y < rect.Bottom);
    }

    /// <summary>
    /// The box around the four mapped corners, exact only on one side of the <c>w = 0</c> plane: across it the corners
    /// reflect and the box lands opposite the image, so <see cref="TransformToAABB"/> checks that first.
    /// </summary>
    internal Rect TransformToMappedCornerAABB(Matrix matrix)
    {
        ReadOnlySpan<Point> points =
        [
            TopLeft.Transform(matrix),
            TopRight.Transform(matrix),
            BottomRight.Transform(matrix),
            BottomLeft.Transform(matrix)
        ];

        return BoundsOf(points);
    }

    /// <summary>The smallest rectangle containing every point of <paramref name="points"/>.</summary>
    private static Rect BoundsOf(ReadOnlySpan<Point> points)
    {
        float left = float.MaxValue;
        float right = float.MinValue;
        float top = float.MaxValue;
        float bottom = float.MinValue;

        foreach (Point p in points)
        {
            if (p.X < left) left = p.X;
            if (p.X > right) right = p.X;
            if (p.Y < top) top = p.Y;
            if (p.Y > bottom) bottom = p.Y;
        }

        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    /// <summary>
    /// Maps this rectangle through <paramref name="matrix"/>, exact inside <paramref name="deliveredTo"/> so nothing
    /// drawn there is given up, and outside it never larger than the <see cref="DefaultNearPlane"/> box, so the working
    /// density it costs is unchanged. Without a delivery region the <see cref="DefaultNearPlane"/> box is returned.
    /// </summary>
    public Rect TransformToDeliveredAABB(Matrix matrix, Rect? deliveredTo)
    {
        Rect pragmatic = TransformToAABB(matrix);
        if (deliveredTo is not { } delivered)
            return pragmatic;

        return TransformToAABB(matrix, RasterizerNearPlane).Intersect(pragmatic.Union(delivered));
    }

    /// <summary>
    /// Returns an axis-aligned bounding box of this rectangle transformed by <paramref name="matrix"/>,
    /// with the part behind the matrix's camera plane clipped away first.
    /// </summary>
    /// <param name="matrix">The transform.</param>
    /// <param name="nearPlane">The finite positive homogeneous divisor cutoff. Lower values widen the box.</param>
    /// <returns>
    /// The bounding box, or <see cref="Empty"/> when nothing reaches the cutoff. The default cutoff may exclude
    /// content that the rasterizer still draws.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="nearPlane"/> is not finite and positive.
    /// </exception>
    public Rect TransformToAABB(Matrix matrix, float nearPlane = DefaultNearPlane)
    {
        // A sign test cannot carry this on its own: only some NaNs have the sign bit that
        // ArgumentOutOfRangeException.ThrowIfNegativeOrZero reads.
        if (!float.IsFinite(nearPlane) || nearPlane <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nearPlane), nearPlane, "The near plane must be finite and positive.");
        }

        if (!matrix.ContainsPerspective())
            return TransformToMappedCornerAABB(matrix);

        ReadOnlySpan<Point> corners = [TopLeft, TopRight, BottomRight, BottomLeft];
        Span<float> divisors = stackalloc float[4];
        float min = float.MaxValue;
        float max = float.MinValue;
        for (int i = 0; i < corners.Length; i++)
        {
            float w = matrix.GetTransformDivisor(corners[i]);
            divisors[i] = w;
            if (w < min) min = w;
            if (w > max) max = w;
        }

        // One divisor sign at the corners keeps the singularity outside, but a corner nearer than the cutoff still maps
        // unbounded, so the box is taken whole only when every corner reaches the cutoff or all lie behind the eye.
        if (min >= nearPlane || max < 0)
            return TransformToMappedCornerAABB(matrix);

        Span<Point> clipped = stackalloc Point[8];
        int count = 0;
        for (int i = 0; i < corners.Length; i++)
        {
            int next = (i + 1) % corners.Length;
            float w = divisors[i];
            float nextW = divisors[next];
            if (w >= nearPlane)
                clipped[count++] = corners[i];
            if ((w < nearPlane) != (nextW < nearPlane))
                clipped[count++] = corners[i] + ((corners[next] - corners[i]) * ((nearPlane - w) / (nextW - w)));
        }

        if (count == 0)
            return Empty;

        for (int i = 0; i < count; i++)
            clipped[i] = clipped[i].Transform(matrix);

        return BoundsOf(clipped[..count]);
    }

    public Rect Translate(Vector offset)
    {
        return new Rect(Position + offset, Size);
    }

    /// <summary>
    /// Normalizes the rectangle so both the <see cref="Width"/> and <see
    /// cref="Height"/> are positive, without changing the location of the rectangle
    /// </summary>
    /// <returns>Normalized Rect</returns>
    /// <remarks>
    /// Empty rect will be return when Rect contains invalid values. Like NaN.
    /// </remarks>
    public Rect Normalize()
    {
        Rect rect = this;

        if (float.IsNaN(rect.Right) || float.IsNaN(rect.Bottom) ||
            float.IsNaN(rect.X) || float.IsNaN(rect.Y) ||
            float.IsNaN(Height) || float.IsNaN(Width))
        {
            return Empty;
        }

        if (rect.Width < 0)
        {
            float x = X + Width;
            float width = X - x;

            rect = rect.WithX(x).WithWidth(width);
        }

        if (rect.Height < 0)
        {
            float y = Y + Height;
            float height = Y - y;

            rect = rect.WithY(y).WithHeight(height);
        }

        return rect;
    }

    public Rect Union(Rect rect)
    {
        if (IsEmpty)
        {
            return rect;
        }
        else if (rect.IsEmpty)
        {
            return this;
        }
        else
        {
            float x1 = MathF.Min(X, rect.X);
            float x2 = MathF.Max(Right, rect.Right);
            float y1 = MathF.Min(Y, rect.Y);
            float y2 = MathF.Max(Bottom, rect.Bottom);

            return new Rect(new Point(x1, y1), new Point(x2, y2));
        }
    }

    public Rect Union(Point point)
    {
        float x1 = MathF.Min(X, point.X);
        float x2 = MathF.Max(Right, point.X);
        float y1 = MathF.Min(Y, point.Y);
        float y2 = MathF.Max(Bottom, point.Y);
        return new Rect(new Point(x1, y1), new Point(x2, y2));
    }

    public Rect WithX(float x)
    {
        return new Rect(x, Y, Width, Height);
    }

    public Rect WithY(float y)
    {
        return new Rect(X, y, Width, Height);
    }

    public Rect WithWidth(float width)
    {
        return new Rect(X, Y, width, Height);
    }

    public Rect WithHeight(float height)
    {
        return new Rect(X, Y, Width, height);
    }

    public override string ToString()
    {
        return FormattableString.Invariant($"{X}, {Y}, {Width}, {Height}");
    }

    public static bool TryParse(string? s, out Rect rect)
    {
        return TryParse(s.AsSpan(), out rect);
    }

    public static bool TryParse(ReadOnlySpan<char> s, out Rect rect)
    {
        return TryParse(s, null, out rect);
    }

    public static Rect Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    public static Rect Parse(ReadOnlySpan<char> s)
    {
        return Parse(s, null);
    }

    public static Rect Parse(string s, IFormatProvider? provider)
    {
        return Parse(s.AsSpan(), provider);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Rect result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }

    public static Rect Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        using var tokenizer = new RefStringTokenizer(s, provider ?? CultureInfo.InvariantCulture, exceptionMessage: "Invalid Rect.");
        return new Rect(
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle()
        );
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Rect result)
    {
        try
        {
            result = Parse(s, provider);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
        return MemoryExtensions.TryWrite(destination, provider, $"{X}{separator} {Y}{separator} {Width}{separator} {Height}", out charsWritten);
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return ToString(formatProvider);
    }

    public string ToString(IFormatProvider? formatProvider)
    {
        if (formatProvider == null)
        {
            return ToString();
        }
        else
        {
            char separator = TokenizerHelper.GetSeparatorFromFormatProvider(formatProvider);
            return string.Create(formatProvider, $"{X}{separator} {Y}{separator} {Width}{separator} {Height}");
        }
    }

    static void ITupleConvertible<Rect, float>.ConvertTo(Rect self, Span<float> tuple)
    {
        tuple[0] = self.X;
        tuple[1] = self.Y;
        tuple[2] = self.Width;
        tuple[3] = self.Height;
    }

    static void ITupleConvertible<Rect, float>.ConvertFrom(Span<float> tuple, out Rect self)
    {
        self = new Rect(tuple[0], tuple[1], tuple[2], tuple[3]);
    }

    public static Rect Parse(ReadOnlySpan<byte> utf8Text)
    {
        return Parse(utf8Text, null);
    }

    public static Rect Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        using var tokenizer = new RefUtf8StringTokenizer(utf8Text, provider ?? CultureInfo.InvariantCulture, exceptionMessage: "Invalid Rect.");
        return new Rect(
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle()
        );
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Rect result)
    {
        return TryParse(utf8Text, null, out result);
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Rect result)
    {
        try
        {
            result = Parse(utf8Text, provider);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
        return Utf8.TryWrite(utf8Destination, provider, $"{X}{separator} {Y}{separator} {Width}{separator} {Height}", out bytesWritten);
    }
}

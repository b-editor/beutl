using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.Unicode;

using Beutl.Converters;
using Beutl.Utilities;

namespace Beutl.Graphics;

/// <summary>
/// Defines a rectangle.
/// </summary>
[JsonConverter(typeof(RectJsonConverter))]
[TypeConverter(typeof(RectConverter))]
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

    /// <summary>
    /// An empty rectangle.
    /// </summary>
    public static readonly Rect Empty;

    /// <summary>
    /// All values are NaN rectangles.
    /// </summary>
    public static readonly Rect Invalid = new(float.NaN, float.NaN, float.NaN, float.NaN);

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> structure.
    /// </summary>
    /// <param name="x">The X position.</param>
    /// <param name="y">The Y position.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    public Rect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> structure.
    /// </summary>
    /// <param name="size">The size of the rectangle.</param>
    public Rect(Size size)
    {
        X = 0;
        Y = 0;
        Width = size.Width;
        Height = size.Height;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> structure.
    /// </summary>
    /// <param name="position">The position of the rectangle.</param>
    /// <param name="size">The size of the rectangle.</param>
    public Rect(Point position, Size size)
    {
        X = position.X;
        Y = position.Y;
        Width = size.Width;
        Height = size.Height;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> structure.
    /// </summary>
    /// <param name="topLeft">The top left position of the rectangle.</param>
    /// <param name="bottomRight">The bottom right position of the rectangle.</param>
    public Rect(Point topLeft, Point bottomRight)
    {
        X = topLeft.X;
        Y = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;
    }

    /// <summary>
    /// Gets the X position.
    /// </summary>
    public float X { get; }

    /// <summary>
    /// Gets the Y position.
    /// </summary>
    public float Y { get; }

    /// <summary>
    /// Gets the width.
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets the height.
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Gets the position of the rectangle.
    /// </summary>
    public Point Position => new(X, Y);

    /// <summary>
    /// Gets the size of the rectangle.
    /// </summary>
    public Size Size => new(Width, Height);

    /// <summary>
    /// Gets the right position of the rectangle.
    /// </summary>
    public float Right => X + Width;

    /// <summary>
    /// Gets the bottom position of the rectangle.
    /// </summary>
    public float Bottom => Y + Height;

    /// <summary>
    /// Gets the left position.
    /// </summary>
    public float Left => X;

    /// <summary>
    /// Gets the top position.
    /// </summary>
    public float Top => Y;

    /// <summary>
    /// Gets the top left point of the rectangle.
    /// </summary>
    public Point TopLeft => new(X, Y);

    /// <summary>
    /// Gets the top right point of the rectangle.
    /// </summary>
    public Point TopRight => new(Right, Y);

    /// <summary>
    /// Gets the bottom left point of the rectangle.
    /// </summary>
    public Point BottomLeft => new(X, Bottom);

    /// <summary>
    /// Gets the bottom right point of the rectangle.
    /// </summary>
    public Point BottomRight => new(Right, Bottom);

    /// <summary>
    /// Gets the center point of the rectangle.
    /// </summary>
    public Point Center => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>
    /// Gets a value that indicates whether the rectangle is empty.
    /// </summary>
    public bool IsEmpty => Width == 0 && Height == 0;

    /// <summary>
    /// Gets the value of whether any of the properties are NaN.
    /// </summary>
    public bool IsInvalid => float.IsNaN(Width) || float.IsNaN(Height) || float.IsNaN(X) || float.IsNaN(Y);

    static int ITupleConvertible<Rect, float>.TupleLength => 4;

    /// <summary>
    /// Checks for equality between two <see cref="Rect"/>s.
    /// </summary>
    /// <param name="left">The first rect.</param>
    /// <param name="right">The second rect.</param>
    /// <returns>True if the rects are equal; otherwise false.</returns>
    public static bool operator ==(Rect left, Rect right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks for inequality between two <see cref="Rect"/>s.
    /// </summary>
    /// <param name="left">The first rect.</param>
    /// <param name="right">The second rect.</param>
    /// <returns>True if the rects are unequal; otherwise false.</returns>
    public static bool operator !=(Rect left, Rect right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Multiplies a rectangle by a scaling vector.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="scale">The vector scale.</param>
    /// <returns>The scaled rectangle.</returns>
    public static Rect operator *(Rect rect, Vector scale)
    {
        return new Rect(
            rect.X * scale.X,
            rect.Y * scale.Y,
            rect.Width * scale.X,
            rect.Height * scale.Y);
    }

    /// <summary>
    /// Multiplies a rectangle by a scale.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="scale">The scale.</param>
    /// <returns>The scaled rectangle.</returns>
    public static Rect operator *(Rect rect, float scale)
    {
        return new Rect(
            rect.X * scale,
            rect.Y * scale,
            rect.Width * scale,
            rect.Height * scale);
    }

    /// <summary>
    /// Divides a rectangle by a vector.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="scale">The vector scale.</param>
    /// <returns>The scaled rectangle.</returns>
    public static Rect operator /(Rect rect, Vector scale)
    {
        return new Rect(
            rect.X / scale.X,
            rect.Y / scale.Y,
            rect.Width / scale.X,
            rect.Height / scale.Y);
    }

    /// <summary>
    /// Determines whether a point in in the bounds of the rectangle.
    /// </summary>
    /// <param name="p">The point.</param>
    /// <returns>true if the point is in the bounds of the rectangle; otherwise false.</returns>
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

    /// <summary>
    /// Determines whether the rectangle fully contains another rectangle.
    /// </summary>
    /// <param name="r">The rectangle.</param>
    /// <returns>true if the rectangle is fully contained; otherwise false.</returns>
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

    /// <summary>
    /// Inflates the rectangle.
    /// </summary>
    /// <param name="thickness">The thickness to be subtracted for each side of the rectangle.</param>
    /// <returns>The inflated rectangle.</returns>
    public Rect Inflate(float thickness)
    {
        return Inflate(new Thickness(thickness));
    }

    /// <summary>
    /// Inflates the rectangle.
    /// </summary>
    /// <param name="thickness">The thickness to be subtracted for each side of the rectangle.</param>
    /// <returns>The inflated rectangle.</returns>
    public Rect Inflate(Thickness thickness)
    {
        return new Rect(
            new Point(X - thickness.Left, Y - thickness.Top),
            Size.Inflate(thickness));
    }

    /// <summary>
    /// Deflates the rectangle.
    /// </summary>
    /// <param name="thickness">The thickness to be subtracted for each side of the rectangle.</param>
    /// <returns>The deflated rectangle.</returns>
    public Rect Deflate(float thickness)
    {
        return Deflate(new Thickness(thickness));
    }

    /// <summary>
    /// Deflates the rectangle by a <see cref="Thickness"/>.
    /// </summary>
    /// <param name="thickness">The thickness to be subtracted for each side of the rectangle.</param>
    /// <returns>The deflated rectangle.</returns>
    public Rect Deflate(Thickness thickness)
    {
        return new Rect(
            new Point(X + thickness.Left, Y + thickness.Top),
            Size.Deflate(thickness));
    }

    /// <summary>
    /// Returns a boolean indicating whether the rect is equal to the other given rect.
    /// </summary>
    /// <param name="other">The other rect to test equality against.</param>
    /// <returns>True if this rect is equal to other; False otherwise.</returns>
    public bool Equals(Rect other)
    {
        return X == other.X &&
               Y == other.Y &&
               Width == other.Width &&
               Height == other.Height;
    }

    /// <summary>
    /// Returns a boolean indicating whether the given object is equal to this rectangle.
    /// </summary>
    /// <param name="obj">The object to compare against.</param>
    /// <returns>True if the object is equal to this rectangle; false otherwise.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Rect other && Equals(other);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Width, Height);
    }

    /// <summary>
    /// Gets the intersection of two rectangles.
    /// </summary>
    /// <param name="rect">The other rectangle.</param>
    /// <returns>The intersection.</returns>
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

    /// <summary>
    /// Determines whether a rectangle intersects with this rectangle.
    /// </summary>
    /// <param name="rect">The other rectangle.</param>
    /// <returns>
    /// True if the specified rectangle intersects with this one; otherwise false.
    /// </returns>
    public bool Intersects(Rect rect)
    {
        return (rect.X < Right) && (X < rect.Right) && (rect.Y < Bottom) && (Y < rect.Bottom);
    }

    /// <summary>
    /// The box around the four mapped corners, with no camera-plane handling.
    /// </summary>
    /// <remarks>
    /// A projective map takes lines to lines, so this box is exact — but only while the rectangle stays
    /// on one side of <paramref name="matrix"/>'s <c>w = 0</c> plane. A rectangle that crosses that
    /// plane has an unbounded image and its behind-plane corners are point-reflected through the origin,
    /// so the box lands on the wrong side of the image rather than containing it. That is why this is
    /// not the public answer: <see cref="TransformToAABB"/> establishes the precondition and then calls
    /// this.
    /// </remarks>
    internal Rect TransformToMappedCornerAABB(Matrix matrix)
    {
        ReadOnlySpan<Point> points =
        [
            TopLeft.Transform(matrix),
            TopRight.Transform(matrix),
            BottomRight.Transform(matrix),
            BottomLeft.Transform(matrix)
        ];

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
    /// Maps this rectangle through <paramref name="matrix"/> covering every pixel the rasterizer can draw,
    /// then keeps whatever of that box either reaches <paramref name="deliveredTo"/> or would have been
    /// declared at <see cref="DefaultNearPlane"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A perspective-mapped rectangle's exact box runs to millions of pixels as the near edge tips towards
    /// the eye, and <see cref="RenderScaleUtilities.ClampWorkingScaleToBufferBudget"/> pays for that in
    /// working density. <see cref="DefaultNearPlane"/> bought the density back by giving up a wedge the
    /// rasterizer still draws - and, when the wedge falls inside the frame, that wedge is content the viewer
    /// should have seen.
    /// </para>
    /// <para>
    /// The union is what makes both affordable: inside <paramref name="deliveredTo"/> the box is exact, so
    /// nothing drawn there is given up, and outside it the box is never larger than
    /// <see cref="DefaultNearPlane"/> already made it, so the density it costs is unchanged. Without a
    /// delivery region there is no output clip to be exact against and the pragmatic box is returned.
    /// </para>
    /// </remarks>
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

        // The divisor is affine over the rectangle, so a single sign at the corners means the singularity
        // lies outside it and the mapped-corner box is already exact. A rectangle wholly in front of the eye
        // is still measured against the cutoff rather than against zero: when even its farthest corner falls
        // short of the cutoff, nothing reaches it and the clip below keeps nothing.
        if ((min > 0 && max >= nearPlane) || max < 0)
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

        float left = float.MaxValue;
        float right = float.MinValue;
        float top = float.MaxValue;
        float bottom = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            Point p = clipped[i].Transform(matrix);
            if (p.X < left) left = p.X;
            if (p.X > right) right = p.X;
            if (p.Y < top) top = p.Y;
            if (p.Y > bottom) bottom = p.Y;
        }

        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    /// <summary>
    /// Translates the rectangle by an offset.
    /// </summary>
    /// <param name="offset">The offset.</param>
    /// <returns>The translated rectangle.</returns>
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

    /// <summary>
    /// Gets the union of two rectangles.
    /// </summary>
    /// <param name="rect">The other rectangle.</param>
    /// <returns>The union.</returns>
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

    /// <summary>
    /// Gets the union of this rectangle and the specified point.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The union.</returns>
    public Rect Union(Point point)
    {
        float x1 = MathF.Min(X, point.X);
        float x2 = MathF.Max(Right, point.X);
        float y1 = MathF.Min(Y, point.Y);
        float y2 = MathF.Max(Bottom, point.Y);
        return new Rect(new Point(x1, y1), new Point(x2, y2));
    }

    /// <summary>
    /// Returns a new <see cref="Rect"/> with the specified X position.
    /// </summary>
    /// <param name="x">The x position.</param>
    /// <returns>The new <see cref="Rect"/>.</returns>
    public Rect WithX(float x)
    {
        return new Rect(x, Y, Width, Height);
    }

    /// <summary>
    /// Returns a new <see cref="Rect"/> with the specified Y position.
    /// </summary>
    /// <param name="y">The y position.</param>
    /// <returns>The new <see cref="Rect"/>.</returns>
    public Rect WithY(float y)
    {
        return new Rect(X, y, Width, Height);
    }

    /// <summary>
    /// Returns a new <see cref="Rect"/> with the specified width.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <returns>The new <see cref="Rect"/>.</returns>
    public Rect WithWidth(float width)
    {
        return new Rect(X, Y, width, Height);
    }

    /// <summary>
    /// Returns a new <see cref="Rect"/> with the specified height.
    /// </summary>
    /// <param name="height">The height.</param>
    /// <returns>The new <see cref="Rect"/>.</returns>
    public Rect WithHeight(float height)
    {
        return new Rect(X, Y, Width, height);
    }

    /// <summary>
    /// Returns the string representation of the rectangle.
    /// </summary>
    /// <returns>The string representation of the rectangle.</returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"{X}, {Y}, {Width}, {Height}");
    }

    /// <summary>
    /// Parses a <see cref="Rect"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="rect">The <see cref="Rect"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string? s, out Rect rect)
    {
        return TryParse(s.AsSpan(), out rect);
    }

    /// <summary>
    /// Parses a <see cref="Rect"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="rect">The <see cref="Rect"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out Rect rect)
    {
        return TryParse(s, null, out rect);
    }

    /// <summary>
    /// Parses a <see cref="Rect"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The parsed <see cref="Rect"/>.</returns>
    public static Rect Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    /// <summary>
    /// Parses a <see cref="Rect"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The parsed <see cref="Rect"/>.</returns>
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
        return TryParse(utf8Text, out result);
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


#pragma warning disable IDE0001, IDE0049

namespace BeUtl.Validation
{
    // Vector2
    public sealed class PixelPointRangeValidator : RangeValidator<BeUtl.Media.PixelPoint>
    {
        public PixelPointRangeValidator()
        {
            Maximum = new(System.Int32.MaxValue, System.Int32.MaxValue);
            Minimum = new(System.Int32.MinValue, System.Int32.MinValue);
        }

        public override BeUtl.Media.PixelPoint Coerce(ICoreObject? obj, BeUtl.Media.PixelPoint value)
        {
            return new BeUtl.Media.PixelPoint(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
        }
        
        public override bool Validate(ICoreObject? obj, BeUtl.Media.PixelPoint value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y;
        }
    }
    public sealed class PixelSizeRangeValidator : RangeValidator<BeUtl.Media.PixelSize>
    {
        public PixelSizeRangeValidator()
        {
            Maximum = new(System.Int32.MaxValue, System.Int32.MaxValue);
            Minimum = new(System.Int32.MinValue, System.Int32.MinValue);
        }

        public override BeUtl.Media.PixelSize Coerce(ICoreObject? obj, BeUtl.Media.PixelSize value)
        {
            return new BeUtl.Media.PixelSize(
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
        }
        
        public override bool Validate(ICoreObject? obj, BeUtl.Media.PixelSize value)
        {
            return value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height;
        }
    }
    public sealed class PointRangeValidator : RangeValidator<BeUtl.Graphics.Point>
    {
        public PointRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override BeUtl.Graphics.Point Coerce(ICoreObject? obj, BeUtl.Graphics.Point value)
        {
            return new BeUtl.Graphics.Point(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
        }
        
        public override bool Validate(ICoreObject? obj, BeUtl.Graphics.Point value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y;
        }
    }
    public sealed class SizeRangeValidator : RangeValidator<BeUtl.Graphics.Size>
    {
        public SizeRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override BeUtl.Graphics.Size Coerce(ICoreObject? obj, BeUtl.Graphics.Size value)
        {
            return new BeUtl.Graphics.Size(
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
        }
        
        public override bool Validate(ICoreObject? obj, BeUtl.Graphics.Size value)
        {
            return value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height;
        }
    }
    public sealed class VectorRangeValidator : RangeValidator<BeUtl.Graphics.Vector>
    {
        public VectorRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override BeUtl.Graphics.Vector Coerce(ICoreObject? obj, BeUtl.Graphics.Vector value)
        {
            return new BeUtl.Graphics.Vector(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
        }
        
        public override bool Validate(ICoreObject? obj, BeUtl.Graphics.Vector value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y;
        }
    }

    // Vector3

    // Vector4
    public sealed class PixelRectRangeValidator : RangeValidator<BeUtl.Media.PixelRect>
    {
        public PixelRectRangeValidator()
        {
            Maximum = new(System.Int32.MaxValue, System.Int32.MaxValue, System.Int32.MaxValue, System.Int32.MaxValue);
            Minimum = new(System.Int32.MinValue, System.Int32.MinValue, System.Int32.MinValue, System.Int32.MinValue);
        }

        public override BeUtl.Media.PixelRect Coerce(ICoreObject? obj, BeUtl.Media.PixelRect value)
        {
            return new BeUtl.Media.PixelRect(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
        }
        
        public override bool Validate(ICoreObject? obj, BeUtl.Media.PixelRect value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height;
        }
    }
    public sealed class CornerRadiusRangeValidator : RangeValidator<BeUtl.Media.CornerRadius>
    {
        public CornerRadiusRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue);
        }

        public override BeUtl.Media.CornerRadius Coerce(ICoreObject? obj, BeUtl.Media.CornerRadius value)
        {
            return new BeUtl.Media.CornerRadius(
                Math.Clamp(value.TopLeft, Minimum.TopLeft, Maximum.TopLeft),
                Math.Clamp(value.TopRight, Minimum.TopRight, Maximum.TopRight),
                Math.Clamp(value.BottomRight, Minimum.BottomRight, Maximum.BottomRight),
                Math.Clamp(value.BottomLeft, Minimum.BottomLeft, Maximum.BottomLeft));
        }
        
        public override bool Validate(ICoreObject? obj, BeUtl.Media.CornerRadius value)
        {
            return value.TopLeft >= Minimum.TopLeft && value.TopLeft <= Maximum.TopLeft
                && value.TopRight >= Minimum.TopRight && value.TopRight <= Maximum.TopRight
                && value.BottomRight >= Minimum.BottomRight && value.BottomRight <= Maximum.BottomRight
                && value.BottomLeft >= Minimum.BottomLeft && value.BottomLeft <= Maximum.BottomLeft;
        }
    }
    public sealed class RectRangeValidator : RangeValidator<BeUtl.Graphics.Rect>
    {
        public RectRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue);
        }

        public override BeUtl.Graphics.Rect Coerce(ICoreObject? obj, BeUtl.Graphics.Rect value)
        {
            return new BeUtl.Graphics.Rect(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
        }
        
        public override bool Validate(ICoreObject? obj, BeUtl.Graphics.Rect value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height;
        }
    }
}

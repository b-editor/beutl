
#pragma warning disable IDE0001, IDE0049

namespace Beutl.Validation
{
    // Vector2
    public sealed class PixelPointRangeValidator : RangeValidator<Beutl.Media.PixelPoint>
    {
        public PixelPointRangeValidator()
        {
            Maximum = new(System.Int32.MaxValue, System.Int32.MaxValue);
            Minimum = new(System.Int32.MinValue, System.Int32.MinValue);
        }

        public override Beutl.Media.PixelPoint Coerce(ICoreObject? obj, Beutl.Media.PixelPoint value)
        {
            return new Beutl.Media.PixelPoint(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
        }
        
        public override bool Validate(ICoreObject? obj, Beutl.Media.PixelPoint value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y;
        }
    }
    public sealed class PixelSizeRangeValidator : RangeValidator<Beutl.Media.PixelSize>
    {
        public PixelSizeRangeValidator()
        {
            Maximum = new(System.Int32.MaxValue, System.Int32.MaxValue);
            Minimum = new(System.Int32.MinValue, System.Int32.MinValue);
        }

        public override Beutl.Media.PixelSize Coerce(ICoreObject? obj, Beutl.Media.PixelSize value)
        {
            return new Beutl.Media.PixelSize(
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
        }
        
        public override bool Validate(ICoreObject? obj, Beutl.Media.PixelSize value)
        {
            return value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height;
        }
    }
    public sealed class PointRangeValidator : RangeValidator<Beutl.Graphics.Point>
    {
        public PointRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override Beutl.Graphics.Point Coerce(ICoreObject? obj, Beutl.Graphics.Point value)
        {
            return new Beutl.Graphics.Point(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
        }
        
        public override bool Validate(ICoreObject? obj, Beutl.Graphics.Point value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y;
        }
    }
    public sealed class SizeRangeValidator : RangeValidator<Beutl.Graphics.Size>
    {
        public SizeRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override Beutl.Graphics.Size Coerce(ICoreObject? obj, Beutl.Graphics.Size value)
        {
            return new Beutl.Graphics.Size(
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
        }
        
        public override bool Validate(ICoreObject? obj, Beutl.Graphics.Size value)
        {
            return value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height;
        }
    }
    public sealed class VectorRangeValidator : RangeValidator<Beutl.Graphics.Vector>
    {
        public VectorRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override Beutl.Graphics.Vector Coerce(ICoreObject? obj, Beutl.Graphics.Vector value)
        {
            return new Beutl.Graphics.Vector(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
        }
        
        public override bool Validate(ICoreObject? obj, Beutl.Graphics.Vector value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y;
        }
    }

    // Vector3

    // Vector4
    public sealed class PixelRectRangeValidator : RangeValidator<Beutl.Media.PixelRect>
    {
        public PixelRectRangeValidator()
        {
            Maximum = new(System.Int32.MaxValue, System.Int32.MaxValue, System.Int32.MaxValue, System.Int32.MaxValue);
            Minimum = new(System.Int32.MinValue, System.Int32.MinValue, System.Int32.MinValue, System.Int32.MinValue);
        }

        public override Beutl.Media.PixelRect Coerce(ICoreObject? obj, Beutl.Media.PixelRect value)
        {
            return new Beutl.Media.PixelRect(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
        }
        
        public override bool Validate(ICoreObject? obj, Beutl.Media.PixelRect value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height;
        }
    }
    public sealed class CornerRadiusRangeValidator : RangeValidator<Beutl.Media.CornerRadius>
    {
        public CornerRadiusRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue);
        }

        public override Beutl.Media.CornerRadius Coerce(ICoreObject? obj, Beutl.Media.CornerRadius value)
        {
            return new Beutl.Media.CornerRadius(
                Math.Clamp(value.TopLeft, Minimum.TopLeft, Maximum.TopLeft),
                Math.Clamp(value.TopRight, Minimum.TopRight, Maximum.TopRight),
                Math.Clamp(value.BottomRight, Minimum.BottomRight, Maximum.BottomRight),
                Math.Clamp(value.BottomLeft, Minimum.BottomLeft, Maximum.BottomLeft));
        }
        
        public override bool Validate(ICoreObject? obj, Beutl.Media.CornerRadius value)
        {
            return value.TopLeft >= Minimum.TopLeft && value.TopLeft <= Maximum.TopLeft
                && value.TopRight >= Minimum.TopRight && value.TopRight <= Maximum.TopRight
                && value.BottomRight >= Minimum.BottomRight && value.BottomRight <= Maximum.BottomRight
                && value.BottomLeft >= Minimum.BottomLeft && value.BottomLeft <= Maximum.BottomLeft;
        }
    }
    public sealed class RectRangeValidator : RangeValidator<Beutl.Graphics.Rect>
    {
        public RectRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue);
        }

        public override Beutl.Graphics.Rect Coerce(ICoreObject? obj, Beutl.Graphics.Rect value)
        {
            return new Beutl.Graphics.Rect(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
        }
        
        public override bool Validate(ICoreObject? obj, Beutl.Graphics.Rect value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height;
        }
    }
}

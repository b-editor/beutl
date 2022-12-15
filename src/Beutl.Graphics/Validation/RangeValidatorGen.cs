
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

        public override bool TryCoerce(ValidationContext context, ref Beutl.Media.PixelPoint value)
        {
            value = new Beutl.Media.PixelPoint(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
            return true;
        }
        
        public override string? Validate(ValidationContext context, Beutl.Media.PixelPoint value)
        {
            if (value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y)
            {
                return $"The value must be between {Minimum} and {Maximum}.";
            }
            else
            {
                return null;
            }
        }
    }
    public sealed class PixelSizeRangeValidator : RangeValidator<Beutl.Media.PixelSize>
    {
        public PixelSizeRangeValidator()
        {
            Maximum = new(System.Int32.MaxValue, System.Int32.MaxValue);
            Minimum = new(System.Int32.MinValue, System.Int32.MinValue);
        }

        public override bool TryCoerce(ValidationContext context, ref Beutl.Media.PixelSize value)
        {
            value = new Beutl.Media.PixelSize(
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
            return true;
        }
        
        public override string? Validate(ValidationContext context, Beutl.Media.PixelSize value)
        {
            if (value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height)
            {
                return $"The value must be between {Minimum} and {Maximum}.";
            }
            else
            {
                return null;
            }
        }
    }
    public sealed class PointRangeValidator : RangeValidator<Beutl.Graphics.Point>
    {
        public PointRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override bool TryCoerce(ValidationContext context, ref Beutl.Graphics.Point value)
        {
            value = new Beutl.Graphics.Point(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
            return true;
        }
        
        public override string? Validate(ValidationContext context, Beutl.Graphics.Point value)
        {
            if (value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y)
            {
                return $"The value must be between {Minimum} and {Maximum}.";
            }
            else
            {
                return null;
            }
        }
    }
    public sealed class SizeRangeValidator : RangeValidator<Beutl.Graphics.Size>
    {
        public SizeRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override bool TryCoerce(ValidationContext context, ref Beutl.Graphics.Size value)
        {
            value = new Beutl.Graphics.Size(
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
            return true;
        }
        
        public override string? Validate(ValidationContext context, Beutl.Graphics.Size value)
        {
            if (value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height)
            {
                return $"The value must be between {Minimum} and {Maximum}.";
            }
            else
            {
                return null;
            }
        }
    }
    public sealed class VectorRangeValidator : RangeValidator<Beutl.Graphics.Vector>
    {
        public VectorRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override bool TryCoerce(ValidationContext context, ref Beutl.Graphics.Vector value)
        {
            value = new Beutl.Graphics.Vector(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
            return true;
        }
        
        public override string? Validate(ValidationContext context, Beutl.Graphics.Vector value)
        {
            if (value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y)
            {
                return $"The value must be between {Minimum} and {Maximum}.";
            }
            else
            {
                return null;
            }
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
        
        public override bool TryCoerce(ValidationContext context, ref Beutl.Media.PixelRect value)
        {
            value = new Beutl.Media.PixelRect(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
            return true;
        }
        
        public override string? Validate(ValidationContext context, Beutl.Media.PixelRect value)
        {
            if (value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height)
            {
                return $"The value must be between {Minimum} and {Maximum}.";
            }
            else
            {
                return null;
            }
        }
    }
    public sealed class CornerRadiusRangeValidator : RangeValidator<Beutl.Media.CornerRadius>
    {
        public CornerRadiusRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue);
        }
        
        public override bool TryCoerce(ValidationContext context, ref Beutl.Media.CornerRadius value)
        {
            value = new Beutl.Media.CornerRadius(
                Math.Clamp(value.TopLeft, Minimum.TopLeft, Maximum.TopLeft),
                Math.Clamp(value.TopRight, Minimum.TopRight, Maximum.TopRight),
                Math.Clamp(value.BottomRight, Minimum.BottomRight, Maximum.BottomRight),
                Math.Clamp(value.BottomLeft, Minimum.BottomLeft, Maximum.BottomLeft));
            return true;
        }
        
        public override string? Validate(ValidationContext context, Beutl.Media.CornerRadius value)
        {
            if (value.TopLeft >= Minimum.TopLeft && value.TopLeft <= Maximum.TopLeft
                && value.TopRight >= Minimum.TopRight && value.TopRight <= Maximum.TopRight
                && value.BottomRight >= Minimum.BottomRight && value.BottomRight <= Maximum.BottomRight
                && value.BottomLeft >= Minimum.BottomLeft && value.BottomLeft <= Maximum.BottomLeft)
            {
                return $"The value must be between {Minimum} and {Maximum}.";
            }
            else
            {
                return null;
            }
        }
    }
    public sealed class RectRangeValidator : RangeValidator<Beutl.Graphics.Rect>
    {
        public RectRangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue);
        }
        
        public override bool TryCoerce(ValidationContext context, ref Beutl.Graphics.Rect value)
        {
            value = new Beutl.Graphics.Rect(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Width, Minimum.Width, Maximum.Width),
                Math.Clamp(value.Height, Minimum.Height, Maximum.Height));
            return true;
        }
        
        public override string? Validate(ValidationContext context, Beutl.Graphics.Rect value)
        {
            if (value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Width >= Minimum.Width && value.Width <= Maximum.Width
                && value.Height >= Minimum.Height && value.Height <= Maximum.Height)
            {
                return $"The value must be between {Minimum} and {Maximum}.";
            }
            else
            {
                return null;
            }
        }
    }
}

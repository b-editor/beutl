
#pragma warning disable IDE0001, IDE0049

namespace Beutl.Validation
{
    // Vector2
    internal sealed class Vector2RangeValidator : RangeValidator<System.Numerics.Vector2>
    {
        public Vector2RangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override bool TryCoerce(ValidationContext context, ref System.Numerics.Vector2 value)
        {
            value = new System.Numerics.Vector2(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));

            return true;
        }
        
        public override string? Validate(ValidationContext context, System.Numerics.Vector2 value)
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
    internal sealed class Vector3RangeValidator : RangeValidator<System.Numerics.Vector3>
    {
        public Vector3RangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue);
        }

        public override bool TryCoerce(ValidationContext context, ref System.Numerics.Vector3 value)
        {
            value = new System.Numerics.Vector3(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Z, Minimum.Z, Maximum.Z));

            return true;
        }
        
        public override string? Validate(ValidationContext context, System.Numerics.Vector3 value)
        {
            if (value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Z >= Minimum.Z && value.Z <= Maximum.Z)
            {
                return $"The value must be between {Minimum} and {Maximum}.";
            }
            else
            {
                return null;
            }
        }
    }

    // Vector4
    internal sealed class Vector4RangeValidator : RangeValidator<System.Numerics.Vector4>
    {
        public Vector4RangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue);
        }

        public override bool TryCoerce(ValidationContext context, ref System.Numerics.Vector4 value)
        {
            value = new System.Numerics.Vector4(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Z, Minimum.Z, Maximum.Z),
                Math.Clamp(value.W, Minimum.W, Maximum.W));

            return true;
        }
        
        public override string? Validate(ValidationContext context, System.Numerics.Vector4 value)
        {
            if (value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Z >= Minimum.Z && value.Z <= Maximum.Z
                && value.W >= Minimum.W && value.W <= Maximum.W)
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

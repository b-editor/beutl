
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

        public override System.Numerics.Vector2 Coerce(ICoreObject? obj, System.Numerics.Vector2 value)
        {
            return new System.Numerics.Vector2(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
        }
        
        public override bool Validate(ICoreObject? obj, System.Numerics.Vector2 value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y;
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

        public override System.Numerics.Vector3 Coerce(ICoreObject? obj, System.Numerics.Vector3 value)
        {
            return new System.Numerics.Vector3(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Z, Minimum.Z, Maximum.Z));
        }
        
        public override bool Validate(ICoreObject? obj, System.Numerics.Vector3 value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Z >= Minimum.Z && value.Z <= Maximum.Z;
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

        public override System.Numerics.Vector4 Coerce(ICoreObject? obj, System.Numerics.Vector4 value)
        {
            return new System.Numerics.Vector4(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Z, Minimum.Z, Maximum.Z),
                Math.Clamp(value.W, Minimum.W, Maximum.W));
        }
        
        public override bool Validate(ICoreObject? obj, System.Numerics.Vector4 value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Z >= Minimum.Z && value.Z <= Maximum.Z
                && value.W >= Minimum.W && value.W <= Maximum.W;
        }
    }
}

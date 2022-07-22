
#pragma warning disable IDE0001, IDE0049

namespace BeUtl.Validation
{
    internal sealed class ByteRangeValidator : RangeValidator<System.Byte>
    {
        public ByteRangeValidator()
        {
            Maximum = System.Byte.MaxValue;
            Minimum = System.Byte.MinValue;
        }

        public override System.Byte Coerce(ICoreObject obj, System.Byte value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.Byte value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class DecimalRangeValidator : RangeValidator<System.Decimal>
    {
        public DecimalRangeValidator()
        {
            Maximum = System.Decimal.MaxValue;
            Minimum = System.Decimal.MinValue;
        }

        public override System.Decimal Coerce(ICoreObject obj, System.Decimal value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.Decimal value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class DoubleRangeValidator : RangeValidator<System.Double>
    {
        public DoubleRangeValidator()
        {
            Maximum = System.Double.MaxValue;
            Minimum = System.Double.MinValue;
        }

        public override System.Double Coerce(ICoreObject obj, System.Double value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.Double value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class SingleRangeValidator : RangeValidator<System.Single>
    {
        public SingleRangeValidator()
        {
            Maximum = System.Single.MaxValue;
            Minimum = System.Single.MinValue;
        }

        public override System.Single Coerce(ICoreObject obj, System.Single value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.Single value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class Int16RangeValidator : RangeValidator<System.Int16>
    {
        public Int16RangeValidator()
        {
            Maximum = System.Int16.MaxValue;
            Minimum = System.Int16.MinValue;
        }

        public override System.Int16 Coerce(ICoreObject obj, System.Int16 value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.Int16 value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class Int32RangeValidator : RangeValidator<System.Int32>
    {
        public Int32RangeValidator()
        {
            Maximum = System.Int32.MaxValue;
            Minimum = System.Int32.MinValue;
        }

        public override System.Int32 Coerce(ICoreObject obj, System.Int32 value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.Int32 value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class Int64RangeValidator : RangeValidator<System.Int64>
    {
        public Int64RangeValidator()
        {
            Maximum = System.Int64.MaxValue;
            Minimum = System.Int64.MinValue;
        }

        public override System.Int64 Coerce(ICoreObject obj, System.Int64 value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.Int64 value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class SByteRangeValidator : RangeValidator<System.SByte>
    {
        public SByteRangeValidator()
        {
            Maximum = System.SByte.MaxValue;
            Minimum = System.SByte.MinValue;
        }

        public override System.SByte Coerce(ICoreObject obj, System.SByte value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.SByte value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class UInt16RangeValidator : RangeValidator<System.UInt16>
    {
        public UInt16RangeValidator()
        {
            Maximum = System.UInt16.MaxValue;
            Minimum = System.UInt16.MinValue;
        }

        public override System.UInt16 Coerce(ICoreObject obj, System.UInt16 value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.UInt16 value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class UInt32RangeValidator : RangeValidator<System.UInt32>
    {
        public UInt32RangeValidator()
        {
            Maximum = System.UInt32.MaxValue;
            Minimum = System.UInt32.MinValue;
        }

        public override System.UInt32 Coerce(ICoreObject obj, System.UInt32 value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.UInt32 value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }
    internal sealed class UInt64RangeValidator : RangeValidator<System.UInt64>
    {
        public UInt64RangeValidator()
        {
            Maximum = System.UInt64.MaxValue;
            Minimum = System.UInt64.MinValue;
        }

        public override System.UInt64 Coerce(ICoreObject obj, System.UInt64 value)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        public override bool Validate(ICoreObject obj, System.UInt64 value)
        {
            return value >= Minimum && value <= Maximum;
        }
    }

    // Vector2
    internal sealed class Vector2RangeValidator : RangeValidator<System.Numerics.Vector2>
    {
        public Vector2RangeValidator()
        {
            Maximum = new(System.Single.MaxValue, System.Single.MaxValue);
            Minimum = new(System.Single.MinValue, System.Single.MinValue);
        }

        public override System.Numerics.Vector2 Coerce(ICoreObject obj, System.Numerics.Vector2 value)
        {
            return new System.Numerics.Vector2(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y));
        }
        
        public override bool Validate(ICoreObject obj, System.Numerics.Vector2 value)
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

        public override System.Numerics.Vector3 Coerce(ICoreObject obj, System.Numerics.Vector3 value)
        {
            return new System.Numerics.Vector3(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Z, Minimum.Z, Maximum.Z));
        }
        
        public override bool Validate(ICoreObject obj, System.Numerics.Vector3 value)
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

        public override System.Numerics.Vector4 Coerce(ICoreObject obj, System.Numerics.Vector4 value)
        {
            return new System.Numerics.Vector4(
                Math.Clamp(value.X, Minimum.X, Maximum.X),
                Math.Clamp(value.Y, Minimum.Y, Maximum.Y),
                Math.Clamp(value.Z, Minimum.Z, Maximum.Z),
                Math.Clamp(value.W, Minimum.W, Maximum.W));
        }
        
        public override bool Validate(ICoreObject obj, System.Numerics.Vector4 value)
        {
            return value.X >= Minimum.X && value.X <= Maximum.X
                && value.Y >= Minimum.Y && value.Y <= Maximum.Y
                && value.Z >= Minimum.Z && value.Z <= Maximum.Z
                && value.W >= Minimum.W && value.W <= Maximum.W;
        }
    }
}

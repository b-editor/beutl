#pragma warning disable IDE0001, IDE0049

namespace Beutl.Services.Editors
{
    public sealed class ByteEditorService : INumberEditorService<System.Byte>
    {
        public System.Byte Decrement(System.Byte value, int increment)
        {
            return (System.Byte)(value - increment);
        }

        public System.Byte Increment(System.Byte value, int increment)
        {
            return (System.Byte)(value + increment);
        }

        public bool TryParse(string? s, out System.Byte result)
        {
            return System.Byte.TryParse(s, out result);
        }
    }
    public sealed class DecimalEditorService : INumberEditorService<System.Decimal>
    {
        public System.Decimal Decrement(System.Decimal value, int increment)
        {
            return value - increment;
        }

        public System.Decimal Increment(System.Decimal value, int increment)
        {
            return value + increment;
        }

        public bool TryParse(string? s, out System.Decimal result)
        {
            return System.Decimal.TryParse(s, out result);
        }
    }
    public sealed class DoubleEditorService : INumberEditorService<System.Double>
    {
        public System.Double Decrement(System.Double value, int increment)
        {
            return value - increment;
        }

        public System.Double Increment(System.Double value, int increment)
        {
            return value + increment;
        }

        public bool TryParse(string? s, out System.Double result)
        {
            return System.Double.TryParse(s, out result);
        }
    }
    public sealed class SingleEditorService : INumberEditorService<System.Single>
    {
        public System.Single Decrement(System.Single value, int increment)
        {
            return value - increment;
        }

        public System.Single Increment(System.Single value, int increment)
        {
            return value + increment;
        }

        public bool TryParse(string? s, out System.Single result)
        {
            return System.Single.TryParse(s, out result);
        }
    }
    public sealed class Int16EditorService : INumberEditorService<System.Int16>
    {
        public System.Int16 Decrement(System.Int16 value, int increment)
        {
            return (System.Int16)(value - increment);
        }

        public System.Int16 Increment(System.Int16 value, int increment)
        {
            return (System.Int16)(value + increment);
        }

        public bool TryParse(string? s, out System.Int16 result)
        {
            return System.Int16.TryParse(s, out result);
        }
    }
    public sealed class Int32EditorService : INumberEditorService<System.Int32>
    {
        public System.Int32 Decrement(System.Int32 value, int increment)
        {
            return value - increment;
        }

        public System.Int32 Increment(System.Int32 value, int increment)
        {
            return value + increment;
        }

        public bool TryParse(string? s, out System.Int32 result)
        {
            return System.Int32.TryParse(s, out result);
        }
    }
    public sealed class Int64EditorService : INumberEditorService<System.Int64>
    {
        public System.Int64 Decrement(System.Int64 value, int increment)
        {
            return value - increment;
        }

        public System.Int64 Increment(System.Int64 value, int increment)
        {
            return value + increment;
        }

        public bool TryParse(string? s, out System.Int64 result)
        {
            return System.Int64.TryParse(s, out result);
        }
    }
    public sealed class SByteEditorService : INumberEditorService<System.SByte>
    {
        public System.SByte Decrement(System.SByte value, int increment)
        {
            return (System.SByte)(value - increment);
        }

        public System.SByte Increment(System.SByte value, int increment)
        {
            return (System.SByte)(value + increment);
        }

        public bool TryParse(string? s, out System.SByte result)
        {
            return System.SByte.TryParse(s, out result);
        }
    }
    public sealed class UInt16EditorService : INumberEditorService<System.UInt16>
    {
        public System.UInt16 Decrement(System.UInt16 value, int increment)
        {
            return (System.UInt16)(value - increment);
        }

        public System.UInt16 Increment(System.UInt16 value, int increment)
        {
            return (System.UInt16)(value + increment);
        }

        public bool TryParse(string? s, out System.UInt16 result)
        {
            return System.UInt16.TryParse(s, out result);
        }
    }
    public sealed class UInt32EditorService : INumberEditorService<System.UInt32>
    {
        public System.UInt32 Decrement(System.UInt32 value, int increment)
        {
            return value - (System.UInt32)increment;
        }

        public System.UInt32 Increment(System.UInt32 value, int increment)
        {
            return value + (System.UInt32)increment;
        }

        public bool TryParse(string? s, out System.UInt32 result)
        {
            return System.UInt32.TryParse(s, out result);
        }
    }
    public sealed class UInt64EditorService : INumberEditorService<System.UInt64>
    {
        public System.UInt64 Decrement(System.UInt64 value, int increment)
        {
            return value - (System.UInt64)increment;
        }

        public System.UInt64 Increment(System.UInt64 value, int increment)
        {
            return value + (System.UInt64)increment;
        }

        public bool TryParse(string? s, out System.UInt64 result)
        {
            return System.UInt64.TryParse(s, out result);
        }
    }
}

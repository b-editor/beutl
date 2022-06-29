using BeUtl.Services.Editors.Wrappers;

#pragma warning disable IDE0001, IDE0049

namespace BeUtl.Services.Editors
{
    public sealed class ByteEditorService : INumberEditorService<System.Byte>
    {
        public System.Byte GetMaximum(IWrappedProperty<System.Byte> property)
        {
            return property.GetMaximumOrDefault(System.Byte.MaxValue);
        }

        public System.Byte GetMinimum(IWrappedProperty<System.Byte> property)
        {
            return property.GetMinimumOrDefault(System.Byte.MinValue);
        }

        public System.Byte Clamp(System.Byte value, System.Byte min, System.Byte max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.Decimal GetMaximum(IWrappedProperty<System.Decimal> property)
        {
            return property.GetMaximumOrDefault(System.Decimal.MaxValue);
        }

        public System.Decimal GetMinimum(IWrappedProperty<System.Decimal> property)
        {
            return property.GetMinimumOrDefault(System.Decimal.MinValue);
        }

        public System.Decimal Clamp(System.Decimal value, System.Decimal min, System.Decimal max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.Double GetMaximum(IWrappedProperty<System.Double> property)
        {
            return property.GetMaximumOrDefault(System.Double.MaxValue);
        }

        public System.Double GetMinimum(IWrappedProperty<System.Double> property)
        {
            return property.GetMinimumOrDefault(System.Double.MinValue);
        }

        public System.Double Clamp(System.Double value, System.Double min, System.Double max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.Single GetMaximum(IWrappedProperty<System.Single> property)
        {
            return property.GetMaximumOrDefault(System.Single.MaxValue);
        }

        public System.Single GetMinimum(IWrappedProperty<System.Single> property)
        {
            return property.GetMinimumOrDefault(System.Single.MinValue);
        }

        public System.Single Clamp(System.Single value, System.Single min, System.Single max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.Int16 GetMaximum(IWrappedProperty<System.Int16> property)
        {
            return property.GetMaximumOrDefault(System.Int16.MaxValue);
        }

        public System.Int16 GetMinimum(IWrappedProperty<System.Int16> property)
        {
            return property.GetMinimumOrDefault(System.Int16.MinValue);
        }

        public System.Int16 Clamp(System.Int16 value, System.Int16 min, System.Int16 max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.Int32 GetMaximum(IWrappedProperty<System.Int32> property)
        {
            return property.GetMaximumOrDefault(System.Int32.MaxValue);
        }

        public System.Int32 GetMinimum(IWrappedProperty<System.Int32> property)
        {
            return property.GetMinimumOrDefault(System.Int32.MinValue);
        }

        public System.Int32 Clamp(System.Int32 value, System.Int32 min, System.Int32 max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.Int64 GetMaximum(IWrappedProperty<System.Int64> property)
        {
            return property.GetMaximumOrDefault(System.Int64.MaxValue);
        }

        public System.Int64 GetMinimum(IWrappedProperty<System.Int64> property)
        {
            return property.GetMinimumOrDefault(System.Int64.MinValue);
        }

        public System.Int64 Clamp(System.Int64 value, System.Int64 min, System.Int64 max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.SByte GetMaximum(IWrappedProperty<System.SByte> property)
        {
            return property.GetMaximumOrDefault(System.SByte.MaxValue);
        }

        public System.SByte GetMinimum(IWrappedProperty<System.SByte> property)
        {
            return property.GetMinimumOrDefault(System.SByte.MinValue);
        }

        public System.SByte Clamp(System.SByte value, System.SByte min, System.SByte max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.UInt16 GetMaximum(IWrappedProperty<System.UInt16> property)
        {
            return property.GetMaximumOrDefault(System.UInt16.MaxValue);
        }

        public System.UInt16 GetMinimum(IWrappedProperty<System.UInt16> property)
        {
            return property.GetMinimumOrDefault(System.UInt16.MinValue);
        }

        public System.UInt16 Clamp(System.UInt16 value, System.UInt16 min, System.UInt16 max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.UInt32 GetMaximum(IWrappedProperty<System.UInt32> property)
        {
            return property.GetMaximumOrDefault(System.UInt32.MaxValue);
        }

        public System.UInt32 GetMinimum(IWrappedProperty<System.UInt32> property)
        {
            return property.GetMinimumOrDefault(System.UInt32.MinValue);
        }

        public System.UInt32 Clamp(System.UInt32 value, System.UInt32 min, System.UInt32 max)
        {
            return Math.Clamp(value, min, max);
        }

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
        public System.UInt64 GetMaximum(IWrappedProperty<System.UInt64> property)
        {
            return property.GetMaximumOrDefault(System.UInt64.MaxValue);
        }

        public System.UInt64 GetMinimum(IWrappedProperty<System.UInt64> property)
        {
            return property.GetMinimumOrDefault(System.UInt64.MinValue);
        }

        public System.UInt64 Clamp(System.UInt64 value, System.UInt64 min, System.UInt64 max)
        {
            return Math.Clamp(value, min, max);
        }

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

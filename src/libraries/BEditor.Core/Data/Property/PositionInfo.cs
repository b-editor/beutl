// ColorAnimationProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Data.Property
{
    public readonly struct PositionInfo : IEquatable<PositionInfo>
    {
        public PositionInfo(float position, PositionType type)
        {
            Value = position;
            Type = type;
        }

        public float Value { get; }

        public PositionType Type { get; }

        public static bool operator ==(PositionInfo left, PositionInfo right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PositionInfo left, PositionInfo right)
        {
            return !(left == right);
        }

        public static bool TryParse(string s, out PositionInfo result)
        {
            var type = s.Contains('`') ? PositionType.Absolute : PositionType.Percentage;
            if (!float.TryParse(s.Trim('`'), out var pos))
            {
                result = default;
                return false;
            }

            result = new PositionInfo(pos, type);
            return true;
        }

        public static PositionInfo Parse(string s)
        {
            if (TryParse(s, out var result))
            {
                return result;
            }
            else
            {
                throw new Exception($"\"{s}\" could not be parsed.");
            }
        }

        public PositionInfo WithValue(float value)
        {
            return new(value, Type);
        }

        public PositionInfo WithType(PositionType type)
        {
            return new(Value, type);
        }

        public PositionInfo WithType(PositionType type, float length)
        {
            if (type == Type) return this;

            if (Type == PositionType.Absolute)
            {
                // 割合に変換
                return new(Value / length, type);
            }
            else
            {
                return new(Value * length, type);
            }
        }

        public float GetAbsolutePosition(float length)
        {
            if (Type == PositionType.Absolute)
                return Value;

            return Value * length;
        }

        public float GetPercentagePosition(float length)
        {
            if (Type == PositionType.Percentage)
                return Value;

            return Value / length;
        }

        public override string ToString()
        {
            return Type switch
            {
                PositionType.Absolute => Value.ToString("f0") + "`",
                PositionType.Percentage => Value.ToString(),
                _ => string.Empty,
            };
        }

        public override bool Equals(object? obj)
        {
            return obj is PositionInfo info && Equals(info);
        }

        public bool Equals(PositionInfo other)
        {
            return Value == other.Value &&
                   Type == other.Type;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Value, Type);
        }
    }
}
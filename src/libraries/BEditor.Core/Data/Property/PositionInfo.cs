// PositionInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents position information.
    /// </summary>
    public readonly struct PositionInfo : IEquatable<PositionInfo>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PositionInfo"/> struct.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="type">The type of the position.</param>
        public PositionInfo(float position, PositionType type)
        {
            Value = position;
            Type = type;
        }

        /// <summary>
        /// Gets the value.
        /// </summary>
        public float Value { get; }

        /// <summary>
        /// Gets the type of the position.
        /// </summary>
        public PositionType Type { get; }

        /// <summary>
        /// Indicates whether two <see cref="PositionInfo"/> instances are equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(PositionInfo left, PositionInfo right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two <see cref="PositionInfo"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(PositionInfo left, PositionInfo right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Parses a <see cref="PositionInfo"/> string.
        /// A return value indicates whether the conversion succeeded or failed.
        /// </summary>
        /// <param name="s">The string.</param>
        /// <param name="result">The parsed <see cref="PositionInfo"/>.</param>
        /// <returns>true if s was parsed successfully; otherwise, false.</returns>
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

        /// <summary>
        /// Parses a <see cref="PositionInfo"/> string.
        /// </summary>
        /// <param name="s">The string.</param>
        /// <returns>The parsed <see cref="PositionInfo"/>.</returns>
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

        /// <summary>
        /// Returns a new <see cref="PositionInfo"/> with the specified position.
        /// </summary>
        /// <param name="value">The position.</param>
        /// <returns>The new <see cref="PositionInfo"/>.</returns>
        public PositionInfo WithValue(float value)
        {
            return new(value, Type);
        }

        /// <summary>
        /// Returns a new <see cref="PositionInfo"/> with the specified position type.
        /// </summary>
        /// <param name="type">The type of the position.</param>
        /// <returns>The new <see cref="PositionInfo"/>.</returns>
        public PositionInfo WithType(PositionType type)
        {
            return new(Value, type);
        }

        /// <summary>
        /// Returns a new <see cref="PositionInfo"/> with the specified position type.
        /// </summary>
        /// <param name="type">The type of the position.</param>
        /// <param name="length">The length.</param>
        /// <returns>The new <see cref="PositionInfo"/>.</returns>
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

        /// <summary>
        /// Gets the absolute position.
        /// </summary>
        /// <param name="length">The length.</param>
        /// <returns>The absolute position.</returns>
        public float GetAbsolutePosition(float length)
        {
            if (Type == PositionType.Absolute)
                return Value;

            return Value * length;
        }

        /// <summary>
        /// Gets the percentage.
        /// </summary>
        /// <param name="length">The length.</param>
        /// <returns>The percentage.</returns>
        public float GetPercentagePosition(float length)
        {
            if (Type == PositionType.Percentage)
                return Value;

            return Value / length;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Type switch
            {
                PositionType.Absolute => Value.ToString("f0") + "`",
                PositionType.Percentage => Value.ToString(),
                _ => string.Empty,
            };
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is PositionInfo info && Equals(info);
        }

        /// <inheritdoc/>
        public bool Equals(PositionInfo other)
        {
            return Value == other.Value &&
                   Type == other.Type;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Value, Type);
        }
    }
}
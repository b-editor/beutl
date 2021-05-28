// Frame.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace BEditor.Media
{
    /// <summary>
    /// Represents the frame number.
    /// </summary>
    [DebuggerDisplay("{Value}")]
    [Serializable]
    public readonly struct Frame : IEquatable<Frame>, ISerializable
    {
        /// <summary>
        /// Represents the largest possible value of an <see cref="Frame"/>.
        /// </summary>
        public static readonly Frame MaxValue = new(int.MaxValue);

        /// <summary>
        /// Represents the smallest possible value of <see cref="Frame"/>.
        /// </summary>
        public static readonly Frame MinValue = new(int.MinValue);

        /// <summary>
        /// Represents the value of 0 in <see cref="Frame"/>.
        /// </summary>
        public static readonly Frame Zero = default;

        /// <summary>
        /// Initializes a new instance of the <see cref="Frame"/> struct.
        /// </summary>
        /// <param name="value">The value.</param>
        public Frame(int value)
        {
            Value = value;
        }

        private Frame(SerializationInfo info, StreamingContext context)
        {
            Value = info.GetInt32(nameof(Value));
        }

        /// <summary>
        /// Gets the number of the frame.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Converts the <see cref="Frame"/> to a 32-bit signed integer.
        /// </summary>
        /// <param name="frame">A frame.</param>
        public static implicit operator int(Frame frame)
        {
            return frame.Value;
        }

        /// <summary>
        /// Converts the 32-bit signed integer to a <see cref="Frame"/>.
        /// </summary>
        /// <param name="value">A 32-bit signed integer.</param>
        public static implicit operator Frame(int value)
        {
            return new(value);
        }

        /// <summary>
        /// Indicates whether two <see cref="Frame"/> instances are equal.
        /// </summary>
        /// <param name="left">The first time interval to compare.</param>
        /// <param name="right">The second time interval to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(Frame left, Frame right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two <see cref="Frame"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first time interval to compare.</param>
        /// <param name="right">The second time interval to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(Frame left, Frame right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Indicates whether a specified <see cref="Frame"/> is less than another specified <see cref="Frame"/>.
        /// </summary>
        /// <param name="left">The first time interval to compare.</param>
        /// <param name="right">The second time interval to compare.</param>
        /// <returns>true if the value of <paramref name="left"/> is less than the value of <paramref name="right"/>; otherwise, false.</returns>
        public static bool operator <(Frame left, Frame right)
        {
            return left.Value < right.Value;
        }

        /// <summary>
        /// Indicates whether a specified <see cref="Frame"/> is greater than another specified <see cref="Frame"/>.
        /// </summary>
        /// <param name="left">The first time interval to compare.</param>
        /// <param name="right">The second time interval to compare.</param>
        /// <returns>true if the value of <paramref name="left"/> is greater than the value of <paramref name="right"/>; otherwise, false.</returns>
        public static bool operator >(Frame left, Frame right)
        {
            return left.Value > right.Value;
        }

        /// <summary>
        /// Indicates whether a specified <see cref="Frame"/> is less than or equal another specified <see cref="Frame"/>.
        /// </summary>
        /// <param name="left">The first time interval to compare.</param>
        /// <param name="right">The second time interval to compare.</param>
        /// <returns>true if the value of <paramref name="left"/> is less than or equal the value of <paramref name="right"/>; otherwise, false.</returns>
        public static bool operator <=(Frame left, Frame right)
        {
            return left.Value <= right.Value;
        }

        /// <summary>
        /// Indicates whether a specified <see cref="Frame"/> is greater than or equal another specified <see cref="Frame"/>.
        /// </summary>
        /// <param name="left">The first time interval to compare.</param>
        /// <param name="right">The second time interval to compare.</param>
        /// <returns>true if the value of <paramref name="left"/> is greater than or equal the value of <paramref name="right"/>; otherwise, false.</returns>
        public static bool operator >=(Frame left, Frame right)
        {
            return left.Value >= right.Value;
        }

        /// <summary>
        /// Adds two specified <see cref="Frame"/> instances.
        /// </summary>
        /// <param name="left">The first time interval to add.</param>
        /// <param name="right">The second time interval to add.</param>
        /// <returns>An object whose value is the sum of the values of <paramref name="left"/> and <paramref name="right"/>.</returns>
        public static Frame operator +(Frame left, Frame right)
        {
            return new(left.Value + right.Value);
        }

        /// <summary>
        /// Subtracts a specified <see cref="Frame"/> from another specified <see cref="Frame"/>.
        /// </summary>
        /// <param name="left">The minuend.</param>
        /// <param name="right">The subtrahend.</param>
        /// <returns>An object whose value is the result of the value of <paramref name="left"/> minus the value of <paramref name="right"/>.</returns>
        public static Frame operator -(Frame left, Frame right)
        {
            return new(left.Value - right.Value);
        }

        /// <summary>
        /// Returns a new <see cref="Frame"/> value which is the result of division of <paramref name="left"/> instance and the specified <paramref name="right"/>.
        /// </summary>
        /// <param name="left">Divident or the value to be divided.</param>
        /// <param name="right">The value to be divided by.</param>
        /// <returns>A new value that represents result of division of <paramref name="left"/> instance by the value of the <paramref name="right"/>.</returns>
        public static Frame operator /(Frame left, Frame right)
        {
            return new(left.Value / right.Value);
        }

        /// <summary>
        /// Returns a new <see cref="Frame"/> object whose value is the result of multiplying the specified <see cref="Frame"/> instance and the specified factor.
        /// </summary>
        /// <param name="left">The value to be multiplied.</param>
        /// <param name="right">The value to be multiplied by.</param>
        /// <returns>A new object that represents the value of the specified <see cref="Frame"/> instance multiplied by the value of the specified factor.</returns>
        public static Frame operator *(Frame left, Frame right)
        {
            return new(left.Value * right.Value);
        }

        /// <summary>
        /// Creates the <see cref="Frame"/> from milliseconds.
        /// </summary>
        /// <param name="milliseconds">A number of milliseconds.</param>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public static Frame FromMilliseconds(double milliseconds, double framerate)
        {
            return new((int)(milliseconds * framerate / 1000));
        }

        /// <summary>
        /// Creates the <see cref="Frame"/> from seconds.
        /// </summary>
        /// <param name="seconds">A number of seconds.</param>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public static Frame FromSeconds(double seconds, double framerate)
        {
            return FromMilliseconds(seconds * 1000, framerate);
        }

        /// <summary>
        /// Creates the <see cref="Frame"/> from minutes.
        /// </summary>
        /// <param name="minutes">A number of minutes.</param>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public static Frame FromMinutes(double minutes, double framerate)
        {
            return FromSeconds(minutes * 60, framerate);
        }

        /// <summary>
        /// Creates the <see cref="Frame"/> from hours.
        /// </summary>
        /// <param name="hours">A number of hours.</param>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public static Frame FromHours(double hours, double framerate)
        {
            return FromMinutes(hours * 60, framerate);
        }

        /// <summary>
        /// Creates the <see cref="Frame"/> from <see cref="TimeSpan"/>.
        /// </summary>
        /// <param name="timeSpan">The <see cref="TimeSpan"/> representing the frame number.</param>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public static Frame FromTimeSpan(TimeSpan timeSpan, double framerate)
        {
            return FromMilliseconds(timeSpan.TotalMilliseconds, framerate);
        }

        /// <summary>
        /// Converts this <see cref="Frame"/> to a <see cref="TimeSpan"/>.
        /// </summary>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public TimeSpan ToTimeSpan(double framerate)
        {
            return TimeSpan.FromMilliseconds(ToMilliseconds(framerate));
        }

        /// <summary>
        /// Converts this <see cref="Frame"/> to milliseconds.
        /// </summary>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public double ToMilliseconds(double framerate)
        {
            return Value / framerate * 1000;
        }

        /// <summary>
        /// Converts this <see cref="Frame"/> to seconds.
        /// </summary>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public double ToSeconds(double framerate)
        {
            return ToMilliseconds(framerate) / 1000;
        }

        /// <summary>
        /// Converts this <see cref="Frame"/> to minutes.
        /// </summary>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public double ToMinutes(double framerate)
        {
            return ToSeconds(framerate) * 60;
        }

        /// <summary>
        /// Converts this <see cref="Frame"/> to hours.
        /// </summary>
        /// <param name="framerate">The number of frames per second.</param>
        /// <returns>An object that represents value.</returns>
        public double ToHours(double framerate)
        {
            return ToMinutes(framerate) * 60;
        }

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj)
        {
            return obj is Frame frame && Equals(frame);
        }

        /// <inheritdoc/>
        public readonly bool Equals(Frame other)
        {
            return Value == other.Value;
        }

        /// <inheritdoc/>
        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Value);
        }

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(Value), Value);
        }
    }
}
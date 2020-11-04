using System;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace BEditor.Mathematics {
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector2 : IEquatable<Vector2> {
        public float X;

        public float Y;

        public Vector2(float value) {
            X = value;
            Y = value;
        }

        public Vector2(float x, float y) {
            X = x;
            Y = y;
        }

        
        public float this[int index] {
            get {
                if (index == 0) {
                    return X;
                }

                if (index == 1) {
                    return Y;
                }

                throw new IndexOutOfRangeException("You tried to access this vector at index: " + index);
            }

            set {
                if (index == 0) {
                    X = value;
                }
                else if (index == 1) {
                    Y = value;
                }
                else {
                    throw new IndexOutOfRangeException("You tried to set this vector at index: " + index);
                }
            }
        }

        
        public float Length => MathF.Sqrt((X * X) + (Y * Y));

        
        public float LengthFast => 1.0f / MathHelper.InverseSqrtFast((X * X) + (Y * Y));

        
        public float LengthSquared => (X * X) + (Y * Y);

        
        public Vector2 PerpendicularRight => new Vector2(Y, -X);

        
        public Vector2 PerpendicularLeft => new Vector2(-Y, X);

        
        public Vector2 Normalized() {
            var v = this;
            v.Normalize();
            return v;
        }

        
        public void Normalize() {
            var scale = 1.0f / Length;
            X *= scale;
            Y *= scale;
        }

        
        public void NormalizeFast() {
            var scale = MathHelper.InverseSqrtFast((X * X) + (Y * Y));
            X *= scale;
            Y *= scale;
        }

        
        public static readonly Vector2 UnitX = new Vector2(1, 0);

        
        public static readonly Vector2 UnitY = new Vector2(0, 1);

        
        public static readonly Vector2 Zero = new Vector2(0, 0);

        
        public static readonly Vector2 One = new Vector2(1, 1);

        
        public static readonly Vector2 PositiveInfinity = new Vector2(float.PositiveInfinity, float.PositiveInfinity);

        
        public static readonly Vector2 NegativeInfinity = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        
        public static readonly int SizeInBytes = Unsafe.SizeOf<Vector2>();

        
        [Pure]
        public static Vector2 Add(Vector2 a, Vector2 b) {
            Add(a, b, out a);
            return a;
        }

        
        public static void Add(Vector2 a, Vector2 b, out Vector2 result) {
            result.X = a.X + b.X;
            result.Y = a.Y + b.Y;
        }

        
        [Pure]
        public static Vector2 Subtract(Vector2 a, Vector2 b) {
            Subtract(a, b, out a);
            return a;
        }

        
        public static void Subtract(Vector2 a, Vector2 b, out Vector2 result) {
            result.X = a.X - b.X;
            result.Y = a.Y - b.Y;
        }

        
        [Pure]
        public static Vector2 Multiply(Vector2 vector, float scale) {
            Multiply(vector, scale, out vector);
            return vector;
        }

        
        public static void Multiply(Vector2 vector, float scale, out Vector2 result) {
            result.X = vector.X * scale;
            result.Y = vector.Y * scale;
        }

        
        [Pure]
        public static Vector2 Multiply(Vector2 vector, Vector2 scale) {
            Multiply(vector, scale, out vector);
            return vector;
        }

        
        public static void Multiply(Vector2 vector, Vector2 scale, out Vector2 result) {
            result.X = vector.X * scale.X;
            result.Y = vector.Y * scale.Y;
        }

        
        [Pure]
        public static Vector2 Divide(Vector2 vector, float scale) {
            Divide(vector, scale, out vector);
            return vector;
        }

        
        public static void Divide(Vector2 vector, float scale, out Vector2 result) {
            result.X = vector.X / scale;
            result.Y = vector.Y / scale;
        }

        
        [Pure]
        public static Vector2 Divide(Vector2 vector, Vector2 scale) {
            Divide(vector, scale, out vector);
            return vector;
        }

        
        public static void Divide(Vector2 vector, Vector2 scale, out Vector2 result) {
            result.X = vector.X / scale.X;
            result.Y = vector.Y / scale.Y;
        }

        
        [Pure]
        public static Vector2 ComponentMin(Vector2 a, Vector2 b) {
            a.X = a.X < b.X ? a.X : b.X;
            a.Y = a.Y < b.Y ? a.Y : b.Y;
            return a;
        }

        
        public static void ComponentMin(Vector2 a, Vector2 b, out Vector2 result) {
            result.X = a.X < b.X ? a.X : b.X;
            result.Y = a.Y < b.Y ? a.Y : b.Y;
        }

        
        [Pure]
        public static Vector2 ComponentMax(Vector2 a, Vector2 b) {
            a.X = a.X > b.X ? a.X : b.X;
            a.Y = a.Y > b.Y ? a.Y : b.Y;
            return a;
        }

        
        public static void ComponentMax(Vector2 a, Vector2 b, out Vector2 result) {
            result.X = a.X > b.X ? a.X : b.X;
            result.Y = a.Y > b.Y ? a.Y : b.Y;
        }

        
        [Pure]
        public static Vector2 MagnitudeMin(Vector2 left, Vector2 right) {
            return left.LengthSquared < right.LengthSquared ? left : right;
        }

        
        public static void MagnitudeMin(Vector2 left, Vector2 right, out Vector2 result) {
            result = left.LengthSquared < right.LengthSquared ? left : right;
        }

        
        [Pure]
        public static Vector2 MagnitudeMax(Vector2 left, Vector2 right) {
            return left.LengthSquared >= right.LengthSquared ? left : right;
        }

        
        public static void MagnitudeMax(Vector2 left, Vector2 right, out Vector2 result) {
            result = left.LengthSquared >= right.LengthSquared ? left : right;
        }

        
        [Pure]
        public static Vector2 Clamp(Vector2 vec, Vector2 min, Vector2 max) {
            vec.X = vec.X < min.X ? min.X : vec.X > max.X ? max.X : vec.X;
            vec.Y = vec.Y < min.Y ? min.Y : vec.Y > max.Y ? max.Y : vec.Y;
            return vec;
        }

        
        public static void Clamp(Vector2 vec, Vector2 min, Vector2 max, out Vector2 result) {
            result.X = vec.X < min.X ? min.X : vec.X > max.X ? max.X : vec.X;
            result.Y = vec.Y < min.Y ? min.Y : vec.Y > max.Y ? max.Y : vec.Y;
        }

        
        [Pure]
        public static float Distance(Vector2 vec1, Vector2 vec2) {
            Distance(vec1, vec2, out float result);
            return result;
        }

        
        public static void Distance(Vector2 vec1, Vector2 vec2, out float result) {
            result = MathF.Sqrt(((vec2.X - vec1.X) * (vec2.X - vec1.X)) + ((vec2.Y - vec1.Y) * (vec2.Y - vec1.Y)));
        }

        
        [Pure]
        public static float DistanceSquared(Vector2 vec1, Vector2 vec2) {
            DistanceSquared(vec1, vec2, out float result);
            return result;
        }

        
        public static void DistanceSquared(Vector2 vec1, Vector2 vec2, out float result) {
            result = ((vec2.X - vec1.X) * (vec2.X - vec1.X)) + ((vec2.Y - vec1.Y) * (vec2.Y - vec1.Y));
        }

        
        [Pure]
        public static Vector2 Normalize(Vector2 vec) {
            var scale = 1.0f / vec.Length;
            vec.X *= scale;
            vec.Y *= scale;
            return vec;
        }

        
        public static void Normalize(Vector2 vec, out Vector2 result) {
            var scale = 1.0f / vec.Length;
            result.X = vec.X * scale;
            result.Y = vec.Y * scale;
        }

        
        [Pure]
        public static Vector2 NormalizeFast(Vector2 vec) {
            var scale = MathHelper.InverseSqrtFast((vec.X * vec.X) + (vec.Y * vec.Y));
            vec.X *= scale;
            vec.Y *= scale;
            return vec;
        }

        
        public static void NormalizeFast(Vector2 vec, out Vector2 result) {
            var scale = MathHelper.InverseSqrtFast((vec.X * vec.X) + (vec.Y * vec.Y));
            result.X = vec.X * scale;
            result.Y = vec.Y * scale;
        }

        
        [Pure]
        public static float Dot(Vector2 left, Vector2 right) {
            return (left.X * right.X) + (left.Y * right.Y);
        }

        
        public static void Dot(Vector2 left, Vector2 right, out float result) {
            result = (left.X * right.X) + (left.Y * right.Y);
        }

        
        [Pure]
        public static float PerpDot(Vector2 left, Vector2 right) {
            return (left.X * right.Y) - (left.Y * right.X);
        }

        
        public static void PerpDot(Vector2 left, Vector2 right, out float result) {
            result = (left.X * right.Y) - (left.Y * right.X);
        }

        
        [Pure]
        public static Vector2 Lerp(Vector2 a, Vector2 b, float blend) {
            a.X = (blend * (b.X - a.X)) + a.X;
            a.Y = (blend * (b.Y - a.Y)) + a.Y;
            return a;
        }

        
        public static void Lerp(Vector2 a, Vector2 b, float blend, out Vector2 result) {
            result.X = (blend * (b.X - a.X)) + a.X;
            result.Y = (blend * (b.Y - a.Y)) + a.Y;
        }

        
        [Pure]
        public static Vector2 BaryCentric(Vector2 a, Vector2 b, Vector2 c, float u, float v) {
            BaryCentric(a, b, c, u, v, out var result);
            return result;
        }

        
        public static void BaryCentric
        (
            Vector2 a,
            Vector2 b,
            Vector2 c,
            float u,
            float v,
            out Vector2 result
        ) {
            Subtract(b, a, out var ab);
            Multiply(ab, u, out var abU);
            Add(a, abU, out var uPos);

            Subtract(c, a, out var ac);
            Multiply(ac, v, out var acV);
            Add(uPos, acV, out result);
        }

        
        [Pure]
        public static Vector2 TransformRow(Vector2 vec, Matrix2 mat) {
            TransformRow(vec, mat, out Vector2 result);
            return result;
        }

        
        public static void TransformRow(Vector2 vec, Matrix2 mat, out Vector2 result) {
            result = new Vector2(
                (vec.X * mat.Row0.X) + (vec.Y * mat.Row1.X),
                (vec.X * mat.Row0.Y) + (vec.Y * mat.Row1.Y));
        }

        
        [Pure]
        public static Vector2 Transform(Vector2 vec, Quaternion quat) {
            Transform(vec, quat, out Vector2 result);
            return result;
        }

        
        public static void Transform(Vector2 vec, Quaternion quat, out Vector2 result) {
            Quaternion v = new Quaternion(vec.X, vec.Y, 0, 0);
            Quaternion.Invert(quat, out Quaternion i);
            Quaternion.Multiply(quat, v, out Quaternion t);
            Quaternion.Multiply(t, i, out v);

            result.X = v.X;
            result.Y = v.Y;
        }

        
        [Pure]
        public static Vector2 TransformColumn(Matrix2 mat, Vector2 vec) {
            TransformColumn(mat, vec, out Vector2 result);
            return result;
        }

        
        public static void TransformColumn(Matrix2 mat, Vector2 vec, out Vector2 result) {
            result.X = (mat.Row0.X * vec.X) + (mat.Row0.Y * vec.Y);
            result.Y = (mat.Row1.X * vec.X) + (mat.Row1.Y * vec.Y);
        }

        
        [XmlIgnore]
        public Vector2 Yx {
            get => new Vector2(Y, X);
            set {
                Y = value.X;
                X = value.Y;
            }
        }

        
        [Pure]
        public static Vector2 operator +(Vector2 left, Vector2 right) {
            left.X += right.X;
            left.Y += right.Y;
            return left;
        }

        
        [Pure]
        public static Vector2 operator -(Vector2 left, Vector2 right) {
            left.X -= right.X;
            left.Y -= right.Y;
            return left;
        }

        
        [Pure]
        public static Vector2 operator -(Vector2 vec) {
            vec.X = -vec.X;
            vec.Y = -vec.Y;
            return vec;
        }

        
        [Pure]
        public static Vector2 operator *(Vector2 vec, float scale) {
            vec.X *= scale;
            vec.Y *= scale;
            return vec;
        }

        
        [Pure]
        public static Vector2 operator *(float scale, Vector2 vec) {
            vec.X *= scale;
            vec.Y *= scale;
            return vec;
        }

        
        [Pure]
        public static Vector2 operator *(Vector2 vec, Vector2 scale) {
            vec.X *= scale.X;
            vec.Y *= scale.Y;
            return vec;
        }

        
        [Pure]
        public static Vector2 operator *(Vector2 vec, Matrix2 mat) {
            TransformRow(vec, mat, out Vector2 result);
            return result;
        }

        
        [Pure]
        public static Vector2 operator *(Matrix2 mat, Vector2 vec) {
            TransformColumn(mat, vec, out Vector2 result);
            return result;
        }

        
        [Pure]
        public static Vector2 operator *(Quaternion quat, Vector2 vec) {
            Transform(vec, quat, out Vector2 result);
            return result;
        }

        
        [Pure]
        public static Vector2 operator /(Vector2 vec, float scale) {
            vec.X /= scale;
            vec.Y /= scale;
            return vec;
        }

        
        public static bool operator ==(Vector2 left, Vector2 right) {
            return left.Equals(right);
        }

        
        public static bool operator !=(Vector2 left, Vector2 right) {
            return !(left == right);
        }

        
        [Pure]
        public static implicit operator Vector2((float X, float Y) values) {
            return new Vector2(values.X, values.Y);
        }

        /// <inheritdoc/>
        public override string ToString() => $"(X:{X} Y:{Y})";

        /// <inheritdoc/>
        public override bool Equals(object obj) {
            return obj is Vector2 && Equals((Vector2)obj);
        }

        /// <inheritdoc/>
        public bool Equals(Vector2 other) {
            return X == other.X &&
                   Y == other.Y;
        }

        /// <inheritdoc/>
        public override int GetHashCode() {
            return HashCode.Combine(X, Y);
        }

        
        [Pure]
        public void Deconstruct(out float x, out float y) {
            x = X;
            y = Y;
        }
    }
}

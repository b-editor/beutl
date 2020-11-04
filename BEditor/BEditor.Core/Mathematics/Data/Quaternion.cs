using System;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace BEditor.Mathematics {
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Quaternion : IEquatable<Quaternion> {
        public Vector3 Xyz;

        public float W;

        public Quaternion(Vector3 v, float w) {
            Xyz = v;
            W = w;
        }

        public Quaternion(float x, float y, float z, float w)
            : this(new Vector3(x, y, z), w) {
        }

        public Quaternion(float rotationX, float rotationY, float rotationZ) {
            rotationX *= 0.5f;
            rotationY *= 0.5f;
            rotationZ *= 0.5f;

            var c1 = MathF.Cos(rotationX);
            var c2 = MathF.Cos(rotationY);
            var c3 = MathF.Cos(rotationZ);
            var s1 = MathF.Sin(rotationX);
            var s2 = MathF.Sin(rotationY);
            var s3 = MathF.Sin(rotationZ);

            W = (c1 * c2 * c3) - (s1 * s2 * s3);
            Xyz.X = (s1 * c2 * c3) + (c1 * s2 * s3);
            Xyz.Y = (c1 * s2 * c3) - (s1 * c2 * s3);
            Xyz.Z = (c1 * c2 * s3) + (s1 * s2 * c3);
        }

        public Quaternion(Vector3 eulerAngles) : this(eulerAngles.X, eulerAngles.Y, eulerAngles.Z) { }

        [XmlIgnore]
        public float X { get => Xyz.X; set => Xyz.X = value; }

        [XmlIgnore]
        public float Y { get => Xyz.Y; set => Xyz.Y = value; }

        [XmlIgnore]
        public float Z { get => Xyz.Z; set => Xyz.Z = value; }

        public void ToAxisAngle(out Vector3 axis, out float angle) {
            var result = ToAxisAngle();
            axis = result.Xyz;
            angle = result.W;
        }

        public Vector4 ToAxisAngle() {
            var q = this;
            if (Math.Abs(q.W) > 1.0f) {
                q.Normalize();
            }

            var result = new Vector4 {
                W = 2.0f * MathF.Acos(q.W) // angle
            };

            var den = MathF.Sqrt(1.0f - (q.W * q.W));
            if (den > 0.0001f) {
                result.Xyz = q.Xyz / den;
            }
            else {
                // This occurs when the angle is zero.
                // Not a problem: just set an arbitrary normalized axis.
                result.Xyz = Vector3.UnitX;
            }

            return result;
        }

        public void ToEulerAngles(out Vector3 angles) => angles = ToEulerAngles();

        public Vector3 ToEulerAngles() {
            var q = this;

            Vector3 eulerAngles;

            // Threshold for the singularities found at the north/south poles.
            const float SINGULARITY_THRESHOLD = 0.4999995f;

            var sqw = q.W * q.W;
            var sqx = q.X * q.X;
            var sqy = q.Y * q.Y;
            var sqz = q.Z * q.Z;
            var unit = sqx + sqy + sqz + sqw; // if normalised is one, otherwise is correction factor
            var singularityTest = (q.X * q.Z) + (q.W * q.Y);

            if (singularityTest > SINGULARITY_THRESHOLD * unit) {
                eulerAngles.Z = (float)(2 * Math.Atan2(q.X, q.W));
                eulerAngles.Y = MathHelper.PiOver2;
                eulerAngles.X = 0;
            }
            else if (singularityTest < -SINGULARITY_THRESHOLD * unit) {
                eulerAngles.Z = (float)(-2 * Math.Atan2(q.X, q.W));
                eulerAngles.Y = -MathHelper.PiOver2;
                eulerAngles.X = 0;
            }
            else {
                eulerAngles.Z = MathF.Atan2(2 * ((q.W * q.Z) - (q.X * q.Y)), sqw + sqx - sqy - sqz);
                eulerAngles.Y = MathF.Asin(2 * singularityTest / unit);
                eulerAngles.X = MathF.Atan2(2 * ((q.W * q.X) - (q.Y * q.Z)), sqw - sqx - sqy + sqz);
            }

            return eulerAngles;
        }

        public float Length => MathF.Sqrt((W * W) + Xyz.LengthSquared);

        public float LengthSquared => (W * W) + Xyz.LengthSquared;

        public Quaternion Normalized() {
            var q = this;
            q.Normalize();
            return q;
        }

        public void Invert() => Invert(this, out this);

        public Quaternion Inverted() {
            var q = this;
            q.Invert();
            return q;
        }

        public void Normalize() {
            var scale = 1.0f / Length;
            Xyz *= scale;
            W *= scale;
        }

        public void Conjugate() => Xyz = -Xyz;

        public static readonly Quaternion Identity = new Quaternion(0, 0, 0, 1);

        [Pure]
        public static Quaternion Add(Quaternion left, Quaternion right) => new Quaternion(
            left.Xyz + right.Xyz,
            left.W + right.W);

        public static void Add(Quaternion left, Quaternion right, out Quaternion result) => result = new Quaternion(
            left.Xyz + right.Xyz,
            left.W + right.W);

        [Pure]
        public static Quaternion Sub(Quaternion left, Quaternion right) => new Quaternion(
            left.Xyz - right.Xyz,
            left.W - right.W);

        public static void Sub(Quaternion left, Quaternion right, out Quaternion result) => result = new Quaternion(
            left.Xyz - right.Xyz,
            left.W - right.W);

        [Pure]
        public static Quaternion Multiply(Quaternion left, Quaternion right) {
            Multiply(left, right, out Quaternion result);
            return result;
        }

        public static void Multiply(Quaternion left, Quaternion right, out Quaternion result) => result = new Quaternion(
            (right.W * left.Xyz) + (left.W * right.Xyz) + Vector3.Cross(left.Xyz, right.Xyz),
            (left.W * right.W) - Vector3.Dot(left.Xyz, right.Xyz));

        public static void Multiply(Quaternion quaternion, float scale, out Quaternion result) => result = new Quaternion(
            quaternion.X * scale,
            quaternion.Y * scale,
            quaternion.Z * scale,
            quaternion.W * scale);

        [Pure]
        public static Quaternion Multiply(Quaternion quaternion, float scale) => new Quaternion(
            quaternion.X * scale,
            quaternion.Y * scale,
            quaternion.Z * scale,
            quaternion.W * scale);

        [Pure]
        public static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.Xyz, q.W);

        public static void Conjugate(Quaternion q, out Quaternion result) => result = new Quaternion(-q.Xyz, q.W);

        [Pure]
        public static Quaternion Invert(Quaternion q) {
            Invert(q, out Quaternion result);
            return result;
        }

        public static void Invert(Quaternion q, out Quaternion result) {
            var lengthSq = q.LengthSquared;
            if (lengthSq != 0.0) {
                var i = 1.0f / lengthSq;
                result = new Quaternion(q.Xyz * -i, q.W * i);
            }
            else {
                result = q;
            }
        }

        [Pure]
        public static Quaternion Normalize(Quaternion q) {
            Normalize(q, out Quaternion result);
            return result;
        }

        public static void Normalize(Quaternion q, out Quaternion result) {
            var scale = 1.0f / q.Length;
            result = new Quaternion(q.Xyz * scale, q.W * scale);
        }

        [Pure]
        public static Quaternion FromAxisAngle(Vector3 axis, float angle) {
            if (axis.LengthSquared == 0.0f) return Identity;

            var result = Identity;

            angle *= 0.5f;
            axis.Normalize();
            result.Xyz = axis * MathF.Sin(angle);
            result.W = MathF.Cos(angle);

            return Normalize(result);
        }

        [Pure]
        public static Quaternion FromEulerAngles(float pitch, float yaw, float roll) => new Quaternion(pitch, yaw, roll);

        [Pure]
        public static Quaternion FromEulerAngles(Vector3 eulerAngles) => new Quaternion(eulerAngles);

        public static void FromEulerAngles(Vector3 eulerAngles, out Quaternion result) {
            var c1 = MathF.Cos(eulerAngles.X * 0.5f);
            var c2 = MathF.Cos(eulerAngles.Y * 0.5f);
            var c3 = MathF.Cos(eulerAngles.Z * 0.5f);
            var s1 = MathF.Sin(eulerAngles.X * 0.5f);
            var s2 = MathF.Sin(eulerAngles.Y * 0.5f);
            var s3 = MathF.Sin(eulerAngles.Z * 0.5f);

            result.W = (c1 * c2 * c3) - (s1 * s2 * s3);
            result.Xyz.X = (s1 * c2 * c3) + (c1 * s2 * s3);
            result.Xyz.Y = (c1 * s2 * c3) - (s1 * c2 * s3);
            result.Xyz.Z = (c1 * c2 * s3) + (s1 * s2 * c3);
        }

        [Pure]
        public static void ToEulerAngles(Quaternion q, out Vector3 result) => q.ToEulerAngles(out result);

        [Pure]
        public static Quaternion FromMatrix(Matrix3 matrix) {
            FromMatrix(matrix, out Quaternion result);
            return result;
        }

        public static void FromMatrix(Matrix3 matrix, out Quaternion result) {
            var trace = matrix.Trace;

            if (trace > 0) {
                var s = MathF.Sqrt(trace + 1) * 2;
                var invS = 1f / s;

                result.W = s * 0.25f;
                result.Xyz.X = (matrix.Row2.Y - matrix.Row1.Z) * invS;
                result.Xyz.Y = (matrix.Row0.Z - matrix.Row2.X) * invS;
                result.Xyz.Z = (matrix.Row1.X - matrix.Row0.Y) * invS;
            }
            else {
                float m00 = matrix.Row0.X, m11 = matrix.Row1.Y, m22 = matrix.Row2.Z;

                if (m00 > m11 && m00 > m22) {
                    var s = MathF.Sqrt(1 + m00 - m11 - m22) * 2;
                    var invS = 1f / s;

                    result.W = (matrix.Row2.Y - matrix.Row1.Z) * invS;
                    result.Xyz.X = s * 0.25f;
                    result.Xyz.Y = (matrix.Row0.Y + matrix.Row1.X) * invS;
                    result.Xyz.Z = (matrix.Row0.Z + matrix.Row2.X) * invS;
                }
                else if (m11 > m22) {
                    var s = MathF.Sqrt(1 + m11 - m00 - m22) * 2;
                    var invS = 1f / s;

                    result.W = (matrix.Row0.Z - matrix.Row2.X) * invS;
                    result.Xyz.X = (matrix.Row0.Y + matrix.Row1.X) * invS;
                    result.Xyz.Y = s * 0.25f;
                    result.Xyz.Z = (matrix.Row1.Z + matrix.Row2.Y) * invS;
                }
                else {
                    var s = MathF.Sqrt(1 + m22 - m00 - m11) * 2;
                    var invS = 1f / s;

                    result.W = (matrix.Row1.X - matrix.Row0.Y) * invS;
                    result.Xyz.X = (matrix.Row0.Z + matrix.Row2.X) * invS;
                    result.Xyz.Y = (matrix.Row1.Z + matrix.Row2.Y) * invS;
                    result.Xyz.Z = s * 0.25f;
                }
            }
        }

        [Pure]
        public static Quaternion Slerp(Quaternion q1, Quaternion q2, float blend) {
            // if either input is zero, return the other.
            if (q1.LengthSquared == 0.0f) {
                if (q2.LengthSquared == 0.0f) {
                    return Identity;
                }

                return q2;
            }

            if (q2.LengthSquared == 0.0f) {
                return q1;
            }

            var cosHalfAngle = (q1.W * q2.W) + Vector3.Dot(q1.Xyz, q2.Xyz);

            if (cosHalfAngle >= 1.0f || cosHalfAngle <= -1.0f) {
                // angle = 0.0f, so just return one input.
                return q1;
            }

            if (cosHalfAngle < 0.0f) {
                q2.Xyz = -q2.Xyz;
                q2.W = -q2.W;
                cosHalfAngle = -cosHalfAngle;
            }

            float blendA;
            float blendB;
            if (cosHalfAngle < 0.99f) {
                // do proper slerp for big angles
                var halfAngle = MathF.Acos(cosHalfAngle);
                var sinHalfAngle = MathF.Sin(halfAngle);
                var oneOverSinHalfAngle = 1.0f / sinHalfAngle;
                blendA = MathF.Sin(halfAngle * (1.0f - blend)) * oneOverSinHalfAngle;
                blendB = MathF.Sin(halfAngle * blend) * oneOverSinHalfAngle;
            }
            else {
                // do lerp if angle is really small.
                blendA = 1.0f - blend;
                blendB = blend;
            }

            var result = new Quaternion((blendA * q1.Xyz) + (blendB * q2.Xyz), (blendA * q1.W) + (blendB * q2.W));
            if (result.LengthSquared > 0.0f) {
                return Normalize(result);
            }

            return Identity;
        }

        [Pure]
        public static Quaternion operator +(Quaternion left, Quaternion right) {
            left.Xyz += right.Xyz;
            left.W += right.W;
            return left;
        }

        [Pure]
        public static Quaternion operator -(Quaternion left, Quaternion right) {
            left.Xyz -= right.Xyz;
            left.W -= right.W;
            return left;
        }

        [Pure]
        public static Quaternion operator *(Quaternion left, Quaternion right) {
            Multiply(left, right, out left);
            return left;
        }

        [Pure]
        public static Quaternion operator *(Quaternion quaternion, float scale) {
            Multiply(quaternion, scale, out quaternion);
            return quaternion;
        }

        [Pure]
        public static Quaternion operator *(float scale, Quaternion quaternion) => new Quaternion(
            quaternion.X * scale,
            quaternion.Y * scale,
            quaternion.Z * scale,
            quaternion.W * scale);

        public static bool operator ==(Quaternion left, Quaternion right) => left.Equals(right);

        public static bool operator !=(Quaternion left, Quaternion right) => !left.Equals(right);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Quaternion && Equals((Quaternion)obj);

        /// <inheritdoc />
        public bool Equals(Quaternion other) => Xyz.Equals(other.Xyz) && W == other.W;

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Xyz, W);


        public override string ToString() => $"(V:{Xyz} W:{W})";
    }
}

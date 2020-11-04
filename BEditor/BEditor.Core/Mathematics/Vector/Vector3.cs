using System;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace BEditor.Mathematics {
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector3 : IEquatable<Vector3> {
        public float X;

        public float Y;

        public float Z;

        public Vector3(float value) {
            X = value;
            Y = value;
            Z = value;
        }

        public Vector3(float x, float y, float z) {
            X = x;
            Y = y;
            Z = z;
        }

        public Vector3(Vector2 v) {
            X = v.X;
            Y = v.Y;
            Z = 0.0f;
        }

        public Vector3(Vector3 v) {
            X = v.X;
            Y = v.Y;
            Z = v.Z;
        }

        public Vector3(Vector4 v) {
            X = v.X;
            Y = v.Y;
            Z = v.Z;
        }

        public float this[int index] {
            get {
                if (index == 0) {
                    return X;
                }

                if (index == 1) {
                    return Y;
                }

                if (index == 2) {
                    return Z;
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
                else if (index == 2) {
                    Z = value;
                }
                else {
                    throw new IndexOutOfRangeException("You tried to set this vector at index: " + index);
                }
            }
        }

        public float Length => MathF.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public float LengthFast => 1.0f / MathHelper.InverseSqrtFast((X * X) + (Y * Y) + (Z * Z));

        public float LengthSquared => (X * X) + (Y * Y) + (Z * Z);

        public Vector3 Normalized() {
            var v = this;
            v.Normalize();
            return v;
        }

        public void Normalize() {
            var scale = 1.0f / Length;
            X *= scale;
            Y *= scale;
            Z *= scale;
        }

        public void NormalizeFast() {
            var scale = MathHelper.InverseSqrtFast((X * X) + (Y * Y) + (Z * Z));
            X *= scale;
            Y *= scale;
            Z *= scale;
        }

        public static readonly Vector3 UnitX = new Vector3(1, 0, 0);
        public static readonly Vector3 UnitY = new Vector3(0, 1, 0);
        public static readonly Vector3 UnitZ = new Vector3(0, 0, 1);
        public static readonly Vector3 Zero = new Vector3(0, 0, 0);
        public static readonly Vector3 One = new Vector3(1, 1, 1);
        public static readonly Vector3 PositiveInfinity = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        public static readonly Vector3 NegativeInfinity = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        public static readonly int SizeInBytes = Marshal.SizeOf<Vector3>();

        [Pure]
        public static Vector3 Add(Vector3 a, Vector3 b) {
            Add(a, b, out a);
            return a;
        }

        public static void Add(Vector3 a, Vector3 b, out Vector3 result) {
            result.X = a.X + b.X;
            result.Y = a.Y + b.Y;
            result.Z = a.Z + b.Z;
        }

        [Pure]
        public static Vector3 Subtract(Vector3 a, Vector3 b) {
            Subtract(a, b, out a);
            return a;
        }

        public static void Subtract(Vector3 a, Vector3 b, out Vector3 result) {
            result.X = a.X - b.X;
            result.Y = a.Y - b.Y;
            result.Z = a.Z - b.Z;
        }

        [Pure]
        public static Vector3 Multiply(Vector3 vector, float scale) {
            Multiply(vector, scale, out vector);
            return vector;
        }

        public static void Multiply(Vector3 vector, float scale, out Vector3 result) {
            result.X = vector.X * scale;
            result.Y = vector.Y * scale;
            result.Z = vector.Z * scale;
        }

        [Pure]
        public static Vector3 Multiply(Vector3 vector, Vector3 scale) {
            Multiply(vector, scale, out vector);
            return vector;
        }

        public static void Multiply(Vector3 vector, Vector3 scale, out Vector3 result) {
            result.X = vector.X * scale.X;
            result.Y = vector.Y * scale.Y;
            result.Z = vector.Z * scale.Z;
        }

        [Pure]
        public static Vector3 Divide(Vector3 vector, float scale) {
            Divide(vector, scale, out vector);
            return vector;
        }

        public static void Divide(Vector3 vector, float scale, out Vector3 result) {
            result.X = vector.X / scale;
            result.Y = vector.Y / scale;
            result.Z = vector.Z / scale;
        }

        [Pure]
        public static Vector3 Divide(Vector3 vector, Vector3 scale) {
            Divide(vector, scale, out vector);
            return vector;
        }

        public static void Divide(Vector3 vector, Vector3 scale, out Vector3 result) {
            result.X = vector.X / scale.X;
            result.Y = vector.Y / scale.Y;
            result.Z = vector.Z / scale.Z;
        }

        [Pure]
        public static Vector3 ComponentMin(Vector3 a, Vector3 b) {
            a.X = a.X < b.X ? a.X : b.X;
            a.Y = a.Y < b.Y ? a.Y : b.Y;
            a.Z = a.Z < b.Z ? a.Z : b.Z;
            return a;
        }

        public static void ComponentMin(Vector3 a, Vector3 b, out Vector3 result) {
            result.X = a.X < b.X ? a.X : b.X;
            result.Y = a.Y < b.Y ? a.Y : b.Y;
            result.Z = a.Z < b.Z ? a.Z : b.Z;
        }

        [Pure]
        public static Vector3 ComponentMax(Vector3 a, Vector3 b) {
            a.X = a.X > b.X ? a.X : b.X;
            a.Y = a.Y > b.Y ? a.Y : b.Y;
            a.Z = a.Z > b.Z ? a.Z : b.Z;
            return a;
        }

        public static void ComponentMax(Vector3 a, Vector3 b, out Vector3 result) {
            result.X = a.X > b.X ? a.X : b.X;
            result.Y = a.Y > b.Y ? a.Y : b.Y;
            result.Z = a.Z > b.Z ? a.Z : b.Z;
        }

        [Pure]
        public static Vector3 MagnitudeMin(Vector3 left, Vector3 right) =>
            left.LengthSquared < right.LengthSquared ? left : right;


        public static void MagnitudeMin(Vector3 left, Vector3 right, out Vector3 result) =>
            result = left.LengthSquared < right.LengthSquared ? left : right;

        [Pure]
        public static Vector3 MagnitudeMax(Vector3 left, Vector3 right) =>
            left.LengthSquared >= right.LengthSquared ? left : right;

        public static void MagnitudeMax(Vector3 left, Vector3 right, out Vector3 result) =>
            result = left.LengthSquared >= right.LengthSquared ? left : right;

        [Pure]
        public static Vector3 Clamp(Vector3 vec, Vector3 min, Vector3 max) {
            vec.X = vec.X < min.X ? min.X : vec.X > max.X ? max.X : vec.X;
            vec.Y = vec.Y < min.Y ? min.Y : vec.Y > max.Y ? max.Y : vec.Y;
            vec.Z = vec.Z < min.Z ? min.Z : vec.Z > max.Z ? max.Z : vec.Z;
            return vec;
        }

        public static void Clamp(Vector3 vec, Vector3 min, Vector3 max, out Vector3 result) {
            result.X = vec.X < min.X ? min.X : vec.X > max.X ? max.X : vec.X;
            result.Y = vec.Y < min.Y ? min.Y : vec.Y > max.Y ? max.Y : vec.Y;
            result.Z = vec.Z < min.Z ? min.Z : vec.Z > max.Z ? max.Z : vec.Z;
        }

        [Pure]
        public static float Distance(Vector3 vec1, Vector3 vec2) {
            Distance(vec1, vec2, out float result);
            return result;
        }

        public static void Distance(Vector3 vec1, Vector3 vec2, out float result) =>
            result = MathF.Sqrt(((vec2.X - vec1.X) * (vec2.X - vec1.X)) +
                                ((vec2.Y - vec1.Y) * (vec2.Y - vec1.Y)) +
                                ((vec2.Z - vec1.Z) * (vec2.Z - vec1.Z)));

        [Pure]
        public static float DistanceSquared(Vector3 vec1, Vector3 vec2) {
            DistanceSquared(vec1, vec2, out float result);
            return result;
        }

        public static void DistanceSquared(Vector3 vec1, Vector3 vec2, out float result) =>
            result = ((vec2.X - vec1.X) * (vec2.X - vec1.X)) +
                     ((vec2.Y - vec1.Y) * (vec2.Y - vec1.Y)) +
                     ((vec2.Z - vec1.Z) * (vec2.Z - vec1.Z));

        [Pure]
        public static Vector3 Normalize(Vector3 vec) {
            var scale = 1.0f / vec.Length;
            vec.X *= scale;
            vec.Y *= scale;
            vec.Z *= scale;
            return vec;
        }

        public static void Normalize(Vector3 vec, out Vector3 result) {
            var scale = 1.0f / vec.Length;
            result.X = vec.X * scale;
            result.Y = vec.Y * scale;
            result.Z = vec.Z * scale;
        }

        [Pure]
        public static Vector3 NormalizeFast(Vector3 vec) {
            var scale = MathHelper.InverseSqrtFast((vec.X * vec.X) + (vec.Y * vec.Y) + (vec.Z * vec.Z));
            vec.X *= scale;
            vec.Y *= scale;
            vec.Z *= scale;
            return vec;
        }

        public static void NormalizeFast(Vector3 vec, out Vector3 result) {
            var scale = MathHelper.InverseSqrtFast((vec.X * vec.X) + (vec.Y * vec.Y) + (vec.Z * vec.Z));
            result.X = vec.X * scale;
            result.Y = vec.Y * scale;
            result.Z = vec.Z * scale;
        }

        [Pure]
        public static float Dot(Vector3 left, Vector3 right) => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

        public static void Dot(Vector3 left, Vector3 right, out float result) => result = (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

        [Pure]
        public static Vector3 Cross(Vector3 left, Vector3 right) {
            Cross(left, right, out Vector3 result);
            return result;
        }

        public static void Cross(Vector3 left, Vector3 right, out Vector3 result) {
            result.X = (left.Y * right.Z) - (left.Z * right.Y);
            result.Y = (left.Z * right.X) - (left.X * right.Z);
            result.Z = (left.X * right.Y) - (left.Y * right.X);
        }

        [Pure]
        public static Vector3 Lerp(Vector3 a, Vector3 b, float blend) {
            a.X = (blend * (b.X - a.X)) + a.X;
            a.Y = (blend * (b.Y - a.Y)) + a.Y;
            a.Z = (blend * (b.Z - a.Z)) + a.Z;
            return a;
        }

        public static void Lerp(Vector3 a, Vector3 b, float blend, out Vector3 result) {
            result.X = (blend * (b.X - a.X)) + a.X;
            result.Y = (blend * (b.Y - a.Y)) + a.Y;
            result.Z = (blend * (b.Z - a.Z)) + a.Z;
        }

        [Pure]
        public static Vector3 BaryCentric(Vector3 a, Vector3 b, Vector3 c, float u, float v) {
            BaryCentric(a, b, c, u, v, out var result);
            return result;
        }

        [Pure]
        public static void BaryCentric(Vector3 a, Vector3 b, Vector3 c, float u, float v, out Vector3 result) {
            Subtract(b, a, out var ab);
            Multiply(ab, u, out var abU);
            Add(a, abU, out var uPos);

            Subtract(c, a, out var ac);
            Multiply(ac, v, out var acV);
            Add(uPos, acV, out result);
        }

        [Pure]
        public static Vector3 TransformVector(Vector3 vec, Matrix4 mat) {
            TransformVector(vec, mat, out Vector3 result);
            return result;
        }

        public static void TransformVector(Vector3 vec, Matrix4 mat, out Vector3 result) {
            result.X = (vec.X * mat.Row0.X) +
                       (vec.Y * mat.Row1.X) +
                       (vec.Z * mat.Row2.X);

            result.Y = (vec.X * mat.Row0.Y) +
                       (vec.Y * mat.Row1.Y) +
                       (vec.Z * mat.Row2.Y);

            result.Z = (vec.X * mat.Row0.Z) +
                       (vec.Y * mat.Row1.Z) +
                       (vec.Z * mat.Row2.Z);
        }

        [Pure]
        public static Vector3 TransformNormal(Vector3 norm, Matrix4 mat) {
            TransformNormal(norm, mat, out Vector3 result);
            return result;
        }

        public static void TransformNormal(Vector3 norm, Matrix4 mat, out Vector3 result) {
            var inverse = Matrix4.Invert(mat);
            TransformNormalInverse(norm, inverse, out result);
        }

        [Pure]
        public static Vector3 TransformNormalInverse(Vector3 norm, Matrix4 invMat) {
            TransformNormalInverse(norm, invMat, out Vector3 result);
            return result;
        }

        public static void TransformNormalInverse(Vector3 norm, Matrix4 invMat, out Vector3 result) {
            result.X = (norm.X * invMat.Row0.X) +
                       (norm.Y * invMat.Row0.Y) +
                       (norm.Z * invMat.Row0.Z);

            result.Y = (norm.X * invMat.Row1.X) +
                       (norm.Y * invMat.Row1.Y) +
                       (norm.Z * invMat.Row1.Z);

            result.Z = (norm.X * invMat.Row2.X) +
                       (norm.Y * invMat.Row2.Y) +
                       (norm.Z * invMat.Row2.Z);
        }

        [Pure]
        public static Vector3 TransformPosition(Vector3 pos, Matrix4 mat) {
            TransformPosition(pos, mat, out Vector3 result);
            return result;
        }

        public static void TransformPosition(Vector3 pos, Matrix4 mat, out Vector3 result) {
            result.X = (pos.X * mat.Row0.X) +
                       (pos.Y * mat.Row1.X) +
                       (pos.Z * mat.Row2.X) +
                       mat.Row3.X;

            result.Y = (pos.X * mat.Row0.Y) +
                       (pos.Y * mat.Row1.Y) +
                       (pos.Z * mat.Row2.Y) +
                       mat.Row3.Y;

            result.Z = (pos.X * mat.Row0.Z) +
                       (pos.Y * mat.Row1.Z) +
                       (pos.Z * mat.Row2.Z) +
                       mat.Row3.Z;
        }

        [Pure]
        public static Vector3 TransformRow(Vector3 vec, Matrix3 mat) {
            TransformRow(vec, mat, out Vector3 result);
            return result;
        }

        public static void TransformRow(Vector3 vec, Matrix3 mat, out Vector3 result) {
            result.X = (vec.X * mat.Row0.X) + (vec.Y * mat.Row1.X) + (vec.Z * mat.Row2.X);
            result.Y = (vec.X * mat.Row0.Y) + (vec.Y * mat.Row1.Y) + (vec.Z * mat.Row2.Y);
            result.Z = (vec.X * mat.Row0.Z) + (vec.Y * mat.Row1.Z) + (vec.Z * mat.Row2.Z);
        }

        [Pure]
        public static Vector3 Transform(Vector3 vec, Quaternion quat) {
            Transform(vec, quat, out Vector3 result);
            return result;
        }

        public static void Transform(Vector3 vec, Quaternion quat, out Vector3 result) {
            // Since vec.W == 0, we can optimize quat * vec * quat^-1 as follows:
            // vec + 2.0 * cross(quat.xyz, cross(quat.xyz, vec) + quat.w * vec)
            Vector3 xyz = quat.Xyz;
            Cross(xyz, vec, out Vector3 temp);
            Multiply(vec, quat.W, out Vector3 temp2);
            Add(temp, temp2, out temp);
            Cross(xyz, temp, out temp2);
            Multiply(temp2, 2f, out temp2);
            Add(vec, temp2, out result);
        }

        [Pure]
        public static Vector3 TransformColumn(Matrix3 mat, Vector3 vec) {
            TransformColumn(mat, vec, out Vector3 result);
            return result;
        }

        public static void TransformColumn(Matrix3 mat, Vector3 vec, out Vector3 result) {
            result.X = (mat.Row0.X * vec.X) + (mat.Row0.Y * vec.Y) + (mat.Row0.Z * vec.Z);
            result.Y = (mat.Row1.X * vec.X) + (mat.Row1.Y * vec.Y) + (mat.Row1.Z * vec.Z);
            result.Z = (mat.Row2.X * vec.X) + (mat.Row2.Y * vec.Y) + (mat.Row2.Z * vec.Z);
        }

        [Pure]
        public static Vector3 TransformPerspective(Vector3 vec, Matrix4 mat) {
            TransformPerspective(vec, mat, out Vector3 result);
            return result;
        }

        public static void TransformPerspective(Vector3 vec, Matrix4 mat, out Vector3 result) {
            var v = new Vector4(vec.X, vec.Y, vec.Z, 1);
            Vector4.TransformRow(v, mat, out v);
            result.X = v.X / v.W;
            result.Y = v.Y / v.W;
            result.Z = v.Z / v.W;
        }

        [Pure]
        public static float CalculateAngle(Vector3 first, Vector3 second) {
            CalculateAngle(first, second, out float result);
            return result;
        }

        public static void CalculateAngle(Vector3 first, Vector3 second, out float result) {
            Dot(first, second, out float temp);
            result = MathF.Acos(Math.Clamp(temp / (first.Length * second.Length), -1.0f, 1.0f));
        }

        [Pure]
        public static Vector3 Project(Vector3 vector, float x, float y, float width, float height, float minZ, float maxZ, Matrix4 worldViewProjection) {
            Vector4 result;

            result.X =
                (vector.X * worldViewProjection.M11) +
                (vector.Y * worldViewProjection.M21) +
                (vector.Z * worldViewProjection.M31) +
                worldViewProjection.M41;

            result.Y =
                (vector.X * worldViewProjection.M12) +
                (vector.Y * worldViewProjection.M22) +
                (vector.Z * worldViewProjection.M32) +
                worldViewProjection.M42;

            result.Z =
                (vector.X * worldViewProjection.M13) +
                (vector.Y * worldViewProjection.M23) +
                (vector.Z * worldViewProjection.M33) +
                worldViewProjection.M43;

            result.W =
                (vector.X * worldViewProjection.M14) +
                (vector.Y * worldViewProjection.M24) +
                (vector.Z * worldViewProjection.M34) +
                worldViewProjection.M44;

            result /= result.W;

            result.X = x + (width * ((result.X + 1.0f) / 2.0f));
            result.Y = y + (height * ((result.Y + 1.0f) / 2.0f));
            result.Z = minZ + ((maxZ - minZ) * ((result.Z + 1.0f) / 2.0f));

            return new Vector3(result.X, result.Y, result.Z);
        }

        [Pure]
        public static Vector3 Unproject(Vector3 vector, float x, float y, float width, float height, float minZ, float maxZ, Matrix4 inverseWorldViewProjection) {
            float tempX = ((vector.X - x) / width * 2.0f) - 1.0f;
            float tempY = ((vector.Y - y) / height * 2.0f) - 1.0f;
            float tempZ = ((vector.Z - minZ) / (maxZ - minZ) * 2.0f) - 1.0f;

            Vector3 result;
            result.X =
                (tempX * inverseWorldViewProjection.M11) +
                (tempY * inverseWorldViewProjection.M21) +
                (tempZ * inverseWorldViewProjection.M31) +
                inverseWorldViewProjection.M41;

            result.Y =
                (tempX * inverseWorldViewProjection.M12) +
                (tempY * inverseWorldViewProjection.M22) +
                (tempZ * inverseWorldViewProjection.M32) +
                inverseWorldViewProjection.M42;

            result.Z =
                (tempX * inverseWorldViewProjection.M13) +
                (tempY * inverseWorldViewProjection.M23) +
                (tempZ * inverseWorldViewProjection.M33) +
                inverseWorldViewProjection.M43;

            float tempW =
                (tempX * inverseWorldViewProjection.M14) +
                (tempY * inverseWorldViewProjection.M24) +
                (tempZ * inverseWorldViewProjection.M34) +
                inverseWorldViewProjection.M44;

            result /= tempW;

            return result;
        }

        [XmlIgnore]
        public Vector2 Xy {
            get => Unsafe.As<Vector3, Vector2>(ref this);
            set {
                X = value.X;
                Y = value.Y;
            }
        }

        [XmlIgnore]
        public Vector2 Xz {
            get => new Vector2(X, Z);
            set {
                X = value.X;
                Z = value.Y;
            }
        }

        [XmlIgnore]
        public Vector2 Yx {
            get => new Vector2(Y, X);
            set {
                Y = value.X;
                X = value.Y;
            }
        }

        [XmlIgnore]
        public Vector2 Yz {
            get => new Vector2(Y, Z);
            set {
                Y = value.X;
                Z = value.Y;
            }
        }

        [XmlIgnore]
        public Vector2 Zx {
            get => new Vector2(Z, X);
            set {
                Z = value.X;
                X = value.Y;
            }
        }

        [XmlIgnore]
        public Vector2 Zy {
            get => new Vector2(Z, Y);
            set {
                Z = value.X;
                Y = value.Y;
            }
        }

        [XmlIgnore]
        public Vector3 Xzy {
            get => new Vector3(X, Z, Y);
            set {
                X = value.X;
                Z = value.Y;
                Y = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Yxz {
            get => new Vector3(Y, X, Z);
            set {
                Y = value.X;
                X = value.Y;
                Z = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Yzx {
            get => new Vector3(Y, Z, X);
            set {
                Y = value.X;
                Z = value.Y;
                X = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Zxy {
            get => new Vector3(Z, X, Y);
            set {
                Z = value.X;
                X = value.Y;
                Y = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Zyx {
            get => new Vector3(Z, Y, X);
            set {
                Z = value.X;
                Y = value.Y;
                X = value.Z;
            }
        }

        #region ‰‰ŽZŽq

        [Pure]
        public static Vector3 operator +(Vector3 left, Vector3 right) {
            left.X += right.X;
            left.Y += right.Y;
            left.Z += right.Z;
            return left;
        }

        [Pure]
        public static Vector3 operator -(Vector3 left, Vector3 right) {
            left.X -= right.X;
            left.Y -= right.Y;
            left.Z -= right.Z;
            return left;
        }

        [Pure]
        public static Vector3 operator -(Vector3 vec) {
            vec.X = -vec.X;
            vec.Y = -vec.Y;
            vec.Z = -vec.Z;
            return vec;
        }

        [Pure]
        public static Vector3 operator *(Vector3 vec, float scale) {
            vec.X *= scale;
            vec.Y *= scale;
            vec.Z *= scale;
            return vec;
        }

        [Pure]
        public static Vector3 operator *(float scale, Vector3 vec) {
            vec.X *= scale;
            vec.Y *= scale;
            vec.Z *= scale;
            return vec;
        }

        [Pure]
        public static Vector3 operator *(Vector3 vec, Vector3 scale) {
            vec.X *= scale.X;
            vec.Y *= scale.Y;
            vec.Z *= scale.Z;
            return vec;
        }

        [Pure]
        public static Vector3 operator *(Vector3 vec, Matrix3 mat) {
            TransformRow(vec, mat, out Vector3 result);
            return result;
        }

        [Pure]
        public static Vector3 operator *(Matrix3 mat, Vector3 vec) {
            TransformColumn(mat, vec, out Vector3 result);
            return result;
        }

        [Pure]
        public static Vector3 operator *(Quaternion quat, Vector3 vec) {
            Transform(vec, quat, out Vector3 result);
            return result;
        }

        [Pure]
        public static Vector3 operator /(Vector3 vec, float scale) {
            vec.X /= scale;
            vec.Y /= scale;
            vec.Z /= scale;
            return vec;
        }

        public static bool operator ==(Vector3 left, Vector3 right) => left.Equals(right);

        public static bool operator !=(Vector3 left, Vector3 right) => !left.Equals(right);

        [Pure]
        public static implicit operator Vector3((float X, float Y, float Z) values) => new Vector3(values.X, values.Y, values.Z);

        #endregion

        /// <inheritdoc />
        public override string ToString() => $"(X:{X} Y:{Y} Z:{Z})";

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Vector3 vector && Equals(vector);

        /// <inheritdoc />
        public bool Equals(Vector3 other) => X == other.X && Y == other.Y && Z == other.Z;

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        [Pure]
        public void Deconstruct(out float x, out float y, out float z) {
            x = X;
            y = Y;
            z = Z;
        }
    }
}

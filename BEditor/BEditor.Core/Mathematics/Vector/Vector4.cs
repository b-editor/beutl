using System;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace BEditor.Mathematics {
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector4 : IEquatable<Vector4> {
        public float X;

        public float Y;

        public float Z;

        public float W;


        public static readonly Vector4 UnitX = new Vector4(1, 0, 0, 0);
        public static readonly Vector4 UnitY = new Vector4(0, 1, 0, 0);
        public static readonly Vector4 UnitZ = new Vector4(0, 0, 1, 0);
        public static readonly Vector4 UnitW = new Vector4(0, 0, 0, 1);
        public static readonly Vector4 Zero = new Vector4(0, 0, 0, 0);
        public static readonly Vector4 One = new Vector4(1, 1, 1, 1);
        public static readonly Vector4 PositiveInfinity = new Vector4(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        public static readonly Vector4 NegativeInfinity = new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        public static readonly int SizeInBytes = Unsafe.SizeOf<Vector4>();

        public Vector4(float value) {
            X = value;
            Y = value;
            Z = value;
            W = value;
        }

        public Vector4(float x, float y, float z, float w) {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public Vector4(Vector2 v) {
            X = v.X;
            Y = v.Y;
            Z = 0.0f;
            W = 0.0f;
        }

        public Vector4(Vector3 v) {
            X = v.X;
            Y = v.Y;
            Z = v.Z;
            W = 0.0f;
        }

        public Vector4(Vector3 v, float w) {
            X = v.X;
            Y = v.Y;
            Z = v.Z;
            W = w;
        }

        public Vector4(Vector4 v) {
            X = v.X;
            Y = v.Y;
            Z = v.Z;
            W = v.W;
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

                if (index == 3) {
                    return W;
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
                else if (index == 3) {
                    W = value;
                }
                else {
                    throw new IndexOutOfRangeException("You tried to set this vector at index: " + index);
                }
            }
        }

        public float Length => MathF.Sqrt((X * X) + (Y * Y) + (Z * Z) + (W * W));

        public float LengthFast => 1.0f / MathHelper.InverseSqrtFast((X * X) + (Y * Y) + (Z * Z) + (W * W));

        public float LengthSquared => (X * X) + (Y * Y) + (Z * Z) + (W * W);

        public Vector4 Normalized() {
            var v = this;
            v.Normalize();
            return v;
        }

        public void Normalize() {
            var scale = 1.0f / Length;
            X *= scale;
            Y *= scale;
            Z *= scale;
            W *= scale;
        }

        public void NormalizeFast() {
            var scale = MathHelper.InverseSqrtFast((X * X) + (Y * Y) + (Z * Z) + (W * W));
            X *= scale;
            Y *= scale;
            Z *= scale;
            W *= scale;
        }

        [Pure]
        public static Vector4 Add(Vector4 a, Vector4 b) {
            Add(a, b, out a);
            return a;
        }

        public static void Add(Vector4 a, Vector4 b, out Vector4 result) {
            result.X = a.X + b.X;
            result.Y = a.Y + b.Y;
            result.Z = a.Z + b.Z;
            result.W = a.W + b.W;
        }

        [Pure]
        public static Vector4 Subtract(Vector4 a, Vector4 b) {
            Subtract(a, b, out a);
            return a;
        }

        public static void Subtract(Vector4 a, Vector4 b, out Vector4 result) {
            result.X = a.X - b.X;
            result.Y = a.Y - b.Y;
            result.Z = a.Z - b.Z;
            result.W = a.W - b.W;
        }

        [Pure]
        public static Vector4 Multiply(Vector4 vector, float scale) {
            Multiply(vector, scale, out vector);
            return vector;
        }

        public static void Multiply(Vector4 vector, float scale, out Vector4 result) {
            result.X = vector.X * scale;
            result.Y = vector.Y * scale;
            result.Z = vector.Z * scale;
            result.W = vector.W * scale;
        }

        [Pure]
        public static Vector4 Multiply(Vector4 vector, Vector4 scale) {
            Multiply(vector, scale, out vector);
            return vector;
        }

        public static void Multiply(Vector4 vector, Vector4 scale, out Vector4 result) {
            result.X = vector.X * scale.X;
            result.Y = vector.Y * scale.Y;
            result.Z = vector.Z * scale.Z;
            result.W = vector.W * scale.W;
        }

        [Pure]
        public static Vector4 Divide(Vector4 vector, float scale) {
            Divide(vector, scale, out vector);
            return vector;
        }

        public static void Divide(Vector4 vector, float scale, out Vector4 result) {
            result.X = vector.X / scale;
            result.Y = vector.Y / scale;
            result.Z = vector.Z / scale;
            result.W = vector.W / scale;
        }

        [Pure]
        public static Vector4 Divide(Vector4 vector, Vector4 scale) {
            Divide(vector, scale, out vector);
            return vector;
        }

        public static void Divide(Vector4 vector, Vector4 scale, out Vector4 result) {
            result.X = vector.X / scale.X;
            result.Y = vector.Y / scale.Y;
            result.Z = vector.Z / scale.Z;
            result.W = vector.W / scale.W;
        }

        [Pure]
        public static Vector4 ComponentMin(Vector4 a, Vector4 b) {
            a.X = a.X < b.X ? a.X : b.X;
            a.Y = a.Y < b.Y ? a.Y : b.Y;
            a.Z = a.Z < b.Z ? a.Z : b.Z;
            a.W = a.W < b.W ? a.W : b.W;
            return a;
        }

        public static void ComponentMin(Vector4 a, Vector4 b, out Vector4 result) {
            result.X = a.X < b.X ? a.X : b.X;
            result.Y = a.Y < b.Y ? a.Y : b.Y;
            result.Z = a.Z < b.Z ? a.Z : b.Z;
            result.W = a.W < b.W ? a.W : b.W;
        }

        [Pure]
        public static Vector4 ComponentMax(Vector4 a, Vector4 b) {
            a.X = a.X > b.X ? a.X : b.X;
            a.Y = a.Y > b.Y ? a.Y : b.Y;
            a.Z = a.Z > b.Z ? a.Z : b.Z;
            a.W = a.W > b.W ? a.W : b.W;
            return a;
        }

        public static void ComponentMax(Vector4 a, Vector4 b, out Vector4 result) {
            result.X = a.X > b.X ? a.X : b.X;
            result.Y = a.Y > b.Y ? a.Y : b.Y;
            result.Z = a.Z > b.Z ? a.Z : b.Z;
            result.W = a.W > b.W ? a.W : b.W;
        }

        [Pure]
        public static Vector4 MagnitudeMin(Vector4 left, Vector4 right) =>
            left.LengthSquared < right.LengthSquared ? left : right;

        public static void MagnitudeMin(Vector4 left, Vector4 right, out Vector4 result) =>
            result = left.LengthSquared < right.LengthSquared ? left : right;

        [Pure]
        public static Vector4 MagnitudeMax(Vector4 left, Vector4 right) =>
            left.LengthSquared >= right.LengthSquared ? left : right;

        public static void MagnitudeMax(Vector4 left, Vector4 right, out Vector4 result) =>
            result = left.LengthSquared >= right.LengthSquared ? left : right;

        [Pure]
        public static Vector4 Clamp(Vector4 vec, Vector4 min, Vector4 max) {
            vec.X = vec.X < min.X ? min.X : vec.X > max.X ? max.X : vec.X;
            vec.Y = vec.Y < min.Y ? min.Y : vec.Y > max.Y ? max.Y : vec.Y;
            vec.Z = vec.Z < min.Z ? min.Z : vec.Z > max.Z ? max.Z : vec.Z;
            vec.W = vec.W < min.W ? min.W : vec.W > max.W ? max.W : vec.W;
            return vec;
        }

        public static void Clamp(Vector4 vec, Vector4 min, Vector4 max, out Vector4 result) {
            result.X = vec.X < min.X ? min.X : vec.X > max.X ? max.X : vec.X;
            result.Y = vec.Y < min.Y ? min.Y : vec.Y > max.Y ? max.Y : vec.Y;
            result.Z = vec.Z < min.Z ? min.Z : vec.Z > max.Z ? max.Z : vec.Z;
            result.W = vec.W < min.W ? min.W : vec.W > max.W ? max.W : vec.W;
        }

        [Pure]
        public static Vector4 Normalize(Vector4 vec) {
            var scale = 1.0f / vec.Length;
            vec.X *= scale;
            vec.Y *= scale;
            vec.Z *= scale;
            vec.W *= scale;
            return vec;
        }

        public static void Normalize(Vector4 vec, out Vector4 result) {
            var scale = 1.0f / vec.Length;
            result.X = vec.X * scale;
            result.Y = vec.Y * scale;
            result.Z = vec.Z * scale;
            result.W = vec.W * scale;
        }

        [Pure]
        public static Vector4 NormalizeFast(Vector4 vec) {
            var scale = MathHelper.InverseSqrtFast((vec.X * vec.X) + (vec.Y * vec.Y) + (vec.Z * vec.Z) + (vec.W * vec.W));
            vec.X *= scale;
            vec.Y *= scale;
            vec.Z *= scale;
            vec.W *= scale;
            return vec;
        }

        public static void NormalizeFast(Vector4 vec, out Vector4 result) {
            var scale = MathHelper.InverseSqrtFast((vec.X * vec.X) + (vec.Y * vec.Y) + (vec.Z * vec.Z) + (vec.W * vec.W));
            result.X = vec.X * scale;
            result.Y = vec.Y * scale;
            result.Z = vec.Z * scale;
            result.W = vec.W * scale;
        }

        [Pure]
        public static float Dot(Vector4 left, Vector4 right) =>
            (left.X * right.X) +
            (left.Y * right.Y) +
            (left.Z * right.Z) +
            (left.W * right.W);

        public static void Dot(Vector4 left, Vector4 right, out float result) =>
            result = (left.X * right.X) +
                     (left.Y * right.Y) +
                     (left.Z * right.Z) +
                     (left.W * right.W);

        [Pure]
        public static Vector4 Lerp(Vector4 a, Vector4 b, float blend) {
            a.X = (blend * (b.X - a.X)) + a.X;
            a.Y = (blend * (b.Y - a.Y)) + a.Y;
            a.Z = (blend * (b.Z - a.Z)) + a.Z;
            a.W = (blend * (b.W - a.W)) + a.W;
            return a;
        }

        public static void Lerp(Vector4 a, Vector4 b, float blend, out Vector4 result) {
            result.X = (blend * (b.X - a.X)) + a.X;
            result.Y = (blend * (b.Y - a.Y)) + a.Y;
            result.Z = (blend * (b.Z - a.Z)) + a.Z;
            result.W = (blend * (b.W - a.W)) + a.W;
        }

        [Pure]
        public static Vector4 BaryCentric(Vector4 a, Vector4 b, Vector4 c, float u, float v) {
            BaryCentric(a, b, c, u, v, out var result);
            return result;
        }

        public static void BaryCentric(Vector4 a, Vector4 b, Vector4 c, float u, float v, out Vector4 result) {
            Subtract(b, a, out var ab);
            Multiply(ab, u, out var abU);
            Add(a, abU, out var uPos);

            Subtract(c, a, out var ac);
            Multiply(ac, v, out var acV);
            Add(uPos, acV, out result);
        }

        [Pure]
        public static Vector4 TransformRow(Vector4 vec, Matrix4 mat) {
            TransformRow(vec, mat, out Vector4 result);
            return result;
        }

        public static void TransformRow(Vector4 vec, Matrix4 mat, out Vector4 result) => result = new Vector4(
                (vec.X * mat.Row0.X) + (vec.Y * mat.Row1.X) + (vec.Z * mat.Row2.X) + (vec.W * mat.Row3.X),
                (vec.X * mat.Row0.Y) + (vec.Y * mat.Row1.Y) + (vec.Z * mat.Row2.Y) + (vec.W * mat.Row3.Y),
                (vec.X * mat.Row0.Z) + (vec.Y * mat.Row1.Z) + (vec.Z * mat.Row2.Z) + (vec.W * mat.Row3.Z),
                (vec.X * mat.Row0.W) + (vec.Y * mat.Row1.W) + (vec.Z * mat.Row2.W) + (vec.W * mat.Row3.W));

        [Pure]
        public static Vector4 Transform(Vector4 vec, Quaternion quat) {
            Transform(vec, quat, out Vector4 result);
            return result;
        }

        public static void Transform(Vector4 vec, Quaternion quat, out Vector4 result) {
            Quaternion v = new Quaternion(vec.X, vec.Y, vec.Z, vec.W);
            Quaternion.Invert(quat, out Quaternion i);
            Quaternion.Multiply(quat, v, out Quaternion t);
            Quaternion.Multiply(t, i, out v);

            result.X = v.X;
            result.Y = v.Y;
            result.Z = v.Z;
            result.W = v.W;
        }

        [Pure]
        public static Vector4 TransformColumn(Matrix4 mat, Vector4 vec) {
            TransformColumn(mat, vec, out Vector4 result);
            return result;
        }

        public static void TransformColumn(Matrix4 mat, Vector4 vec, out Vector4 result) => result = new Vector4(
                (mat.Row0.X * vec.X) + (mat.Row0.Y * vec.Y) + (mat.Row0.Z * vec.Z) + (mat.Row0.W * vec.W),
                (mat.Row1.X * vec.X) + (mat.Row1.Y * vec.Y) + (mat.Row1.Z * vec.Z) + (mat.Row1.W * vec.W),
                (mat.Row2.X * vec.X) + (mat.Row2.Y * vec.Y) + (mat.Row2.Z * vec.Z) + (mat.Row2.W * vec.W),
                (mat.Row3.X * vec.X) + (mat.Row3.Y * vec.Y) + (mat.Row3.Z * vec.Z) + (mat.Row3.W * vec.W));

        [XmlIgnore]
        public Vector2 Xy {
            get => Unsafe.As<Vector4, Vector2>(ref this);
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
        public Vector2 Xw {
            get => new Vector2(X, W);
            set {
                X = value.X;
                W = value.Y;
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
        public Vector2 Yw {
            get => new Vector2(Y, W);
            set {
                Y = value.X;
                W = value.Y;
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
        public Vector2 Zw {
            get => new Vector2(Z, W);
            set {
                Z = value.X;
                W = value.Y;
            }
        }

        [XmlIgnore]
        public Vector2 Wx {
            get => new Vector2(W, X);
            set {
                W = value.X;
                X = value.Y;
            }
        }

        [XmlIgnore]
        public Vector2 Wy {
            get => new Vector2(W, Y);
            set {
                W = value.X;
                Y = value.Y;
            }
        }

        [XmlIgnore]
        public Vector2 Wz {
            get => new Vector2(W, Z);
            set {
                W = value.X;
                Z = value.Y;
            }
        }

        [XmlIgnore]
        public Vector3 Xyz {
            get => Unsafe.As<Vector4, Vector3>(ref this);
            set {
                X = value.X;
                Y = value.Y;
                Z = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Xyw {
            get => new Vector3(X, Y, W);
            set {
                X = value.X;
                Y = value.Y;
                W = value.Z;
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
        public Vector3 Xzw {
            get => new Vector3(X, Z, W);
            set {
                X = value.X;
                Z = value.Y;
                W = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Xwy {
            get => new Vector3(X, W, Y);
            set {
                X = value.X;
                W = value.Y;
                Y = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Xwz {
            get => new Vector3(X, W, Z);
            set {
                X = value.X;
                W = value.Y;
                Z = value.Z;
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
        public Vector3 Yxw {
            get => new Vector3(Y, X, W);
            set {
                Y = value.X;
                X = value.Y;
                W = value.Z;
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
        public Vector3 Yzw {
            get => new Vector3(Y, Z, W);
            set {
                Y = value.X;
                Z = value.Y;
                W = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Ywx {
            get => new Vector3(Y, W, X);
            set {
                Y = value.X;
                W = value.Y;
                X = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Ywz {
            get => new Vector3(Y, W, Z);
            set {
                Y = value.X;
                W = value.Y;
                Z = value.Z;
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
        public Vector3 Zxw {
            get => new Vector3(Z, X, W);
            set {
                Z = value.X;
                X = value.Y;
                W = value.Z;
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

        [XmlIgnore]
        public Vector3 Zyw {
            get => new Vector3(Z, Y, W);
            set {
                Z = value.X;
                Y = value.Y;
                W = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Zwx {
            get => new Vector3(Z, W, X);
            set {
                Z = value.X;
                W = value.Y;
                X = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Zwy {
            get => new Vector3(Z, W, Y);
            set {
                Z = value.X;
                W = value.Y;
                Y = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Wxy {
            get => new Vector3(W, X, Y);
            set {
                W = value.X;
                X = value.Y;
                Y = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Wxz {
            get => new Vector3(W, X, Z);
            set {
                W = value.X;
                X = value.Y;
                Z = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Wyx {
            get => new Vector3(W, Y, X);
            set {
                W = value.X;
                Y = value.Y;
                X = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Wyz {
            get => new Vector3(W, Y, Z);
            set {
                W = value.X;
                Y = value.Y;
                Z = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Wzx {
            get => new Vector3(W, Z, X);
            set {
                W = value.X;
                Z = value.Y;
                X = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3 Wzy {
            get => new Vector3(W, Z, Y);
            set {
                W = value.X;
                Z = value.Y;
                Y = value.Z;
            }
        }

        [XmlIgnore]
        public Vector4 Xywz {
            get => new Vector4(X, Y, W, Z);
            set {
                X = value.X;
                Y = value.Y;
                W = value.Z;
                Z = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Xzyw {
            get => new Vector4(X, Z, Y, W);
            set {
                X = value.X;
                Z = value.Y;
                Y = value.Z;
                W = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Xzwy {
            get => new Vector4(X, Z, W, Y);
            set {
                X = value.X;
                Z = value.Y;
                W = value.Z;
                Y = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Xwyz {
            get => new Vector4(X, W, Y, Z);
            set {
                X = value.X;
                W = value.Y;
                Y = value.Z;
                Z = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Xwzy {
            get => new Vector4(X, W, Z, Y);
            set {
                X = value.X;
                W = value.Y;
                Z = value.Z;
                Y = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Yxzw {
            get => new Vector4(Y, X, Z, W);
            set {
                Y = value.X;
                X = value.Y;
                Z = value.Z;
                W = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Yxwz {
            get => new Vector4(Y, X, W, Z);
            set {
                Y = value.X;
                X = value.Y;
                W = value.Z;
                Z = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Yyzw {
            get => new Vector4(Y, Y, Z, W);
            set {
                X = value.X;
                Y = value.Y;
                Z = value.Z;
                W = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Yywz {
            get => new Vector4(Y, Y, W, Z);
            set {
                X = value.X;
                Y = value.Y;
                W = value.Z;
                Z = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Yzxw {
            get => new Vector4(Y, Z, X, W);
            set {
                Y = value.X;
                Z = value.Y;
                X = value.Z;
                W = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Yzwx {
            get => new Vector4(Y, Z, W, X);
            set {
                Y = value.X;
                Z = value.Y;
                W = value.Z;
                X = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Ywxz {
            get => new Vector4(Y, W, X, Z);
            set {
                Y = value.X;
                W = value.Y;
                X = value.Z;
                Z = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Ywzx {
            get => new Vector4(Y, W, Z, X);
            set {
                Y = value.X;
                W = value.Y;
                Z = value.Z;
                X = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Zxyw {
            get => new Vector4(Z, X, Y, W);
            set {
                Z = value.X;
                X = value.Y;
                Y = value.Z;
                W = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Zxwy {
            get => new Vector4(Z, X, W, Y);
            set {
                Z = value.X;
                X = value.Y;
                W = value.Z;
                Y = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Zyxw {
            get => new Vector4(Z, Y, X, W);
            set {
                Z = value.X;
                Y = value.Y;
                X = value.Z;
                W = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Zywx {
            get => new Vector4(Z, Y, W, X);
            set {
                Z = value.X;
                Y = value.Y;
                W = value.Z;
                X = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Zwxy {
            get => new Vector4(Z, W, X, Y);
            set {
                Z = value.X;
                W = value.Y;
                X = value.Z;
                Y = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Zwyx {
            get => new Vector4(Z, W, Y, X);
            set {
                Z = value.X;
                W = value.Y;
                Y = value.Z;
                X = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Zwzy {
            get => new Vector4(Z, W, Z, Y);
            set {
                X = value.X;
                W = value.Y;
                Z = value.Z;
                Y = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Wxyz {
            get => new Vector4(W, X, Y, Z);
            set {
                W = value.X;
                X = value.Y;
                Y = value.Z;
                Z = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Wxzy {
            get => new Vector4(W, X, Z, Y);
            set {
                W = value.X;
                X = value.Y;
                Z = value.Z;
                Y = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Wyxz {
            get => new Vector4(W, Y, X, Z);
            set {
                W = value.X;
                Y = value.Y;
                X = value.Z;
                Z = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Wyzx {
            get => new Vector4(W, Y, Z, X);
            set {
                W = value.X;
                Y = value.Y;
                Z = value.Z;
                X = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Wzxy {
            get => new Vector4(W, Z, X, Y);
            set {
                W = value.X;
                Z = value.Y;
                X = value.Z;
                Y = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Wzyx {
            get => new Vector4(W, Z, Y, X);
            set {
                W = value.X;
                Z = value.Y;
                Y = value.Z;
                X = value.W;
            }
        }

        [XmlIgnore]
        public Vector4 Wzyw {
            get => new Vector4(W, Z, Y, W);
            set {
                X = value.X;
                Z = value.Y;
                Y = value.Z;
                W = value.W;
            }
        }

        #region ‰‰ŽZŽq

        [Pure]
        public static Vector4 operator +(Vector4 left, Vector4 right) {
            left.X += right.X;
            left.Y += right.Y;
            left.Z += right.Z;
            left.W += right.W;
            return left;
        }

        [Pure]
        public static Vector4 operator -(Vector4 left, Vector4 right) {
            left.X -= right.X;
            left.Y -= right.Y;
            left.Z -= right.Z;
            left.W -= right.W;
            return left;
        }

        [Pure]
        public static Vector4 operator -(Vector4 vec) {
            vec.X = -vec.X;
            vec.Y = -vec.Y;
            vec.Z = -vec.Z;
            vec.W = -vec.W;
            return vec;
        }

        [Pure]
        public static Vector4 operator *(Vector4 vec, float scale) {
            vec.X *= scale;
            vec.Y *= scale;
            vec.Z *= scale;
            vec.W *= scale;
            return vec;
        }

        [Pure]
        public static Vector4 operator *(float scale, Vector4 vec) {
            vec.X *= scale;
            vec.Y *= scale;
            vec.Z *= scale;
            vec.W *= scale;
            return vec;
        }

        [Pure]
        public static Vector4 operator *(Vector4 vec, Vector4 scale) {
            vec.X *= scale.X;
            vec.Y *= scale.Y;
            vec.Z *= scale.Z;
            vec.W *= scale.W;
            return vec;
        }

        [Pure]
        public static Vector4 operator *(Vector4 vec, Matrix4 mat) {
            TransformRow(vec, mat, out Vector4 result);
            return result;
        }

        [Pure]
        public static Vector4 operator *(Matrix4 mat, Vector4 vec) {
            TransformColumn(mat, vec, out Vector4 result);
            return result;
        }

        [Pure]
        public static Vector4 operator *(Quaternion quat, Vector4 vec) {
            Transform(vec, quat, out Vector4 result);
            return result;
        }

        [Pure]
        public static Vector4 operator /(Vector4 vec, float scale) {
            vec.X /= scale;
            vec.Y /= scale;
            vec.Z /= scale;
            vec.W /= scale;
            return vec;
        }

        public static bool operator ==(Vector4 left, Vector4 right) => left.Equals(right);

        public static bool operator !=(Vector4 left, Vector4 right) => !left.Equals(right);

        [Pure]
        public static unsafe explicit operator float*(Vector4 v) => &v.X;

        [Pure]
        public static explicit operator IntPtr(Vector4 v) {
            unsafe {
                return (IntPtr)(&v.X);
            }
        }


        [Pure]
        public static implicit operator Vector4((float X, float Y, float Z, float W) values) => new Vector4(values.X, values.Y, values.Z, values.W);

        #endregion

        /// <inheritdoc />
        public override string ToString() => $"(X:{X} Y:{Y} Z:{Z} W:{W})";

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Vector4 && Equals((Vector4)obj);

        /// <inheritdoc />
        public bool Equals(Vector4 other) =>
            X == other.X &&
            Y == other.Y &&
            Z == other.Z &&
            W == other.W;

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

        [Pure]
        public void Deconstruct(out float x, out float y, out float z, out float w) {
            x = X;
            y = Y;
            z = Z;
            w = W;
        }
    }
}

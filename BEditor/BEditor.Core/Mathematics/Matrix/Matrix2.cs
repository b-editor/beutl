using System;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;

namespace BEditor.Mathematics {
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Matrix2 : IEquatable<Matrix2> {
        public Vector2 Row0;

        public Vector2 Row1;

        public static readonly Matrix2 Identity = new Matrix2(Vector2.UnitX, Vector2.UnitY);

        public static readonly Matrix2 Zero = new Matrix2(Vector2.Zero, Vector2.Zero);

        public Matrix2(Vector2 row0, Vector2 row1) {
            Row0 = row0;
            Row1 = row1;
        }

        public Matrix2(
            float m00, float m01,
            float m10, float m11) {
            Row0 = new Vector2(m00, m01);
            Row1 = new Vector2(m10, m11);
        }

        public float Determinant {
            get {
                float m11 = Row0.X;
                float m12 = Row0.Y;
                float m21 = Row1.X;
                float m22 = Row1.Y;

                return (m11 * m22) - (m12 * m21);
            }
        }

        public Vector2 Column0 {
            get => new Vector2(Row0.X, Row1.X);
            set {
                Row0.X = value.X;
                Row1.X = value.Y;
            }
        }

        public Vector2 Column1 {
            get => new Vector2(Row0.Y, Row1.Y);
            set {
                Row0.Y = value.X;
                Row1.Y = value.Y;
            }
        }

        public float M11 { get => Row0.X; set => Row0.X = value; }

        public float M12 { get => Row0.Y; set => Row0.Y = value; }

        public float M21 { get => Row1.X; set => Row1.X = value; }

        public float M22 { get => Row1.Y; set => Row1.Y = value; }

        public Vector2 Diagonal {
            get => new Vector2(Row0.X, Row1.Y);
            set {
                Row0.X = value.X;
                Row1.Y = value.Y;
            }
        }

        public float Trace => Row0.X + Row1.Y;

        public float this[int rowIndex, int columnIndex] {
            get {
                if (rowIndex == 0) {
                    return Row0[columnIndex];
                }

                if (rowIndex == 1) {
                    return Row1[columnIndex];
                }

                throw new IndexOutOfRangeException("You tried to access this matrix at: (" + rowIndex + ", " +
                                                   columnIndex + ")");
            }

            set {
                if (rowIndex == 0) {
                    Row0[columnIndex] = value;
                }
                else if (rowIndex == 1) {
                    Row1[columnIndex] = value;
                }
                else {
                    throw new IndexOutOfRangeException("You tried to set this matrix at: (" + rowIndex + ", " +
                                                       columnIndex + ")");
                }
            }
        }

        public void Transpose() => this = Transpose(this);

        public void Invert() => this = Invert(this);

        public static void CreateRotation(float angle, out Matrix2 result) {
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);

            result.Row0.X = cos;
            result.Row0.Y = sin;
            result.Row1.X = -sin;
            result.Row1.Y = cos;
        }

        [Pure]
        public static Matrix2 CreateRotation(float angle) {
            CreateRotation(angle, out Matrix2 result);
            return result;
        }

        public static void CreateScale(float scale, out Matrix2 result) {
            result.Row0.X = scale;
            result.Row0.Y = 0;
            result.Row1.X = 0;
            result.Row1.Y = scale;
        }

        [Pure]
        public static Matrix2 CreateScale(float scale) {
            CreateScale(scale, out Matrix2 result);
            return result;
        }

        public static void CreateScale(Vector2 scale, out Matrix2 result) {
            result.Row0.X = scale.X;
            result.Row0.Y = 0;
            result.Row1.X = 0;
            result.Row1.Y = scale.Y;
        }

        [Pure]
        public static Matrix2 CreateScale(Vector2 scale) {
            CreateScale(scale, out Matrix2 result);
            return result;
        }

        public static void CreateScale(float x, float y, out Matrix2 result) {
            result.Row0.X = x;
            result.Row0.Y = 0;
            result.Row1.X = 0;
            result.Row1.Y = y;
        }

        [Pure]
        public static Matrix2 CreateScale(float x, float y) {
            CreateScale(x, y, out Matrix2 result);
            return result;
        }

        public static void Mult(Matrix2 left, float right, out Matrix2 result) {
            result.Row0.X = left.Row0.X * right;
            result.Row0.Y = left.Row0.Y * right;
            result.Row1.X = left.Row1.X * right;
            result.Row1.Y = left.Row1.Y * right;
        }

        [Pure]
        public static Matrix2 Mult(Matrix2 left, float right) {
            Mult(left, right, out Matrix2 result);
            return result;
        }

        public static void Mult(Matrix2 left, Matrix2 right, out Matrix2 result) {
            float leftM11 = left.Row0.X;
            float leftM12 = left.Row0.Y;
            float leftM21 = left.Row1.X;
            float leftM22 = left.Row1.Y;
            float rightM11 = right.Row0.X;
            float rightM12 = right.Row0.Y;
            float rightM21 = right.Row1.X;
            float rightM22 = right.Row1.Y;

            result.Row0.X = (leftM11 * rightM11) + (leftM12 * rightM21);
            result.Row0.Y = (leftM11 * rightM12) + (leftM12 * rightM22);
            result.Row1.X = (leftM21 * rightM11) + (leftM22 * rightM21);
            result.Row1.Y = (leftM21 * rightM12) + (leftM22 * rightM22);
        }

        [Pure]
        public static Matrix2 Mult(Matrix2 left, Matrix2 right) {
            Mult(left, right, out Matrix2 result);
            return result;
        }

        public static void Add(Matrix2 left, Matrix2 right, out Matrix2 result) {
            result.Row0.X = left.Row0.X + right.Row0.X;
            result.Row0.Y = left.Row0.Y + right.Row0.Y;
            result.Row1.X = left.Row1.X + right.Row1.X;
            result.Row1.Y = left.Row1.Y + right.Row1.Y;
        }

        [Pure]
        public static Matrix2 Add(Matrix2 left, Matrix2 right) {
            Add(left, right, out Matrix2 result);
            return result;
        }

        public static void Subtract(Matrix2 left, Matrix2 right, out Matrix2 result) {
            result.Row0.X = left.Row0.X - right.Row0.X;
            result.Row0.Y = left.Row0.Y - right.Row0.Y;
            result.Row1.X = left.Row1.X - right.Row1.X;
            result.Row1.Y = left.Row1.Y - right.Row1.Y;
        }

        [Pure]
        public static Matrix2 Subtract(Matrix2 left, Matrix2 right) {
            Subtract(left, right, out Matrix2 result);
            return result;
        }

        public static void Invert(Matrix2 mat, out Matrix2 result) {
            var det = mat.Determinant;

            if (det == 0) {
                throw new InvalidOperationException("Matrix is singular and cannot be inverted.");
            }

            var invDet = 1f / det;

            result.Row0.X = mat.Row1.Y * invDet;
            result.Row0.Y = -mat.Row0.Y * invDet;
            result.Row1.X = -mat.Row1.X * invDet;
            result.Row1.Y = mat.Row0.X * invDet;
        }

        [Pure]
        public static Matrix2 Invert(Matrix2 mat) {
            Invert(mat, out Matrix2 result);
            return result;
        }

        public static void Transpose(Matrix2 mat, out Matrix2 result) {
            result.Row0.X = mat.Row0.X;
            result.Row0.Y = mat.Row1.X;
            result.Row1.X = mat.Row0.Y;
            result.Row1.Y = mat.Row1.Y;
        }

        [Pure]
        public static Matrix2 Transpose(Matrix2 mat) {
            Transpose(mat, out Matrix2 result);
            return result;
        }

        [Pure]
        public static Matrix2 operator *(float left, Matrix2 right) => Mult(right, left);

        [Pure]
        public static Matrix2 operator *(Matrix2 left, float right) => Mult(left, right);

        [Pure]
        public static Matrix2 operator *(Matrix2 left, Matrix2 right) => Mult(left, right);

        [Pure]
        public static Matrix2 operator +(Matrix2 left, Matrix2 right) => Add(left, right);

        [Pure]
        public static Matrix2 operator -(Matrix2 left, Matrix2 right) => Subtract(left, right);

        [Pure]
        public static bool operator ==(Matrix2 left, Matrix2 right) => left.Equals(right);

        [Pure]
        public static bool operator !=(Matrix2 left, Matrix2 right) => !left.Equals(right);

        public override string ToString() => $"({Row0.X} {Row0.Y})\n({Row1.X} {Row1.Y})";

        public override int GetHashCode() => HashCode.Combine(Row0, Row1);

        [Pure]
        public override bool Equals(object obj) => obj is Matrix2 matrix && Equals(matrix);

        [Pure]
        public bool Equals(Matrix2 other) => Row0 == other.Row0 && Row1 == other.Row1;
    }
}

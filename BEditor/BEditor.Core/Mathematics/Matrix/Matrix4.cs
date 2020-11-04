using System;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;

namespace BEditor.Mathematics {
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Matrix4 : IEquatable<Matrix4> {
        public Vector4 Row0;

        public Vector4 Row1;

        public Vector4 Row2;

        public Vector4 Row3;

        public static readonly Matrix4 Identity = new Matrix4(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW);
        public static readonly Matrix4 Zero = new Matrix4(Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero);

        public Matrix4(Vector4 row0, Vector4 row1, Vector4 row2, Vector4 row3) {
            Row0 = row0;
            Row1 = row1;
            Row2 = row2;
            Row3 = row3;
        }

        public Matrix4(
            float m00, float m01, float m02, float m03,
            float m10, float m11, float m12, float m13,
            float m20, float m21, float m22, float m23,
            float m30, float m31, float m32, float m33) {
            Row0 = new Vector4(m00, m01, m02, m03);
            Row1 = new Vector4(m10, m11, m12, m13);
            Row2 = new Vector4(m20, m21, m22, m23);
            Row3 = new Vector4(m30, m31, m32, m33);
        }

        public Matrix4(Matrix3 topLeft) {
            Row0.X = topLeft.Row0.X;
            Row0.Y = topLeft.Row0.Y;
            Row0.Z = topLeft.Row0.Z;
            Row0.W = 0;
            Row1.X = topLeft.Row1.X;
            Row1.Y = topLeft.Row1.Y;
            Row1.Z = topLeft.Row1.Z;
            Row1.W = 0;
            Row2.X = topLeft.Row2.X;
            Row2.Y = topLeft.Row2.Y;
            Row2.Z = topLeft.Row2.Z;
            Row2.W = 0;
            Row3.X = 0;
            Row3.Y = 0;
            Row3.Z = 0;
            Row3.W = 1;
        }

        public float Determinant {
            get {
                float m11 = Row0.X;
                float m12 = Row0.Y;
                float m13 = Row0.Z;
                float m14 = Row0.W;
                float m21 = Row1.X;
                float m22 = Row1.Y;
                float m23 = Row1.Z;
                float m24 = Row1.W;
                float m31 = Row2.X;
                float m32 = Row2.Y;
                float m33 = Row2.Z;
                float m34 = Row2.W;
                float m41 = Row3.X;
                float m42 = Row3.Y;
                float m43 = Row3.Z;
                float m44 = Row3.W;

                return
                    (m11 * m22 * m33 * m44) - (m11 * m22 * m34 * m43) + (m11 * m23 * m34 * m42) - (m11 * m23 * m32 * m44)
                    + (m11 * m24 * m32 * m43) - (m11 * m24 * m33 * m42) - (m12 * m23 * m34 * m41) + (m12 * m23 * m31 * m44)
                    - (m12 * m24 * m31 * m43) + (m12 * m24 * m33 * m41) - (m12 * m21 * m33 * m44) + (m12 * m21 * m34 * m43)
                                                                                            + (m13 * m24 * m31 * m42) -
                    (m13 * m24 * m32 * m41) + (m13 * m21 * m32 * m44) - (m13 * m21 * m34 * m42)
                    + (m13 * m22 * m34 * m41) - (m13 * m22 * m31 * m44) - (m14 * m21 * m32 * m43) + (m14 * m21 * m33 * m42)
                    - (m14 * m22 * m33 * m41) + (m14 * m22 * m31 * m43) - (m14 * m23 * m31 * m42) + (m14 * m23 * m32 * m41);
            }
        }

        public Vector4 Column0 {
            get => new Vector4(Row0.X, Row1.X, Row2.X, Row3.X);
            set {
                Row0.X = value.X;
                Row1.X = value.Y;
                Row2.X = value.Z;
                Row3.X = value.W;
            }
        }

        public Vector4 Column1 {
            get => new Vector4(Row0.Y, Row1.Y, Row2.Y, Row3.Y);
            set {
                Row0.Y = value.X;
                Row1.Y = value.Y;
                Row2.Y = value.Z;
                Row3.Y = value.W;
            }
        }

        public Vector4 Column2 {
            get => new Vector4(Row0.Z, Row1.Z, Row2.Z, Row3.Z);
            set {
                Row0.Z = value.X;
                Row1.Z = value.Y;
                Row2.Z = value.Z;
                Row3.Z = value.W;
            }
        }

        public Vector4 Column3 {
            get => new Vector4(Row0.W, Row1.W, Row2.W, Row3.W);
            set {
                Row0.W = value.X;
                Row1.W = value.Y;
                Row2.W = value.Z;
                Row3.W = value.W;
            }
        }

        public float M11 { get => Row0.X; set => Row0.X = value; }

        public float M12 { get => Row0.Y; set => Row0.Y = value; }

        public float M13 { get => Row0.Z; set => Row0.Z = value; }

        public float M14 { get => Row0.W; set => Row0.W = value; }


        public float M21 { get => Row1.X; set => Row1.X = value; }

        public float M22 { get => Row1.Y; set => Row1.Y = value; }

        public float M23 { get => Row1.Z; set => Row1.Z = value; }

        public float M24 { get => Row1.W; set => Row1.W = value; }


        public float M31 { get => Row2.X; set => Row2.X = value; }

        public float M32 { get => Row2.Y; set => Row2.Y = value; }

        public float M33 { get => Row2.Z; set => Row2.Z = value; }

        public float M34 { get => Row2.W; set => Row2.W = value; }


        public float M41 { get => Row3.X; set => Row3.X = value; }

        public float M42 { get => Row3.Y; set => Row3.Y = value; }

        public float M43 { get => Row3.Z; set => Row3.Z = value; }

        public float M44 { get => Row3.W; set => Row3.W = value; }


        public Vector4 Diagonal {
            get => new Vector4(Row0.X, Row1.Y, Row2.Z, Row3.W);
            set {
                Row0.X = value.X;
                Row1.Y = value.Y;
                Row2.Z = value.Z;
                Row3.W = value.W;
            }
        }

        public float Trace => Row0.X + Row1.Y + Row2.Z + Row3.W;

        public float this[int rowIndex, int columnIndex] {
            get {
                if (rowIndex == 0) {
                    return Row0[columnIndex];
                }

                if (rowIndex == 1) {
                    return Row1[columnIndex];
                }

                if (rowIndex == 2) {
                    return Row2[columnIndex];
                }

                if (rowIndex == 3) {
                    return Row3[columnIndex];
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
                else if (rowIndex == 2) {
                    Row2[columnIndex] = value;
                }
                else if (rowIndex == 3) {
                    Row3[columnIndex] = value;
                }
                else {
                    throw new IndexOutOfRangeException("You tried to set this matrix at: (" + rowIndex + ", " +
                                                       columnIndex + ")");
                }
            }
        }

        public void Invert() => this = Invert(this);

        public void Transpose() => this = Transpose(this);

        public Matrix4 Normalized() {
            var m = this;
            m.Normalize();
            return m;
        }

        public void Normalize() {
            var determinant = Determinant;
            Row0 /= determinant;
            Row1 /= determinant;
            Row2 /= determinant;
            Row3 /= determinant;
        }

        public Matrix4 Inverted() {
            var m = this;
            if (m.Determinant != 0) {
                m.Invert();
            }

            return m;
        }

        public Matrix4 ClearTranslation() {
            var m = this;
            m.Row3.Xyz = Vector3.Zero;
            return m;
        }

        public Matrix4 ClearScale() {
            var m = this;
            m.Row0.Xyz = m.Row0.Xyz.Normalized();
            m.Row1.Xyz = m.Row1.Xyz.Normalized();
            m.Row2.Xyz = m.Row2.Xyz.Normalized();
            return m;
        }

        public Matrix4 ClearRotation() {
            var m = this;
            m.Row0.Xyz = new Vector3(m.Row0.Xyz.Length, 0, 0);
            m.Row1.Xyz = new Vector3(0, m.Row1.Xyz.Length, 0);
            m.Row2.Xyz = new Vector3(0, 0, m.Row2.Xyz.Length);
            return m;
        }

        public Matrix4 ClearProjection() {
            var m = this;
            m.Column3 = Vector4.Zero;
            return m;
        }

        public Vector3 ExtractTranslation() => Row3.Xyz;

        public Vector3 ExtractScale() => new Vector3(Row0.Xyz.Length, Row1.Xyz.Length, Row2.Xyz.Length);

        [Pure]
        public Quaternion ExtractRotation(bool rowNormalize = true) {
            var row0 = Row0.Xyz;
            var row1 = Row1.Xyz;
            var row2 = Row2.Xyz;

            if (rowNormalize) {
                row0 = row0.Normalized();
                row1 = row1.Normalized();
                row2 = row2.Normalized();
            }

            // code below adapted from Blender
            var q = default(Quaternion);
            var trace = 0.25 * (row0[0] + row1[1] + row2[2] + 1.0);

            if (trace > 0) {
                var sq = Math.Sqrt(trace);

                q.W = (float)sq;
                sq = 1.0 / (4.0 * sq);
                q.X = (float)((row1[2] - row2[1]) * sq);
                q.Y = (float)((row2[0] - row0[2]) * sq);
                q.Z = (float)((row0[1] - row1[0]) * sq);
            }
            else if (row0[0] > row1[1] && row0[0] > row2[2]) {
                var sq = 2.0 * Math.Sqrt(1.0 + row0[0] - row1[1] - row2[2]);

                q.X = (float)(0.25 * sq);
                sq = 1.0 / sq;
                q.W = (float)((row2[1] - row1[2]) * sq);
                q.Y = (float)((row1[0] + row0[1]) * sq);
                q.Z = (float)((row2[0] + row0[2]) * sq);
            }
            else if (row1[1] > row2[2]) {
                var sq = 2.0 * Math.Sqrt(1.0 + row1[1] - row0[0] - row2[2]);

                q.Y = (float)(0.25 * sq);
                sq = 1.0 / sq;
                q.W = (float)((row2[0] - row0[2]) * sq);
                q.X = (float)((row1[0] + row0[1]) * sq);
                q.Z = (float)((row2[1] + row1[2]) * sq);
            }
            else {
                var sq = 2.0 * Math.Sqrt(1.0 + row2[2] - row0[0] - row1[1]);

                q.Z = (float)(0.25 * sq);
                sq = 1.0 / sq;
                q.W = (float)((row1[0] - row0[1]) * sq);
                q.X = (float)((row2[0] + row0[2]) * sq);
                q.Y = (float)((row2[1] + row1[2]) * sq);
            }

            q.Normalize();
            return q;
        }

        public Vector4 ExtractProjection() => Column3;

        public static void CreateFromAxisAngle(Vector3 axis, float angle, out Matrix4 result) {
            // normalize and create a local copy of the vector.
            axis.Normalize();
            float axisX = axis.X, axisY = axis.Y, axisZ = axis.Z;

            // calculate angles
            var cos = MathF.Cos(-angle);
            var s = MathF.Sin(-angle);
            var t = 1.0f - cos;

            // do the conversion math once
            float tXX = t * axisX * axisX;
            float tXY = t * axisX * axisY;
            float tXZ = t * axisX * axisZ;
            float tYY = t * axisY * axisY;
            float tYZ = t * axisY * axisZ;
            float tZZ = t * axisZ * axisZ;

            float sinX = s * axisX;
            float sinY = s * axisY;
            float sinZ = s * axisZ;

            result.Row0.X = tXX + cos;
            result.Row0.Y = tXY - sinZ;
            result.Row0.Z = tXZ + sinY;
            result.Row0.W = 0;
            result.Row1.X = tXY + sinZ;
            result.Row1.Y = tYY + cos;
            result.Row1.Z = tYZ - sinX;
            result.Row1.W = 0;
            result.Row2.X = tXZ - sinY;
            result.Row2.Y = tYZ + sinX;
            result.Row2.Z = tZZ + cos;
            result.Row2.W = 0;
            result.Row3 = Vector4.UnitW;
        }

        [Pure]
        public static Matrix4 CreateFromAxisAngle(Vector3 axis, float angle) {
            CreateFromAxisAngle(axis, angle, out Matrix4 result);
            return result;
        }

        public static void CreateFromQuaternion(Quaternion q, out Matrix4 result) {
            q.ToAxisAngle(out Vector3 axis, out float angle);
            CreateFromAxisAngle(axis, angle, out result);
        }

        [Pure]
        public static Matrix4 CreateFromQuaternion(Quaternion q) {
            CreateFromQuaternion(q, out Matrix4 result);
            return result;
        }

        public static void CreateRotationX(float angle, out Matrix4 result) {
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);

            result = Identity;
            result.Row1.Y = cos;
            result.Row1.Z = sin;
            result.Row2.Y = -sin;
            result.Row2.Z = cos;
        }

        [Pure]
        public static Matrix4 CreateRotationX(float angle) {
            CreateRotationX(angle, out Matrix4 result);
            return result;
        }

        public static void CreateRotationY(float angle, out Matrix4 result) {
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);

            result = Identity;
            result.Row0.X = cos;
            result.Row0.Z = -sin;
            result.Row2.X = sin;
            result.Row2.Z = cos;
        }

        [Pure]
        public static Matrix4 CreateRotationY(float angle) {
            CreateRotationY(angle, out Matrix4 result);
            return result;
        }

        public static void CreateRotationZ(float angle, out Matrix4 result) {
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);

            result = Identity;
            result.Row0.X = cos;
            result.Row0.Y = sin;
            result.Row1.X = -sin;
            result.Row1.Y = cos;
        }

        [Pure]
        public static Matrix4 CreateRotationZ(float angle) {
            CreateRotationZ(angle, out Matrix4 result);
            return result;
        }

        public static void CreateTranslation(float x, float y, float z, out Matrix4 result) {
            result = Identity;
            result.Row3.X = x;
            result.Row3.Y = y;
            result.Row3.Z = z;
        }

        public static void CreateTranslation(Vector3 vector, out Matrix4 result) {
            result = Identity;
            result.Row3.X = vector.X;
            result.Row3.Y = vector.Y;
            result.Row3.Z = vector.Z;
        }

        [Pure]
        public static Matrix4 CreateTranslation(float x, float y, float z) {
            CreateTranslation(x, y, z, out Matrix4 result);
            return result;
        }

        [Pure]
        public static Matrix4 CreateTranslation(Vector3 vector) {
            CreateTranslation(vector.X, vector.Y, vector.Z, out Matrix4 result);
            return result;
        }

        [Pure]
        public static Matrix4 CreateScale(float scale) {
            CreateScale(scale, out Matrix4 result);
            return result;
        }

        [Pure]
        public static Matrix4 CreateScale(Vector3 scale) {
            CreateScale(scale, out Matrix4 result);
            return result;
        }

        [Pure]
        public static Matrix4 CreateScale(float x, float y, float z) {
            CreateScale(x, y, z, out Matrix4 result);
            return result;
        }

        public static void CreateScale(float scale, out Matrix4 result) {
            result = Identity;
            result.Row0.X = scale;
            result.Row1.Y = scale;
            result.Row2.Z = scale;
        }

        public static void CreateScale(Vector3 scale, out Matrix4 result) {
            result = Identity;
            result.Row0.X = scale.X;
            result.Row1.Y = scale.Y;
            result.Row2.Z = scale.Z;
        }

        public static void CreateScale(float x, float y, float z, out Matrix4 result) {
            result = Identity;
            result.Row0.X = x;
            result.Row1.Y = y;
            result.Row2.Z = z;
        }

        public static void CreateOrthographic(float width, float height, float depthNear, float depthFar, out Matrix4 result) =>
            CreateOrthographicOffCenter(-width / 2, width / 2, -height / 2, height / 2, depthNear, depthFar, out result);

        [Pure]
        public static Matrix4 CreateOrthographic(float width, float height, float depthNear, float depthFar) {
            CreateOrthographicOffCenter(-width / 2, width / 2, -height / 2, height / 2, depthNear, depthFar, out Matrix4 result);
            return result;
        }

        public static void CreateOrthographicOffCenter(float left, float right, float bottom, float top, float depthNear, float depthFar, out Matrix4 result) {
            result = Identity;

            var invRL = 1.0f / (right - left);
            var invTB = 1.0f / (top - bottom);
            var invFN = 1.0f / (depthFar - depthNear);

            result.Row0.X = 2 * invRL;
            result.Row1.Y = 2 * invTB;
            result.Row2.Z = -2 * invFN;

            result.Row3.X = -(right + left) * invRL;
            result.Row3.Y = -(top + bottom) * invTB;
            result.Row3.Z = -(depthFar + depthNear) * invFN;
        }

        [Pure]
        public static Matrix4 CreateOrthographicOffCenter(float left, float right, float bottom, float top, float depthNear, float depthFar) {
            CreateOrthographicOffCenter(left, right, bottom, top, depthNear, depthFar, out Matrix4 result);
            return result;
        }

        public static void CreatePerspectiveFieldOfView(float fovy, float aspect, float depthNear, float depthFar, out Matrix4 result) {
            if (fovy <= 0 || fovy > Math.PI) throw new ArgumentOutOfRangeException(nameof(fovy));

            if (aspect <= 0) throw new ArgumentOutOfRangeException(nameof(aspect));

            if (depthNear <= 0) throw new ArgumentOutOfRangeException(nameof(depthNear));

            if (depthFar <= 0) throw new ArgumentOutOfRangeException(nameof(depthFar));


            var maxY = depthNear * MathF.Tan(0.5f * fovy);
            var minY = -maxY;
            var minX = minY * aspect;
            var maxX = maxY * aspect;

            CreatePerspectiveOffCenter(minX, maxX, minY, maxY, depthNear, depthFar, out result);
        }

        [Pure]
        public static Matrix4 CreatePerspectiveFieldOfView(float fovy, float aspect, float depthNear, float depthFar) {
            CreatePerspectiveFieldOfView(fovy, aspect, depthNear, depthFar, out Matrix4 result);
            return result;
        }

        public static void CreatePerspectiveOffCenter(float left, float right, float bottom, float top, float depthNear, float depthFar, out Matrix4 result) {
            if (depthNear <= 0) throw new ArgumentOutOfRangeException(nameof(depthNear));

            if (depthFar <= 0) throw new ArgumentOutOfRangeException(nameof(depthFar));

            if (depthNear >= depthFar) throw new ArgumentOutOfRangeException(nameof(depthNear));


            var x = 2.0f * depthNear / (right - left);
            var y = 2.0f * depthNear / (top - bottom);
            var a = (right + left) / (right - left);
            var b = (top + bottom) / (top - bottom);
            var c = -(depthFar + depthNear) / (depthFar - depthNear);
            var d = -(2.0f * depthFar * depthNear) / (depthFar - depthNear);

            result.Row0.X = x;
            result.Row0.Y = 0;
            result.Row0.Z = 0;
            result.Row0.W = 0;
            result.Row1.X = 0;
            result.Row1.Y = y;
            result.Row1.Z = 0;
            result.Row1.W = 0;
            result.Row2.X = a;
            result.Row2.Y = b;
            result.Row2.Z = c;
            result.Row2.W = -1;
            result.Row3.X = 0;
            result.Row3.Y = 0;
            result.Row3.Z = d;
            result.Row3.W = 0;
        }

        [Pure]
        public static Matrix4 CreatePerspectiveOffCenter(float left, float right, float bottom, float top, float depthNear, float depthFar) {
            CreatePerspectiveOffCenter(left, right, bottom, top, depthNear, depthFar, out Matrix4 result);
            return result;
        }

        [Pure]
        public static Matrix4 LookAt(Vector3 eye, Vector3 target, Vector3 up) {
            var z = Vector3.Normalize(eye - target);
            var x = Vector3.Normalize(Vector3.Cross(up, z));
            var y = Vector3.Normalize(Vector3.Cross(z, x));

            Matrix4 result;

            result.Row0.X = x.X;
            result.Row0.Y = y.X;
            result.Row0.Z = z.X;
            result.Row0.W = 0;
            result.Row1.X = x.Y;
            result.Row1.Y = y.Y;
            result.Row1.Z = z.Y;
            result.Row1.W = 0;
            result.Row2.X = x.Z;
            result.Row2.Y = y.Z;
            result.Row2.Z = z.Z;
            result.Row2.W = 0;
            result.Row3.X = -((x.X * eye.X) + (x.Y * eye.Y) + (x.Z * eye.Z));
            result.Row3.Y = -((y.X * eye.X) + (y.Y * eye.Y) + (y.Z * eye.Z));
            result.Row3.Z = -((z.X * eye.X) + (z.Y * eye.Y) + (z.Z * eye.Z));
            result.Row3.W = 1;

            return result;
        }

        [Pure]
        public static Matrix4 LookAt(float eyeX, float eyeY, float eyeZ, float targetX, float targetY, float targetZ, float upX, float upY, float upZ) =>
            LookAt(
                new Vector3(eyeX, eyeY, eyeZ),
                new Vector3(targetX, targetY, targetZ),
                new Vector3(upX, upY, upZ));

        [Pure]
        public static Matrix4 Add(Matrix4 left, Matrix4 right) {
            Add(left, right, out Matrix4 result);
            return result;
        }

        public static void Add(Matrix4 left, Matrix4 right, out Matrix4 result) {
            result.Row0 = left.Row0 + right.Row0;
            result.Row1 = left.Row1 + right.Row1;
            result.Row2 = left.Row2 + right.Row2;
            result.Row3 = left.Row3 + right.Row3;
        }

        [Pure]
        public static Matrix4 Subtract(Matrix4 left, Matrix4 right) {
            Subtract(left, right, out Matrix4 result);
            return result;
        }

        public static void Subtract(Matrix4 left, Matrix4 right, out Matrix4 result) {
            result.Row0 = left.Row0 - right.Row0;
            result.Row1 = left.Row1 - right.Row1;
            result.Row2 = left.Row2 - right.Row2;
            result.Row3 = left.Row3 - right.Row3;
        }

        [Pure]
        public static Matrix4 Mult(Matrix4 left, Matrix4 right) {
            Mult(left, right, out Matrix4 result);
            return result;
        }

        public static void Mult(Matrix4 left, Matrix4 right, out Matrix4 result) {
            float leftM11 = left.Row0.X;
            float leftM12 = left.Row0.Y;
            float leftM13 = left.Row0.Z;
            float leftM14 = left.Row0.W;
            float leftM21 = left.Row1.X;
            float leftM22 = left.Row1.Y;
            float leftM23 = left.Row1.Z;
            float leftM24 = left.Row1.W;
            float leftM31 = left.Row2.X;
            float leftM32 = left.Row2.Y;
            float leftM33 = left.Row2.Z;
            float leftM34 = left.Row2.W;
            float leftM41 = left.Row3.X;
            float leftM42 = left.Row3.Y;
            float leftM43 = left.Row3.Z;
            float leftM44 = left.Row3.W;
            float rightM11 = right.Row0.X;
            float rightM12 = right.Row0.Y;
            float rightM13 = right.Row0.Z;
            float rightM14 = right.Row0.W;
            float rightM21 = right.Row1.X;
            float rightM22 = right.Row1.Y;
            float rightM23 = right.Row1.Z;
            float rightM24 = right.Row1.W;
            float rightM31 = right.Row2.X;
            float rightM32 = right.Row2.Y;
            float rightM33 = right.Row2.Z;
            float rightM34 = right.Row2.W;
            float rightM41 = right.Row3.X;
            float rightM42 = right.Row3.Y;
            float rightM43 = right.Row3.Z;
            float rightM44 = right.Row3.W;

            result.Row0.X = (leftM11 * rightM11) + (leftM12 * rightM21) + (leftM13 * rightM31) + (leftM14 * rightM41);
            result.Row0.Y = (leftM11 * rightM12) + (leftM12 * rightM22) + (leftM13 * rightM32) + (leftM14 * rightM42);
            result.Row0.Z = (leftM11 * rightM13) + (leftM12 * rightM23) + (leftM13 * rightM33) + (leftM14 * rightM43);
            result.Row0.W = (leftM11 * rightM14) + (leftM12 * rightM24) + (leftM13 * rightM34) + (leftM14 * rightM44);
            result.Row1.X = (leftM21 * rightM11) + (leftM22 * rightM21) + (leftM23 * rightM31) + (leftM24 * rightM41);
            result.Row1.Y = (leftM21 * rightM12) + (leftM22 * rightM22) + (leftM23 * rightM32) + (leftM24 * rightM42);
            result.Row1.Z = (leftM21 * rightM13) + (leftM22 * rightM23) + (leftM23 * rightM33) + (leftM24 * rightM43);
            result.Row1.W = (leftM21 * rightM14) + (leftM22 * rightM24) + (leftM23 * rightM34) + (leftM24 * rightM44);
            result.Row2.X = (leftM31 * rightM11) + (leftM32 * rightM21) + (leftM33 * rightM31) + (leftM34 * rightM41);
            result.Row2.Y = (leftM31 * rightM12) + (leftM32 * rightM22) + (leftM33 * rightM32) + (leftM34 * rightM42);
            result.Row2.Z = (leftM31 * rightM13) + (leftM32 * rightM23) + (leftM33 * rightM33) + (leftM34 * rightM43);
            result.Row2.W = (leftM31 * rightM14) + (leftM32 * rightM24) + (leftM33 * rightM34) + (leftM34 * rightM44);
            result.Row3.X = (leftM41 * rightM11) + (leftM42 * rightM21) + (leftM43 * rightM31) + (leftM44 * rightM41);
            result.Row3.Y = (leftM41 * rightM12) + (leftM42 * rightM22) + (leftM43 * rightM32) + (leftM44 * rightM42);
            result.Row3.Z = (leftM41 * rightM13) + (leftM42 * rightM23) + (leftM43 * rightM33) + (leftM44 * rightM43);
            result.Row3.W = (leftM41 * rightM14) + (leftM42 * rightM24) + (leftM43 * rightM34) + (leftM44 * rightM44);
        }

        [Pure]
        public static Matrix4 Mult(Matrix4 left, float right) {
            Mult(left, right, out Matrix4 result);
            return result;
        }

        public static void Mult(Matrix4 left, float right, out Matrix4 result) {
            result.Row0 = left.Row0 * right;
            result.Row1 = left.Row1 * right;
            result.Row2 = left.Row2 * right;
            result.Row3 = left.Row3 * right;
        }

        public static void Invert(Matrix4 mat, out Matrix4 result) {
            int[] colIdx = { 0, 0, 0, 0 };
            int[] rowIdx = { 0, 0, 0, 0 };
            int[] pivotIdx = { -1, -1, -1, -1 };

            // convert the matrix to an array for easy looping
            float[,] inverse =
            {
                { mat.Row0.X, mat.Row0.Y, mat.Row0.Z, mat.Row0.W },
                { mat.Row1.X, mat.Row1.Y, mat.Row1.Z, mat.Row1.W },
                { mat.Row2.X, mat.Row2.Y, mat.Row2.Z, mat.Row2.W },
                { mat.Row3.X, mat.Row3.Y, mat.Row3.Z, mat.Row3.W }
            };
            var icol = 0;
            var irow = 0;
            for (var i = 0; i < 4; i++) {
                // Find the largest pivot value
                var maxPivot = 0.0f;
                for (var j = 0; j < 4; j++) {
                    if (pivotIdx[j] != 0) {
                        for (var k = 0; k < 4; ++k) {
                            if (pivotIdx[k] == -1) {
                                var absVal = Math.Abs(inverse[j, k]);
                                if (absVal > maxPivot) {
                                    maxPivot = absVal;
                                    irow = j;
                                    icol = k;
                                }
                            }
                            else if (pivotIdx[k] > 0) {
                                result = mat;
                                return;
                            }
                        }
                    }
                }

                ++pivotIdx[icol];

                // Swap rows over so pivot is on diagonal
                if (irow != icol) {
                    for (var k = 0; k < 4; ++k) {
                        var f = inverse[irow, k];
                        inverse[irow, k] = inverse[icol, k];
                        inverse[icol, k] = f;
                    }
                }

                rowIdx[i] = irow;
                colIdx[i] = icol;

                var pivot = inverse[icol, icol];

                // check for singular matrix
                if (pivot == 0.0f) {
                    throw new InvalidOperationException("Matrix is singular and cannot be inverted.");
                }

                // Scale row so it has a unit diagonal
                var oneOverPivot = 1.0f / pivot;
                inverse[icol, icol] = 1.0f;
                for (var k = 0; k < 4; ++k) {
                    inverse[icol, k] *= oneOverPivot;
                }

                // Do elimination of non-diagonal elements
                for (var j = 0; j < 4; ++j) {
                    // check this isn't on the diagonal
                    if (icol != j) {
                        var f = inverse[j, icol];
                        inverse[j, icol] = 0.0f;
                        for (var k = 0; k < 4; ++k) {
                            inverse[j, k] -= inverse[icol, k] * f;
                        }
                    }
                }
            }

            for (var j = 3; j >= 0; --j) {
                var ir = rowIdx[j];
                var ic = colIdx[j];
                for (var k = 0; k < 4; ++k) {
                    var f = inverse[k, ir];
                    inverse[k, ir] = inverse[k, ic];
                    inverse[k, ic] = f;
                }
            }

            result.Row0.X = inverse[0, 0];
            result.Row0.Y = inverse[0, 1];
            result.Row0.Z = inverse[0, 2];
            result.Row0.W = inverse[0, 3];
            result.Row1.X = inverse[1, 0];
            result.Row1.Y = inverse[1, 1];
            result.Row1.Z = inverse[1, 2];
            result.Row1.W = inverse[1, 3];
            result.Row2.X = inverse[2, 0];
            result.Row2.Y = inverse[2, 1];
            result.Row2.Z = inverse[2, 2];
            result.Row2.W = inverse[2, 3];
            result.Row3.X = inverse[3, 0];
            result.Row3.Y = inverse[3, 1];
            result.Row3.Z = inverse[3, 2];
            result.Row3.W = inverse[3, 3];
        }

        [Pure]
        public static Matrix4 Invert(Matrix4 mat) {
            Invert(mat, out Matrix4 result);
            return result;
        }

        [Pure]
        public static Matrix4 Transpose(Matrix4 mat) => new Matrix4(mat.Column0, mat.Column1, mat.Column2, mat.Column3);

        public static void Transpose(Matrix4 mat, out Matrix4 result) {
            result.Row0 = mat.Column0;
            result.Row1 = mat.Column1;
            result.Row2 = mat.Column2;
            result.Row3 = mat.Column3;
        }

        [Pure]
        public static Matrix4 operator *(Matrix4 left, Matrix4 right) => Mult(left, right);

        [Pure]
        public static Matrix4 operator *(Matrix4 left, float right) => Mult(left, right);

        [Pure]
        public static Matrix4 operator +(Matrix4 left, Matrix4 right) => Add(left, right);

        [Pure]
        public static Matrix4 operator -(Matrix4 left, Matrix4 right) => Subtract(left, right);

        [Pure]
        public static bool operator ==(Matrix4 left, Matrix4 right) => left.Equals(right);

        [Pure]
        public static bool operator !=(Matrix4 left, Matrix4 right) => !left.Equals(right);

        public override string ToString() => $"({Row0.X} {Row0.Y} {Row0.Z} {Row0.W})\n({Row1.X} {Row1.Y} {Row1.Z} {Row1.W})\n({Row2.X} {Row2.Y} {Row2.Z} {Row2.W})\n({Row3.X} {Row3.Y} {Row3.Z} {Row3.W})";

        public override int GetHashCode() => HashCode.Combine(Row0, Row1, Row2, Row3);

        [Pure]
        public override bool Equals(object obj) => obj is Matrix4 matrix && Equals(matrix);

        [Pure]
        public bool Equals(Matrix4 other) =>
            Row0 == other.Row0 &&
            Row1 == other.Row1 &&
            Row2 == other.Row2 &&
            Row3 == other.Row3;
    }
}

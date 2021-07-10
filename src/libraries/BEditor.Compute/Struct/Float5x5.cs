// Float5x5.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Runtime.InteropServices;

namespace BEditor.Compute
{
#pragma warning disable CS1591, SA1600
    [StructLayout(LayoutKind.Sequential)]
    public struct Float5x5
    {
        public float M00;
        public float M01;
        public float M02;
        public float M03;
        public float M04;

        public float M10;
        public float M11;
        public float M12;
        public float M13;
        public float M14;

        public float M20;
        public float M21;
        public float M22;
        public float M23;
        public float M24;

        public float M30;
        public float M31;
        public float M32;
        public float M33;
        public float M34;

        public float M40;
        public float M41;
        public float M42;
        public float M43;
        public float M44;

        public Float5x5(
            float m00, float m01, float m02, float m03, float m04,
            float m10, float m11, float m12, float m13, float m14,
            float m20, float m21, float m22, float m23, float m24,
            float m30, float m31, float m32, float m33, float m34,
            float m40, float m41, float m42, float m43, float m44)
        {
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M03 = m03;
            M04 = m04;

            M10 = m10;
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M14 = m14;

            M20 = m20;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M24 = m24;

            M30 = m30;
            M31 = m31;
            M32 = m32;
            M33 = m33;
            M34 = m34;

            M40 = m40;
            M41 = m41;
            M42 = m42;
            M43 = m43;
            M44 = m44;
            M44 = m44;
        }
    }
#pragma warning restore CS1591, SA1600
}
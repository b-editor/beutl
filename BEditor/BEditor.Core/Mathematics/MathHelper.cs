using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;

using static System.Math;

namespace BEditor.Mathematics {
    public static class MathHelper {
        public const float Pi = 3.1415927f;

        public const float PiOver2 = Pi / 2;

        public const float PiOver3 = Pi / 3;

        public const float PiOver4 = Pi / 4;

        public const float PiOver6 = Pi / 6;

        public const float TwoPi = 2 * Pi;

        public const float ThreePiOver2 = 3 * Pi / 2;

        public const float E = 2.7182817f;

        public const float Log10E = 0.4342945f;

        public const float Log2E = 1.442695f;

        [Pure]
        public static long NextPowerOfTwo(long n) {
            if (n < 0) {
                throw new ArgumentOutOfRangeException(nameof(n), "Must be positive.");
            }

            return (long)Math.Pow(2, Math.Ceiling(Math.Log(n, 2)));
        }

        [Pure]
        public static int NextPowerOfTwo(int n) {
            if (n < 0) {
                throw new ArgumentOutOfRangeException(nameof(n), "Must be positive.");
            }

            return (int)Math.Pow(2, Math.Ceiling(Math.Log(n, 2)));
        }

        [Pure]
        public static float NextPowerOfTwo(float n) {
            if (n < 0) {
                throw new ArgumentOutOfRangeException(nameof(n), "Must be positive.");
            }

            return MathF.Pow(2, MathF.Ceiling(MathF.Log(n, 2)));
        }

        [Pure]
        public static double NextPowerOfTwo(double n) {
            if (n < 0) {
                throw new ArgumentOutOfRangeException(nameof(n), "Must be positive.");
            }

            return Math.Pow(2, Math.Ceiling(Math.Log(n, 2)));
        }

        [Pure]
        public static long Factorial(int n) {
            long result = 1;

            for (; n > 1; n--) {
                result *= n;
            }

            return result;
        }

        [Pure]
        public static long BinomialCoefficient(int n, int k) {
            return Factorial(n) / (Factorial(k) * Factorial(n - k));
        }

        [Pure]
        public static float InverseSqrtFast(float x) {
            unsafe {
                var xhalf = 0.5f * x;
                var i = *(int*)&x; // Read bits as integer.
                i = 0x5f375a86 - (i >> 1); // Make an initial guess for Newton-Raphson approximation
                x = *(float*)&i; // Convert bits back to float
                x = x * (1.5f - (xhalf * x * x)); // Perform left single Newton-Raphson step.
                return x;
            }
        }

        [Pure]
        public static double InverseSqrtFast(double x) {
            unsafe {
                double xhalf = 0.5 * x;
                long i = *(long*)&x; // Read bits as long.
                i = 0x5fe6eb50c7b537a9 - (i >> 1); // Make an initial guess for Newton-Raphson approximation
                x = *(double*)&i; // Convert bits back to double
                x = x * (1.5 - (xhalf * x * x)); // Perform left single Newton-Raphson step.
                return x;
            }
        }

        [Pure]
        public static float DegreesToRadians(float degrees) {
            const float degToRad = MathF.PI / 180.0f;
            return degrees * degToRad;
        }

        [Pure]
        public static float RadiansToDegrees(float radians) {
            const float radToDeg = 180.0f / MathF.PI;
            return radians * radToDeg;
        }

        [Pure]
        public static double DegreesToRadians(double degrees) {
            const double degToRad = Math.PI / 180.0;
            return degrees * degToRad;
        }

        [Pure]
        public static double RadiansToDegrees(double radians) {
            const double radToDeg = 180.0 / Math.PI;
            return radians * radToDeg;
        }

        public static void Swap(ref double a, ref double b) {
            var temp = a;
            a = b;
            b = temp;
        }

        public static void Swap(ref float a, ref float b) {
            var temp = a;
            a = b;
            b = temp;
        }

        [Pure]
        private static unsafe int FloatToInt32Bits(float f) {
            return *((int*)&f);
        }

        [Pure]
        public static int ScaleValue (
            int value,
            int valueMin,
            int valueMax,
            int resultMin,
            int resultMax
        ) {
            if (valueMin >= valueMax || resultMin >= resultMax) {
                throw new ArgumentOutOfRangeException();
            }

            value = Clamp(value, valueMin, valueMax);

            var range = resultMax - resultMin;
            long temp = (value - valueMin) * range; // need long to avoid overflow
            return (int)((temp / (valueMax - valueMin)) + resultMin);
        }

        [Pure]
        public static bool ApproximatelyEqual(float a, float b, int maxDeltaBits) {
            // we use longs here, otherwise we run into a two's complement problem, causing this to fail with -2 and 2.0
            long k = FloatToInt32Bits(a);
            if (k < 0) {
                k = int.MinValue - k;
            }

            long l = FloatToInt32Bits(b);
            if (l < 0) {
                l = int.MinValue - l;
            }

            var intDiff = Math.Abs(k - l);
            return intDiff <= 1 << maxDeltaBits;
        }

        [Pure]
        public static bool ApproximatelyEqualEpsilon(double a, double b, double epsilon) {
            const double doubleNormal = (1L << 52) * double.Epsilon;
            var absA = Math.Abs(a);
            var absB = Math.Abs(b);
            var diff = Math.Abs(a - b);

            if (a == b) {
                // Shortcut, handles infinities
                return true;
            }

            if (a == 0.0f || b == 0.0f || diff < doubleNormal) {
                // a or b is zero, or both are extremely close to it.
                // relative error is less meaningful here
                return diff < epsilon * doubleNormal;
            }

            // use relative error
            return diff / Math.Min(absA + absB, double.MaxValue) < epsilon;
        }

        [Pure]
        public static bool ApproximatelyEqualEpsilon(float a, float b, float epsilon) {
            const float floatNormal = (1 << 23) * float.Epsilon;
            var absA = Math.Abs(a);
            var absB = Math.Abs(b);
            var diff = Math.Abs(a - b);

            if (a == b) {
                // Shortcut, handles infinities
                return true;
            }

            if (a == 0.0f || b == 0.0f || diff < floatNormal) {
                // a or b is zero, or both are extremely close to it.
                // relative error is less meaningful here
                return diff < epsilon * floatNormal;
            }

            // use relative error
            var relativeError = diff / Math.Min(absA + absB, float.MaxValue);
            return relativeError < epsilon;
        }

        [Pure]
        public static bool ApproximatelyEquivalent(float a, float b, float tolerance) {
            if (a == b) {
                // Early bailout, handles infinities
                return true;
            }

            var diff = Math.Abs(a - b);
            return diff <= tolerance;
        }

        [Pure]
        public static bool ApproximatelyEquivalent(double a, double b, double tolerance) {
            if (a == b) {
                // Early bailout, handles infinities
                return true;
            }

            var diff = Math.Abs(a - b);
            return diff <= tolerance;
        }

        [Pure]
        public static float Lerp(float start, float end, float t) {
            t = Clamp(t, 0, 1);
            return start + (t * (end - start));
        }

        public static float NormalizeAngle(float angle) {
            // returns angle the range [0, 360)
            angle = ClampAngle(angle);

            if (angle > 180f) {
                // shift angle to range (-180, 180]
                angle -= 360f;
            }

            return angle;
        }

        public static double NormalizeAngle(double angle) {
            // returns angle the range [0, 360)
            angle = ClampAngle(angle);

            if (angle > 180f) {
                // shift angle to range (-180, 180]
                angle -= 360f;
            }

            return angle;
        }

        public static float NormalizeRadians(float angle) {
            // returns angle the range [0, 2π).
            angle = ClampRadians(angle);

            if (angle > PiOver2) {
                // shift angle to range (-π, π]
                angle -= 2 * Pi;
            }

            return angle;
        }

        public static double NormalizeRadians(double angle) {
            // returns angle the range [0, 2π).
            angle = ClampRadians(angle);

            if (angle > PiOver2) {
                // shift angle to range (-π, π]
                angle -= 2 * Pi;
            }

            return angle;
        }

        public static float ClampAngle(float angle) {
            // mod angle so it's the range (-360, 360)
            angle %= 360f;

            // abs angle so it's the range [0, 360)
            angle = Abs(angle);

            return angle;
        }

        public static double ClampAngle(double angle) {
            // mod angle so it's the range (-360, 360)
            angle %= 360f;

            // abs angle so it's the range [0, 360)
            angle = Abs(angle);

            return angle;
        }

        public static float ClampRadians(float angle) {
            // mod angle so it's the range (-2π,2π)
            angle %= 2 * Pi;

            // abs angle so it's the range [0,2π)
            angle = Abs(angle);

            return angle;
        }

        public static double ClampRadians(double angle) {
            // mod angle so it's the range (-2π,2π)
            angle %= 2 * Pi;

            // abs angle so it's the range [0,2π)
            angle = Abs(angle);

            return angle;
        }
    }
}

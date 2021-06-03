using System.Diagnostics.Contracts;
using System.Numerics;

namespace BEditor.Extensions.AviUtl
{
    public struct BezierCurveCubic
    {
        public Vector3 StartAnchor;

        public Vector3 EndAnchor;

        public Vector3 FirstControlPoint;

        public Vector3 SecondControlPoint;

        public float Parallel;

        public BezierCurveCubic(
            Vector3 startAnchor,
            Vector3 endAnchor,
            Vector3 firstControlPoint,
            Vector3 secondControlPoint)
        {
            StartAnchor = startAnchor;
            EndAnchor = endAnchor;
            FirstControlPoint = firstControlPoint;
            SecondControlPoint = secondControlPoint;
            Parallel = 0.0f;
        }

        public BezierCurveCubic(
            float parallel,
            Vector3 startAnchor,
            Vector3 endAnchor,
            Vector3 firstControlPoint,
            Vector3 secondControlPoint)
        {
            Parallel = parallel;
            StartAnchor = startAnchor;
            EndAnchor = endAnchor;
            FirstControlPoint = firstControlPoint;
            SecondControlPoint = secondControlPoint;
        }

        [Pure]
        public Vector3 CalculatePoint(float t)
        {
            var c = 1.0f - t;

            float x = (StartAnchor.X * c * c * c) + (FirstControlPoint.X * 3 * t * c * c) +
                (SecondControlPoint.X * 3 * t * t * c) + (EndAnchor.X * t * t * t);

            float y = (StartAnchor.Y * c * c * c) + (FirstControlPoint.Y * 3 * t * c * c) +
                (SecondControlPoint.Y * 3 * t * t * c) + (EndAnchor.Y * t * t * t);

            float z = (StartAnchor.Z * c * c * c) + (FirstControlPoint.Z * 3 * t * c * c) +
                (SecondControlPoint.Z * 3 * t * t * c) + (EndAnchor.Z * t * t * t);

            var r = new Vector3(x, y, z);

            if (Parallel == 0.0f)
            {
                return r;
            }

            Vector3 perpendicular;

            if (t == 0.0f)
            {
                perpendicular = FirstControlPoint - StartAnchor;
            }
            else
            {
                perpendicular = r - CalculatePointOfDerivative(t);
            }
            var tmp = Vector3.Normalize(perpendicular);
            return r + (new Vector3(tmp.Y, -tmp.X, tmp.Z) * Parallel);
        }

        [Pure]
        private Vector3 CalculatePointOfDerivative(float t)
        {
            var c = 1.0f - t;
            var r = new Vector3(
                (c * c * StartAnchor.X) + (2 * t * c * FirstControlPoint.X) + (t * t * SecondControlPoint.X),
                (c * c * StartAnchor.Y) + (2 * t * c * FirstControlPoint.Y) + (t * t * SecondControlPoint.Y),
                (c * c * StartAnchor.Z) + (2 * t * c * FirstControlPoint.Z) + (t * t * SecondControlPoint.Z));

            return r;
        }

        [Pure]
        public float CalculateLength(float precision)
        {
            var length = 0.0f;
            var old = CalculatePoint(0.0f);

            for (var i = precision; i < 1.0f + precision; i += precision)
            {
                var n = CalculatePoint(i);
                length += (n - old).Length();
                old = n;
            }

            return length;
        }
    }
}
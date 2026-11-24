using System.Runtime.InteropServices;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;

namespace Beutl.Graphics.Effects;

public sealed partial class SteppedMotion
{
    // Key frames on the rate count steps as the rate's integral from the drawable's start, so each step lasts 1/rate
    // at the rate in effect and the held time never goes back as time advances. Each piece between key frames keeps
    // the intervals that adaptive Gauss-Kronrod quadrature divided it into, with the integral up to each, until the key
    // frames change. A frame then costs a lookup per piece it passes and a few evaluations of the rate, however long
    // the element is, and gives the same result whatever was rendered before.
    private sealed class KeyframedRate
    {
        // A count this close below a whole number has reached it, so that the float rounding of a rate never puts a
        // frame that falls on a boundary in the step before. At the lowest rate this is a millisecond.
        private const double Snap = 1e-5;

        // The 15-point Gauss-Kronrod abscissae on one side of the centre; the odd ones and the centre are the
        // 7-point Gauss rule's.
        private static readonly double[] s_nodes =
        [
            0.991455371120812639206854697526329, 0.949107912342758524526189684047851,
            0.864864423359769072789712788640926, 0.741531185599394439863864773280788,
            0.586087235467691130294144845693013, 0.405845151377397166906606412076961,
            0.207784955007898467600689403773245,
        ];

        // The Kronrod weights of those abscissae, then of the centre.
        private static readonly double[] s_kronrodWeights =
        [
            0.022935322010529224963732008058970, 0.063092092629978553290700663189204,
            0.104790010322250183839876322541518, 0.140653259715525918745189590510238,
            0.169004726639267902826583426598550, 0.190350578064785409913256402421014,
            0.204432940075298892414161999234649, 0.209482141084727828012999174891714,
        ];

        // The Gauss weights of the odd abscissae, then of the centre.
        private static readonly double[] s_gaussWeights =
        [
            0.129484966168869693270611432679082, 0.279705391489276667901467771423780,
            0.381830050505118944950369775488975, 0.417959183673469387755102040816327,
        ];

        // How closely the two rules must agree per flick, as a fraction of the piece's largest rate. It stays above the
        // rounding of a float rate, so dividing ends early.
        private static readonly double s_tolerance = Math.ScaleB(1d, -18);

        private readonly Dictionary<(long From, long To), Piece> _pieces = [];
        private List<Key> _keys = [];
        private List<Key> _scratch = [];
        private KeyFrameAnimation<float>? _animation;
        // The scene time, in flicks, of the key frames' zero.
        private long _origin;

        // Whether key frames vary the property's rate, taking them as they are now. An expression on top can give any
        // rate at any time, and key frames of one value give one rate, so both hold as a fixed rate does.
        public bool Follows(IExpressionProperty<float> property)
        {
            if (property.HasExpression || property.GetKeyFrameAnimation() is not { KeyFrames.Count: > 0 } animation)
                return false;

            long origin = animation.ToScene(MediaTime.Zero).Flicks;
            var keys = animation.KeyFrames;
            bool varies = false;
            _scratch.Clear();
            for (int i = 0; i < keys.Count; i++)
            {
                IKeyFrame key = keys[i];
                float value = key is KeyFrame<float> typed ? typed.Value.Value : float.NaN;
                Easing easing = key.Easing.Value;
                // A spline easing's handles move in place, which no event of the animation tells, so what the easing
                // gives at a few progresses stands for it.
                _scratch.Add(new Key(key.KeyTime.Value.Flicks + origin, value, easing,
                    (easing.Ease(0.2f), easing.Ease(0.4f), easing.Ease(0.6f), easing.Ease(0.8f))));
                varies |= value != _scratch[0].Value;
            }

            if (!varies) return false;

            if (!ReferenceEquals(animation, _animation) || origin != _origin
                || !CollectionsMarshal.AsSpan(_scratch).SequenceEqual(CollectionsMarshal.AsSpan(_keys)))
            {
                (_keys, _scratch) = (_scratch, _keys);
                _animation = animation;
                _origin = origin;
                _pieces.Clear();
            }

            return true;
        }

        // The time at which the steps counted from start last reached a whole number by time. Before start they count
        // back from it. It depends on start and the count alone, so every frame of a step holds the very same time.
        public MediaTime HeldTime(MediaTime start, MediaTime time)
        {
            long from = start.Flicks;
            long to = time.Flicks;
            double steps = Integral(from, to) / MediaTime.FlicksPerSecond;
            double held = to >= from
                ? Reach(from, 1, Math.Floor(steps + Snap) * MediaTime.FlicksPerSecond)
                : Reach(from, -1, Math.Ceiling(steps - Snap) * MediaTime.FlicksPerSecond);
            return new MediaTime((long)Math.Round(held));
        }

        // The rate's integral between from and to in rate × flicks, adding whole pieces from from on.
        private double Integral(long from, long to)
        {
            int direction = Math.Sign(to - from);
            double sum = 0;
            for (long position = from; position != to;)
            {
                long next = Next(position, direction);
                if (direction > 0 ? next < to : next > to)
                {
                    sum += Between(position, next).Total;
                    position = next;
                    continue;
                }

                // Only the part of the last piece up to to counts.
                if (next is long.MaxValue or long.MinValue)
                    return sum + Math.Abs((double)to - position) * Rate(position + direction);

                if (direction > 0) return sum + Prefix(Between(position, next), to);

                Piece piece = Between(next, position);
                return sum + piece.Total - Prefix(piece, to);
            }

            return sum;
        }

        // The time beyond from, in the direction, at which the integral from it reaches amount.
        private double Reach(long from, int direction, double amount)
        {
            for (long position = from; ;)
            {
                if (amount <= 0) return position;

                long next = Next(position, direction);
                // Past the last key frame, or before the first, the rate holds.
                if (next is long.MaxValue or long.MinValue)
                    return position + direction * amount / Rate(position + direction);

                Piece piece = Between(Math.Min(position, next), Math.Max(position, next));
                if (piece.Total > amount)
                    return Inverse(piece, direction > 0 ? amount : piece.Total - amount);

                amount -= piece.Total;
                position = next;
            }
        }

        // The piece from a to b, which no key frame divides, divided where its rate needs it.
        private Piece Between(long a, long b)
        {
            if (_pieces.TryGetValue((a, b), out Piece? piece)) return piece;

            // Pieces that began at an earlier start of the drawable are left behind by moving it.
            if (_pieces.Count >= 2 * _keys.Count + 16) _pieces.Clear();

            Quadrature estimate = Estimate(a, b);
            if (estimate.Minimum == estimate.Maximum)
            {
                piece = new Piece([a, b], [0, estimate.Minimum * (b - a)], estimate.Minimum);
            }
            else
            {
                List<double> ends = [a];
                List<double> sums = [0];
                Divide(a, b, estimate, s_tolerance * estimate.Maximum, ends, sums);
                piece = new Piece([.. ends], [.. sums], double.NaN);
            }

            _pieces[(a, b)] = piece;
            return piece;
        }

        // Halves an interval until its 15-point Kronrod estimate agrees with the 7-point Gauss one, then adds it. Neither
        // rule samples the ends, so a hold key frame's value at its own time never counts for the piece before it.
        private void Divide(double a, double b, Quadrature estimate, double tolerance, List<double> ends, List<double> sums)
        {
            if (estimate.Minimum != estimate.Maximum && b - a > 16
                && Math.Abs(estimate.Kronrod - estimate.Gauss) > tolerance * (b - a))
            {
                double middle = a + (b - a) / 2;
                Divide(a, middle, Estimate(a, middle), tolerance, ends, sums);
                Divide(middle, b, Estimate(middle, b), tolerance, ends, sums);
                return;
            }

            ends.Add(b);
            sums.Add(sums[^1] + Area(estimate, b - a));
        }

        // The integral from the piece's start to x, which lies in it.
        private double Prefix(Piece piece, double x)
        {
            if (!double.IsNaN(piece.Rate)) return piece.Rate * (x - piece.Ends[0]);

            int i = Last(piece.Ends, x);
            return piece.Sums[i] + Area(Estimate(piece.Ends[i], x), x - piece.Ends[i]);
        }

        // Where the integral from the piece's start reaches amount, which is less than the piece's total. Newton's
        // steps within the interval that holds it take a few evaluations; one that leaves the bracket the evaluations so
        // far set bisects it instead, as a kink in the rate can need.
        private double Inverse(Piece piece, double amount)
        {
            if (!double.IsNaN(piece.Rate)) return piece.Ends[0] + amount / piece.Rate;

            int i = Last(piece.Sums, amount);
            double start = piece.Ends[i];
            double target = amount - piece.Sums[i];
            double low = start;
            double high = piece.Ends[i + 1];
            // A float rate's rounding leaves the integral uncertain by about 2^-24 of the interval, so steps finer than
            // this only wander.
            double resolution = Math.Max(0.25, Math.ScaleB(high - start, -22));
            double x = start + (high - start) * (target / (piece.Sums[i + 1] - piece.Sums[i]));
            for (int step = 0; step < 64 && high - low > resolution; step++)
            {
                double excess = Area(Estimate(start, x), x - start) - target;
                if (excess == 0) break;
                if (excess < 0) low = x;
                else high = x;

                double next = x - excess / Rate(x);
                if (!(next > low && next < high)) next = low + (high - low) / 2;
                if (Math.Abs(next - x) < resolution) return next;

                x = next;
            }

            return x;
        }

        // The index of the last interval whose start is at or before value, by binary search over the ascending values.
        private static int Last(double[] values, double value)
        {
            int low = 0;
            int high = values.Length - 2;
            while (low < high)
            {
                int middle = (low + high + 1) >>> 1;
                if (values[middle] <= value) low = middle;
                else high = middle - 1;
            }

            return low;
        }

        // The estimate's integral over an interval of width, exact when every node gave the same rate.
        private static double Area(Quadrature estimate, double width)
            => estimate.Minimum == estimate.Maximum ? estimate.Minimum * width : estimate.Kronrod;

        private Quadrature Estimate(double a, double b)
        {
            double half = (b - a) / 2;
            double center = a + half;
            double value = Rate(center);
            double kronrod = s_kronrodWeights[^1] * value;
            double gauss = s_gaussWeights[^1] * value;
            double minimum = value;
            double maximum = value;
            for (int i = 0; i < s_nodes.Length; i++)
            {
                double left = Rate(center - half * s_nodes[i]);
                double right = Rate(center + half * s_nodes[i]);
                minimum = Math.Min(minimum, Math.Min(left, right));
                maximum = Math.Max(maximum, Math.Max(left, right));
                kronrod += s_kronrodWeights[i] * (left + right);
                if (i % 2 == 1) gauss += s_gaussWeights[i / 2] * (left + right);
            }

            return new Quadrature(kronrod * half, gauss * half, minimum, maximum);
        }

        // The rate at a scene time in flicks, clamped as the effect clamps the current rate.
        private double Rate(double flicks)
        {
            float rate = _animation!.Interpolate(new MediaTime((long)Math.Round(flicks) - _origin));
            return float.IsNaN(rate) ? MinimumRate : Math.Clamp(rate, (float)MinimumRate, MaximumRate);
        }

        // The nearest key frame time beyond position in the direction, or MaxValue or MinValue when there is none.
        private long Next(long position, int direction)
        {
            int low = 0;
            int high = _keys.Count;
            while (low < high)
            {
                int middle = (low + high) >>> 1;
                if (_keys[middle].Time > position) high = middle;
                else low = middle + 1;
            }

            if (direction > 0) return low < _keys.Count ? _keys[low].Time : long.MaxValue;

            while (low > 0 && _keys[low - 1].Time == position) low--;
            return low > 0 ? _keys[low - 1].Time : long.MinValue;
        }

        private readonly record struct Key(long Time, float Value, Easing Easing, (float, float, float, float) Shape);

        private readonly record struct Quadrature(double Kronrod, double Gauss, double Minimum, double Maximum);

        // A piece as adaptive quadrature divided it: the ends of its intervals and the integral up to each, in
        // rate × flicks, with its one rate when every node gave the same, otherwise NaN.
        private sealed record Piece(double[] Ends, double[] Sums, double Rate)
        {
            public double Total => Sums[^1];
        }
    }
}

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

        // How closely the two rules must agree per flick, as a fraction of the piece's largest rate. It stays above the
        // rounding of a float rate, so dividing ends early.
        private static readonly double s_tolerance = Math.ScaleB(1d, -18);

        private readonly Dictionary<(long From, long To), Piece> _pieces = [];
        private List<Key> _keys = [];
        private List<Key> _scratch = [];
        private KeyFrameAnimation<float>? _animation;
        // The scene time, in flicks, of the key frames' zero.
        private long _origin;
        private readonly Func<double, double> _rate;

        public KeyframedRate() => _rate = Rate;

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
                    sum += Between(Math.Min(position, next), Math.Max(position, next)).Total;
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

            double rate = ConstantRate(a, b);
            if (!double.IsNaN(rate))
            {
                piece = new Piece([a, b], [0, rate * (b - a)], rate);
            }
            else
            {
                // At this resolution the nodes stay inside a piece, even next to a hold key's time.
                var (ends, sums) = GaussKronrod.Partition(_rate, a, b, s_tolerance, 1024);
                piece = new Piece(ends, sums, double.NaN);
            }

            _pieces[(a, b)] = piece;
            return piece;
        }

        // The one rate the key frames give throughout the piece from a to b, which no key frame divides, or NaN when it
        // does not follow from them. Samples cannot tell: an easing that overshoots can clamp at every one of them and
        // still change near an end.
        private double ConstantRate(long a, long b)
        {
            // The key frame that ends the piece, whose easing shapes it.
            int next = FirstAtOrAfter(b);
            // Past the last key frame its value holds.
            if (next == _keys.Count) return Clamp(_keys[^1].Value);

            Key end = _keys[next];
            // Equal values stay equal through an easing that keeps its output finite.
            if (next > 0) return _keys[next - 1].Value == end.Value && Bounded(end.Easing) ? Clamp(end.Value) : double.NaN;

            // Before the first key frame its value holds throughout when it is at the key frames' zero, and otherwise
            // while its easing's progress, counted from that zero, stays in [0, 1].
            return end.Time == _origin || (a >= _origin && Bounded(end.Easing)) ? Clamp(end.Value) : double.NaN;

            static bool Bounded(Easing easing)
                => easing.TryGetOutputRange(out float minimum, out float maximum)
                    && float.IsFinite(minimum) && float.IsFinite(maximum);
        }

        // The integral from the piece's start to x, which lies in it.
        private double Prefix(Piece piece, double x)
        {
            if (!double.IsNaN(piece.Rate)) return piece.Rate * (x - piece.Ends[0]);

            return GaussKronrod.Prefix(_rate, piece.Ends, piece.Sums, x);
        }

        // Where the integral from the piece's start reaches amount, which is less than the piece's total. Newton's
        // steps within the interval that holds it take a few evaluations; one that leaves the bracket the evaluations so
        // far set bisects it instead, as a kink in the rate can need.
        private double Inverse(Piece piece, double amount)
        {
            if (!double.IsNaN(piece.Rate)) return piece.Ends[0] + amount / piece.Rate;

            int i = GaussKronrod.FindInterval(piece.Sums, amount);
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
                double excess = GaussKronrod.Integrate(_rate, start, x) - target;
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

        // The rate at a scene time in flicks, clamped as the effect clamps the current rate. A key frame's progress is a
        // float, which rounds to 1 for the last 2^-25 of the span before the key frame, so a time there is read where
        // the progress is still below 1: a hold key frame's value would otherwise count from just before its time.
        private double Rate(double flicks)
        {
            long time = (long)Math.Round(flicks);
            int next = FirstAtOrAfter(time);
            if (next < _keys.Count && _keys[next].Time > time)
            {
                long span = _keys[next].Time - (next > 0 ? _keys[next - 1].Time : _origin);
                if (span > 0) time = Math.Min(time, _keys[next].Time - (long)Math.Ceiling(Math.ScaleB((double)span, -24)));
            }

            return Clamp(_animation!.Interpolate(new MediaTime(time - _origin)));
        }

        private static double Clamp(float rate)
            => float.IsNaN(rate) ? MinimumRate : Math.Clamp(rate, (float)MinimumRate, MaximumRate);

        // The nearest key frame time beyond position in the direction, or MaxValue or MinValue when there is none.
        private long Next(long position, int direction)
        {
            if (direction > 0)
            {
                int after = FirstAtOrAfter(position + 1);
                return after < _keys.Count ? _keys[after].Time : long.MaxValue;
            }

            int before = FirstAtOrAfter(position) - 1;
            return before >= 0 ? _keys[before].Time : long.MinValue;
        }

        // The index of the first key frame at or after time, or the count when there is none, by binary search over the
        // times in order.
        private int FirstAtOrAfter(long time)
        {
            int low = 0;
            int high = _keys.Count;
            while (low < high)
            {
                int middle = (low + high) >>> 1;
                if (_keys[middle].Time >= time) high = middle;
                else low = middle + 1;
            }

            return low;
        }

        private readonly record struct Key(long Time, float Value, Easing Easing, (float, float, float, float) Shape);

        // A piece as adaptive quadrature divided it: the ends of its intervals and the integral up to each, in
        // rate × flicks, with its one rate when the key frames give it one, otherwise NaN.
        private sealed record Piece(double[] Ends, double[] Sums, double Rate)
        {
            public double Total => Sums[^1];
        }
    }
}

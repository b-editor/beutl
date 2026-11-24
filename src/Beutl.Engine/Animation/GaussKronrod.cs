namespace Beutl.Animation;

internal static class GaussKronrod
{
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

    public static (double[] Ends, double[] Sums) Partition(Func<double, double> rate, double a, double b,
        double relativeTolerance, double minimumWidth, int panels = 1)
    {
        Quadrature estimate = Estimate(rate, a, b);
        double scale = Math.Max(Math.Max(Magnitude(estimate.Minimum), Magnitude(estimate.Maximum)),
            Math.Max(Magnitude(rate(a)), Magnitude(rate(b))));
        double tolerance = relativeTolerance * scale;
        List<double> ends = [a];
        List<double> sums = [0];
        if (panels == 1)
        {
            Divide(rate, a, b, estimate, tolerance, minimumWidth, ends, sums);
        }
        else
        {
            double start = a;
            for (int i = 1; i <= panels; i++)
            {
                double end = i == panels ? b : a + (b - a) * (i / (double)panels);
                Divide(rate, start, end, Estimate(rate, start, end), tolerance, minimumWidth, ends, sums);
                start = end;
            }
        }

        return ([.. ends], [.. sums]);
    }

    private static double Magnitude(double value) => double.IsFinite(value) ? Math.Abs(value) : 0;

    public static double Integrate(Func<double, double> rate, double a, double b)
        => Area(Estimate(rate, a, b), b - a);

    public static double Prefix(Func<double, double> rate, double[] ends, double[] sums, double x)
    {
        int i = FindInterval(ends, x);
        return sums[i] + Integrate(rate, ends[i], x);
    }

    // Halves an interval until its estimate holds or the caller's resolution is reached.
    private static void Divide(Func<double, double> rate, double a, double b, Quadrature estimate,
        double tolerance, double minimumWidth, List<double> ends, List<double> sums)
    {
        if (b - a > minimumWidth && double.IsFinite(estimate.Minimum) && double.IsFinite(estimate.Maximum)
            && !Holds(rate, a, b, estimate, tolerance))
        {
            double middle = a + (b - a) / 2;
            Divide(rate, a, middle, Estimate(rate, a, middle), tolerance, minimumWidth, ends, sums);
            Divide(rate, middle, b, Estimate(rate, middle, b), tolerance, minimumWidth, ends, sums);
            return;
        }

        ends.Add(b);
        sums.Add(sums[^1] + Area(estimate, b - a));
    }

    // Whether an interval's estimate holds: its 15-point Kronrod and 7-point Gauss rules agree, and the rate at each
    // end continues the line through the two nodes nearest that end. Neither rule samples the ends, which lie 0.43%
    // of the interval beyond the outermost nodes, so a change there, such as an overshooting easing leaving the
    // rate's limit, shows only at the ends.
    private static bool Holds(Func<double, double> rate, double a, double b, Quadrature estimate, double tolerance)
    {
        if (Math.Abs(estimate.Kronrod - estimate.Gauss) > tolerance * (b - a)) return false;

        // What a change between an outermost node and its end can add stays within the tolerance.
        double gap = 1 - s_nodes[0];
        double reach = gap / (s_nodes[0] - s_nodes[1]);
        double allowed = 2 * tolerance / gap;
        return Math.Abs(rate(a) - (estimate.Left + (estimate.Left - estimate.LeftInner) * reach)) <= allowed
            && Math.Abs(rate(b) - (estimate.Right + (estimate.Right - estimate.RightInner) * reach)) <= allowed;
    }

    // The index of the last interval whose start is at or before value, by binary search over the ascending values.
    public static int FindInterval(double[] values, double value)
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

    private static Quadrature Estimate(Func<double, double> rate, double a, double b)
    {
        double half = (b - a) / 2;
        double center = a + half;
        double value = rate(center);
        double kronrod = s_kronrodWeights[^1] * value;
        double gauss = s_gaussWeights[^1] * value;
        double minimum = value;
        double maximum = value;
        double left = 0, leftInner = 0, right = 0, rightInner = 0;
        for (int i = 0; i < s_nodes.Length; i++)
        {
            double before = rate(center - half * s_nodes[i]);
            double after = rate(center + half * s_nodes[i]);
            if (i == 0) (left, right) = (before, after);
            else if (i == 1) (leftInner, rightInner) = (before, after);
            minimum = Math.Min(minimum, Math.Min(before, after));
            maximum = Math.Max(maximum, Math.Max(before, after));
            kronrod += s_kronrodWeights[i] * (before + after);
            if (i % 2 == 1) gauss += s_gaussWeights[i / 2] * (before + after);
        }

        return new Quadrature(kronrod * half, gauss * half, minimum, maximum, left, leftInner, right, rightInner);
    }

    // The two rules' estimates, the extremes of the rates they sampled, and the rates at the two outermost nodes
    // on each side, outermost first.
    private readonly record struct Quadrature(double Kronrod, double Gauss, double Minimum, double Maximum,
        double Left, double LeftInner, double Right, double RightInner);
}

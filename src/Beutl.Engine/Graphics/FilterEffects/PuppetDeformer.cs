using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Beutl.Graphics.Effects;

// Two-dimensional ARAP with uniform edge weights and exact positional constraints.
internal sealed class PuppetDeformer
{
    // The iterations stop once no vertex moves further than this in one of them, in the input's units.
    private const float Tolerance = 1f / 64;
    // Bounds the work for a pose that converges slowly, such as one that folds the picture over itself.
    private const int MaxIterations = 100;
    // Amortize the two parallel loop dispatches over enough free vertices.
    private const int ParallelThreshold = 4096;
    private const int ParallelBlockSize = 512;
    private readonly PuppetMesh _mesh;
    private readonly int[][] _neighbors;
    private readonly int[] _pinAtVertex;
    private readonly int[] _component;
    private readonly int[] _componentPinCount;
    private readonly int[] _pinned;
    private readonly int[] _rotating;
    private readonly int[] _free;
    private readonly LaplacianFactor _laplacian;

    public PuppetDeformer(PuppetMesh mesh) : this(mesh, allowParallel: Environment.ProcessorCount > 1)
    {
    }

    internal PuppetDeformer(PuppetMesh mesh, bool allowParallel)
    {
        _mesh = mesh;
        _neighbors = BuildNeighbors(mesh);
        _component = FindComponents(_neighbors, out int componentCount);
        _componentPinCount = new int[componentCount];
        _pinAtVertex = new int[mesh.Rest.Length];
        Array.Fill(_pinAtVertex, -1);
        for (int pin = 0; pin < mesh.PinVertices.Length; pin++)
        {
            int vertex = mesh.PinVertices[pin];
            if (vertex < 0 || _pinAtVertex[vertex] >= 0) continue;

            _pinAtVertex[vertex] = pin;
            _componentPinCount[_component[vertex]]++;
        }

        _pinned = Enumerable.Range(0, mesh.Rest.Length).Where(vertex => _pinAtVertex[vertex] >= 0).ToArray();
        _rotating = Enumerable.Range(0, mesh.Rest.Length)
            .Where(vertex => _componentPinCount[_component[vertex]] >= 2).ToArray();
        _free = _rotating.Where(vertex => _pinAtVertex[vertex] < 0).ToArray();
        UsesParallelSteps = allowParallel && _free.Length >= ParallelThreshold;
        _laplacian = new LaplacianFactor(_neighbors, _free, _pinAtVertex);
    }

    internal bool UsesParallelSteps { get; }

    public Vector2[] Deform(Size size, Point[] targets) => Deform(size, targets, Tolerance, MaxIterations);

    // Tests solve with a tighter tolerance and more iterations for a converged reference.
    internal Vector2[] Deform(Size size, Point[] targets, float tolerance, int maxIterations)
    {
        Vector2[] rest = _mesh.Rest;
        var displacement = new Vector2[rest.Length];
        bool moved = false;
        foreach (int vertex in _pinned)
        {
            // A pin without a finite destination holds its vertex at rest.
            Vector2 target = PuppetMesh.ToLocal(size, targets[_pinAtVertex[vertex]]);
            if (!float.IsFinite(target.X) || !float.IsFinite(target.Y)) continue;

            Vector2 delta = target - rest[vertex];
            displacement[vertex] = delta;
            moved |= delta != Vector2.Zero;
        }
        if (!moved) return rest;

        // Every solve starts from the pins alone, never from an earlier frame, so it is independent of playback order.
        StartFromRigidFit(rest, displacement);
        if (_free.Length > 0)
            Relax(rest, displacement, tolerance, maxIterations);

        for (int vertex = 0; vertex < rest.Length; vertex++)
            displacement[vertex] += rest[vertex];
        foreach (int vertex in _pinned)
        {
            Vector2 target = PuppetMesh.ToLocal(size, targets[_pinAtVertex[vertex]]);
            if (float.IsFinite(target.X) && float.IsFinite(target.Y))
                displacement[vertex] = target;
        }
        return displacement;
    }

    // Moves each pinned component by the rotation and translation that best map its pins' rest positions onto their
    // destinations, a 2D Procrustes fit, so the local step starts near the rotations the pins ask for. A component with
    // one pin translates exactly, and pins that move rigidly need no further iteration.
    private void StartFromRigidFit(Vector2[] rest, Vector2[] displacement)
    {
        int count = _componentPinCount.Length;
        var restCenters = new Vector2D[count];
        var targetCenters = new Vector2D[count];
        foreach (int vertex in _pinned)
        {
            int component = _component[vertex];
            restCenters[component] += (Vector2D)rest[vertex];
            targetCenters[component] += (Vector2D)rest[vertex] + (Vector2D)displacement[vertex];
        }

        for (int component = 0; component < count; component++)
        {
            if (_componentPinCount[component] == 0) continue;

            restCenters[component] /= _componentPinCount[component];
            targetCenters[component] /= _componentPinCount[component];
        }

        // The best rotation's cosine and sine are in proportion to the sums of the dot and cross products of the pins'
        // rest and target offsets from their centers.
        var rotations = new Vector2D[count];
        foreach (int vertex in _pinned)
        {
            int component = _component[vertex];
            Vector2D from = (Vector2D)rest[vertex] - restCenters[component];
            Vector2D to = (Vector2D)rest[vertex] + (Vector2D)displacement[vertex] - targetCenters[component];
            rotations[component] += new Vector2D(from.X * to.X + from.Y * to.Y, from.X * to.Y - from.Y * to.X);
        }

        for (int component = 0; component < count; component++)
        {
            (double cosine, double sine) = rotations[component];
            double length = Math.Sqrt(cosine * cosine + sine * sine);
            rotations[component] = length > 0 ? rotations[component] / length : new Vector2D(1, 0);
        }

        for (int vertex = 0; vertex < rest.Length; vertex++)
        {
            int component = _component[vertex];
            if (_pinAtVertex[vertex] >= 0 || _componentPinCount[component] == 0) continue;

            (double cosine, double sine) = rotations[component];
            Vector2D offset = (Vector2D)rest[vertex] - restCenters[component];
            Vector2D value = targetCenters[component] - restCenters[component] + new Vector2D(
                (cosine - 1) * offset.X - sine * offset.Y,
                sine * offset.X + (cosine - 1) * offset.Y);
            displacement[vertex] = new Vector2((float)value.X, (float)value.Y);
        }
    }

    // Alternates the local step, which fits each vertex's rotation to its edges, and the global step, which solves the
    // positions for those rotations, until the vertices stop moving.
    private void Relax(Vector2[] rest, Vector2[] displacement, float tolerance, int maxIterations)
    {
        var rotations = new Vector2[rest.Length];
        var right = new Vector2[rest.Length];
        var solved = new Vector2[rest.Length];
        double[]? energyTerms = UsesParallelSteps ? ArrayPool<double>.Shared.Rent(_rotating.Length) : null;
        AndersonMixer? mixer = null;
        try
        {
            double energy = FitRotations(rest, displacement, rotations, energyTerms);
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                BuildRight(rest, displacement, rotations, right);
                _laplacian.Solve(right, solved);
                float largest = 0;
                foreach (int vertex in _free)
                    largest = MathF.Max(largest, Vector2.DistanceSquared(solved[vertex], displacement[vertex]));
                // A destination too far away to represent gives a solve that cannot converge.
                if (largest <= tolerance * tolerance || !float.IsFinite(largest))
                {
                    foreach (int vertex in _free)
                        displacement[vertex] = solved[vertex];
                    return;
                }

                mixer ??= new AndersonMixer(_free.Length);
                if (mixer.Mix(_free, displacement, solved))
                {
                    double mixedEnergy = FitRotations(rest, displacement, rotations, energyTerms);
                    if (mixedEnergy < energy)
                    {
                        energy = mixedEnergy;
                        continue;
                    }
                }

                foreach (int vertex in _free)
                    displacement[vertex] = solved[vertex];
                energy = FitRotations(rest, displacement, rotations, energyTerms);
            }
        }
        finally
        {
            mixer?.Dispose();
            if (energyTerms is not null)
                ArrayPool<double>.Shared.Return(energyTerms);
        }
    }

    // The local step: each vertex's rotation that best maps its rest edges onto the current ones. Returns the ARAP energy
    // of the current positions with those rotations.
    private double FitRotations(Vector2[] rest, Vector2[] displacement, Vector2[] rotations, double[]? energyTerms)
    {
        if (energyTerms is not null)
            return FitRotationsParallel(rest, displacement, rotations, energyTerms);

        double energy = 0;
        foreach (int vertex in _rotating)
            energy += FitRotation(rest, displacement, rotations, vertex);
        return energy;
    }

    private double FitRotationsParallel(Vector2[] rest, Vector2[] displacement, Vector2[] rotations, double[] energyTerms)
    {
        Parallel.For(0, (_rotating.Length + ParallelBlockSize - 1) / ParallelBlockSize, block =>
        {
            int start = block * ParallelBlockSize;
            int end = Math.Min(start + ParallelBlockSize, _rotating.Length);
            for (int i = start; i < end; i++)
                energyTerms[i] = FitRotation(rest, displacement, rotations, _rotating[i]);
        });

        // The sum must round in vertex order because it decides whether to keep the accelerated step.
        double energy = 0;
        for (int i = 0; i < _rotating.Length; i++)
            energy += energyTerms[i];
        return energy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double FitRotation(Vector2[] rest, Vector2[] displacement, Vector2[] rotations, int vertex)
    {
        Vector2 restVertex = rest[vertex];
        Vector2 displacementVertex = displacement[vertex];
        double cosine = 0;
        double sine = 0;
        double squares = 0;
        foreach (int other in _neighbors[vertex])
        {
            Vector2 original = restVertex - rest[other];
            Vector2 current = original + displacementVertex - displacement[other];
            cosine += (double)original.X * current.X + (double)original.Y * current.Y;
            sine += (double)original.X * current.Y - (double)original.Y * current.X;
            // Squared in double: the energies compared to accept an accelerated step can differ by less than a float
            // squared length rounds away.
            squares += (double)original.X * original.X + (double)original.Y * original.Y
                + (double)current.X * current.X + (double)current.Y * current.Y;
        }

        double length = Math.Sqrt(cosine * cosine + sine * sine);
        rotations[vertex] = length > 0
            ? new Vector2((float)(cosine / length), (float)(sine / length))
            : Vector2.UnitX;
        return squares - 2 * length;
    }

    private void BuildRight(Vector2[] rest, Vector2[] displacement, Vector2[] rotations, Vector2[] right)
    {
        if (UsesParallelSteps)
            BuildRightParallel(rest, displacement, rotations, right);
        else
            BuildRightRange(rest, displacement, rotations, right, 0, _free.Length);
    }

    private void BuildRightParallel(Vector2[] rest, Vector2[] displacement, Vector2[] rotations, Vector2[] right)
    {
        Parallel.For(0, (_free.Length + ParallelBlockSize - 1) / ParallelBlockSize, block =>
        {
            int start = block * ParallelBlockSize;
            int end = Math.Min(start + ParallelBlockSize, _free.Length);
            BuildRightRange(rest, displacement, rotations, right, start, end);
        });
    }

    private void BuildRightRange(Vector2[] rest, Vector2[] displacement, Vector2[] rotations, Vector2[] right, int start, int end)
    {
        // Solve for displacements from rest so small moves on a large image retain their precision.
        foreach (int vertex in _free.AsSpan(start, end - start))
        {
            Vector2 restVertex = rest[vertex];
            Vector2 rotationVertex = rotations[vertex];
            Vector2 value = Vector2.Zero;
            foreach (int other in _neighbors[vertex])
            {
                Vector2 edge = restVertex - rest[other];
                Vector2 rotation = (rotationVertex + rotations[other]) * 0.5f;
                value += new Vector2(rotation.X * edge.X - rotation.Y * edge.Y,
                    rotation.Y * edge.X + rotation.X * edge.Y) - edge;
                if (_pinAtVertex[other] >= 0)
                    value += displacement[other];
            }
            right[vertex] = value;
        }
    }

    // Anderson acceleration of the local-global iteration (Peng et al., "Anderson Acceleration for Geometry Optimization
    // and Physics Simulation", 2018). Where plain iterations creep along a slow bend, it extrapolates from the last few
    // of them. The caller keeps an extrapolated step only when it lowers the energy.
    private sealed class AndersonMixer : IDisposable
    {
        private const int Depth = 5;
        private readonly int _length;
        // Depth changes of the residual, Depth changes of the solved displacement, then the last residual and solve.
        private readonly Vector2[] _history;
        private readonly double[] _gram = new double[Depth * Depth];
        private readonly double[] _factor = new double[Depth * Depth];
        private readonly double[] _weights = new double[Depth];
        private int _count;
        private int _next;
        private bool _started;

        public AndersonMixer(int length)
        {
            _length = length;
            _history = ArrayPool<Vector2>.Shared.Rent(length * (2 * Depth + 2));
        }

        // Records that the global step took the free vertices from displacement to solved, then replaces their
        // displacement with the extrapolated one. Returns false, leaving it alone, while there is nothing to
        // extrapolate from.
        public bool Mix(int[] free, Vector2[] displacement, Vector2[] solved)
        {
            Vector2[] history = _history;
            int length = _length;
            int lastResidual = 2 * Depth * length;
            int lastSolved = lastResidual + length;
            if (!_started)
            {
                for (int i = 0; i < free.Length; i++)
                {
                    int vertex = free[i];
                    history[lastResidual + i] = solved[vertex] - displacement[vertex];
                    history[lastSolved + i] = solved[vertex];
                }
                _started = true;
                return false;
            }

            int residualChange = _next * length;
            int solvedChange = (Depth + _next) * length;
            for (int i = 0; i < free.Length; i++)
            {
                int vertex = free[i];
                Vector2 residual = solved[vertex] - displacement[vertex];
                history[residualChange + i] = residual - history[lastResidual + i];
                history[solvedChange + i] = solved[vertex] - history[lastSolved + i];
                history[lastResidual + i] = residual;
                history[lastSolved + i] = solved[vertex];
            }
            _count = Math.Min(_count + 1, Depth);

            Span<double> products = stackalloc double[Depth];
            Span<double> right = stackalloc double[Depth];
            for (int i = 0; i < free.Length; i++)
            {
                Vector2 change = history[residualChange + i];
                Vector2 residual = history[lastResidual + i];
                for (int slot = 0; slot < _count; slot++)
                {
                    Vector2 other = history[slot * length + i];
                    products[slot] += (double)change.X * other.X + (double)change.Y * other.Y;
                    right[slot] += (double)other.X * residual.X + (double)other.Y * residual.Y;
                }
            }
            for (int slot = 0; slot < _count; slot++)
            {
                _gram[_next * Depth + slot] = products[slot];
                _gram[slot * Depth + _next] = products[slot];
            }
            _next = (_next + 1) % Depth;
            if (!SolveWeights(right[.._count])) return false;

            for (int i = 0; i < free.Length; i++)
            {
                int vertex = free[i];
                double x = solved[vertex].X;
                double y = solved[vertex].Y;
                for (int slot = 0; slot < _count; slot++)
                {
                    Vector2 change = history[(Depth + slot) * length + i];
                    x -= _weights[slot] * change.X;
                    y -= _weights[slot] * change.Y;
                }
                displacement[vertex] = new Vector2((float)x, (float)y);
            }
            return true;
        }

        // The weights of the recorded residual changes that best cancel the last residual, by least squares.
        private bool SolveWeights(ReadOnlySpan<double> right)
        {
            int count = right.Length;
            double scale = 0;
            for (int slot = 0; slot < count; slot++)
                scale = Math.Max(scale, _gram[slot * Depth + slot]);
            if (!(scale > 0)) return false;

            // Cholesky with a small ridge, which keeps nearly parallel changes from blowing the weights up.
            for (int row = 0; row < count; row++)
            {
                for (int column = 0; column <= row; column++)
                {
                    double sum = _gram[row * Depth + column];
                    for (int k = 0; k < column; k++)
                        sum -= _factor[row * Depth + k] * _factor[column * Depth + k];
                    if (row != column)
                    {
                        _factor[row * Depth + column] = sum / _factor[column * Depth + column];
                    }
                    else
                    {
                        sum += scale * 1e-10;
                        if (!(sum > 0)) return false;
                        _factor[row * Depth + row] = Math.Sqrt(sum);
                    }
                }
            }

            for (int row = 0; row < count; row++)
            {
                double sum = right[row];
                for (int k = 0; k < row; k++)
                    sum -= _factor[row * Depth + k] * _weights[k];
                _weights[row] = sum / _factor[row * Depth + row];
            }
            for (int row = count - 1; row >= 0; row--)
            {
                double sum = _weights[row];
                for (int k = row + 1; k < count; k++)
                    sum -= _factor[k * Depth + row] * _weights[k];
                _weights[row] = sum / _factor[row * Depth + row];
            }
            return true;
        }

        public void Dispose() => ArrayPool<Vector2>.Shared.Return(_history);
    }

    private readonly record struct Vector2D(double X, double Y)
    {
        public static explicit operator Vector2D(Vector2 value) => new(value.X, value.Y);

        public static Vector2D operator +(Vector2D left, Vector2D right) => new(left.X + right.X, left.Y + right.Y);

        public static Vector2D operator -(Vector2D left, Vector2D right) => new(left.X - right.X, left.Y - right.Y);

        public static Vector2D operator /(Vector2D value, double divisor) => new(value.X / divisor, value.Y / divisor);
    }

    // The anchored Laplacian is positive definite and stays fixed while pins move. Factor it once in double
    // precision. Eliminating low-degree vertices first keeps the new nonzero entries sparse.
    private sealed class LaplacianFactor
    {
        private readonly Column[] _columns;

        public LaplacianFactor(int[][] neighbors, int[] free, int[] pinAtVertex)
        {
            _columns = new Column[free.Length];
            if (free.Length == 0) return;

            var right = new Dictionary<int, double>?[neighbors.Length];
            var diagonal = new double[neighbors.Length];
            var queue = new PriorityQueue<(int Degree, int Vertex), (int Degree, int Vertex)>();
            foreach (int vertex in free)
            {
                var entries = new Dictionary<int, double>();
                foreach (int other in neighbors[vertex])
                {
                    if (pinAtVertex[other] < 0)
                        entries.Add(other, -1);
                }
                right[vertex] = entries;
                diagonal[vertex] = neighbors[vertex].Length;
                Enqueue(vertex, entries.Count);
            }

            for (int columnIndex = 0; columnIndex < _columns.Length; columnIndex++)
            {
                int vertex;
                int degree;
                Dictionary<int, double>? entries;
                do
                {
                    (degree, vertex) = queue.Dequeue();
                    entries = right[vertex];
                } while (entries is null || entries.Count != degree);

                double inverse = 1 / Math.Sqrt(diagonal[vertex]);
                int[] rows = entries.Keys.Order().ToArray();
                var weights = new double[rows.Length];
                for (int i = 0; i < rows.Length; i++)
                    weights[i] = entries[rows[i]] * inverse;

                for (int i = 0; i < rows.Length; i++)
                {
                    int row = rows[i];
                    Dictionary<int, double> rowEntries = right[row]!;
                    rowEntries.Remove(vertex);
                    diagonal[row] -= weights[i] * weights[i];
                    for (int j = i + 1; j < rows.Length; j++)
                    {
                        int other = rows[j];
                        double next = rowEntries.GetValueOrDefault(other) - weights[i] * weights[j];
                        rowEntries[other] = next;
                        right[other]![row] = next;
                    }
                    Enqueue(row, rowEntries.Count);
                }

                right[vertex] = null;
                _columns[columnIndex] = new Column(vertex, (float)inverse, rows,
                    Array.ConvertAll(weights, static value => (float)value));
            }

            void Enqueue(int vertex, int degree)
            {
                var entry = (degree, vertex);
                queue.Enqueue(entry, entry);
            }
        }

        public void Solve(Vector2[] right, Vector2[] result)
        {
            foreach (Column column in _columns)
            {
                Vector2 value = right[column.Vertex] * column.InverseDiagonal;
                right[column.Vertex] = value;
                for (int i = 0; i < column.Rows.Length; i++)
                    right[column.Rows[i]] -= value * column.Weights[i];
            }

            for (int index = _columns.Length - 1; index >= 0; index--)
            {
                Column column = _columns[index];
                Vector2 value = right[column.Vertex];
                for (int i = 0; i < column.Rows.Length; i++)
                    value -= result[column.Rows[i]] * column.Weights[i];
                result[column.Vertex] = value * column.InverseDiagonal;
            }
        }

        private readonly record struct Column(int Vertex, float InverseDiagonal, int[] Rows, float[] Weights);
    }

    private static int[][] BuildNeighbors(PuppetMesh mesh)
    {
        var neighbors = new HashSet<int>[mesh.Rest.Length];
        for (int vertex = 0; vertex < neighbors.Length; vertex++)
            neighbors[vertex] = [];
        ushort[] indices = mesh.Indices;
        for (int index = 0; index < indices.Length; index += 3)
        {
            int a = indices[index];
            int b = indices[index + 1];
            int c = indices[index + 2];
            AddEdge(a, b);
            AddEdge(b, c);
            AddEdge(c, a);
        }

        return neighbors.Select(list => list.Order().ToArray()).ToArray();

        void AddEdge(int a, int b)
        {
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }
    }

    private static int[] FindComponents(int[][] neighbors, out int count)
    {
        var components = new int[neighbors.Length];
        Array.Fill(components, -1);
        var pending = new Stack<int>();
        count = 0;
        for (int start = 0; start < components.Length; start++)
        {
            if (components[start] >= 0) continue;

            components[start] = count;
            pending.Push(start);
            while (pending.TryPop(out int vertex))
            {
                foreach (int other in neighbors[vertex])
                {
                    if (components[other] >= 0) continue;

                    components[other] = count;
                    pending.Push(other);
                }
            }
            count++;
        }
        return components;
    }
}

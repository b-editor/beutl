using System.Numerics;

namespace Beutl.Graphics.Effects;

// Two-dimensional ARAP with uniform edge weights and exact positional constraints.
internal sealed class PuppetDeformer
{
    private const int RotationIterations = 6;
    private readonly PuppetMesh _mesh;
    private readonly int[][] _neighbors;
    private readonly int[] _pinAtVertex;
    private readonly int[] _component;
    private readonly int[] _componentPinCount;
    private readonly int[] _rotating;
    private readonly int[] _free;
    private readonly LaplacianFactor _laplacian;

    public PuppetDeformer(PuppetMesh mesh)
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

        _rotating = Enumerable.Range(0, mesh.Rest.Length)
            .Where(vertex => _componentPinCount[_component[vertex]] >= 2).ToArray();
        _free = _rotating.Where(vertex => _pinAtVertex[vertex] < 0).ToArray();
        _laplacian = new LaplacianFactor(_neighbors, _free, _pinAtVertex);
    }

    public Vector2[] Deform(Size size, Point[] targets)
    {
        Vector2[] rest = _mesh.Rest;
        var displacement = new Vector2[rest.Length];
        var componentDisplacement = new Vector2[_componentPinCount.Length];
        bool moved = false;
        for (int vertex = 0; vertex < rest.Length; vertex++)
        {
            if (_pinAtVertex[vertex] < 0) continue;

            Vector2 target = PuppetMesh.ToLocal(size, targets[_pinAtVertex[vertex]]);
            if (!float.IsFinite(target.X) || !float.IsFinite(target.Y)) continue;

            Vector2 delta = target - rest[vertex];
            displacement[vertex] = delta;
            componentDisplacement[_component[vertex]] += delta;
            moved |= delta != Vector2.Zero;
        }
        if (!moved) return rest;

        // A component with one pin translates exactly. Starting other components from their mean translation makes
        // every solve independent of playback order while leaving them free to find a rotation.
        for (int vertex = 0; vertex < displacement.Length; vertex++)
        {
            int component = _component[vertex];
            if (_pinAtVertex[vertex] < 0 && _componentPinCount[component] > 0)
                displacement[vertex] = componentDisplacement[component] / _componentPinCount[component];
        }

        if (_free.Length > 0)
        {
            var rotations = new Vector2[rest.Length];
            var right = new Vector2[rest.Length];
            for (int iteration = 0; iteration < RotationIterations; iteration++)
            {
                foreach (int vertex in _rotating)
                {
                    double cosine = 0;
                    double sine = 0;
                    foreach (int other in _neighbors[vertex])
                    {
                        Vector2 original = rest[vertex] - rest[other];
                        Vector2 current = original + displacement[vertex] - displacement[other];
                        cosine += (double)original.X * current.X + (double)original.Y * current.Y;
                        sine += (double)original.X * current.Y - (double)original.Y * current.X;
                    }

                    double length = Math.Sqrt(cosine * cosine + sine * sine);
                    rotations[vertex] = length > 0
                        ? new Vector2((float)(cosine / length), (float)(sine / length))
                        : Vector2.UnitX;
                }

                // Solve for displacements from rest so small moves on a large image retain their precision.
                foreach (int vertex in _free)
                {
                    Vector2 value = Vector2.Zero;
                    foreach (int other in _neighbors[vertex])
                    {
                        Vector2 edge = rest[vertex] - rest[other];
                        Vector2 rotation = (rotations[vertex] + rotations[other]) * 0.5f;
                        value += new Vector2(rotation.X * edge.X - rotation.Y * edge.Y,
                            rotation.Y * edge.X + rotation.X * edge.Y) - edge;
                        if (_pinAtVertex[other] >= 0)
                            value += displacement[other];
                    }
                    right[vertex] = value;
                }
                _laplacian.Solve(right, displacement);
            }
        }

        for (int vertex = 0; vertex < rest.Length; vertex++)
            displacement[vertex] += rest[vertex];
        for (int vertex = 0; vertex < rest.Length; vertex++)
        {
            if (_pinAtVertex[vertex] < 0) continue;

            Vector2 target = PuppetMesh.ToLocal(size, targets[_pinAtVertex[vertex]]);
            if (float.IsFinite(target.X) && float.IsFinite(target.Y))
                displacement[vertex] = target;
        }
        return displacement;
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

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// A stable observation sink a state-passing render callback can carry as part of its identity.
/// </summary>
/// <remarks>
/// A test that counts executions cannot capture a local or a node field: the callback must not capture, and a
/// render node is disposable and so cannot be an identity. One probe instance per node keeps the identity
/// stable across frames while still letting the callback record what happened.
/// </remarks>
internal sealed class ExecutionProbe
{
    public int Count { get; private set; }

    public void Record() => Count++;
}

/// <summary>The same sink for a callback that observes the live execution session.</summary>
internal sealed class SessionProbe<TSession>(Action<TSession>? observe)
{
    public void Observe(TSession session) => observe?.Invoke(session);
}

/// <summary>The same sink for a callback that records a value rather than only that it ran.</summary>
internal sealed class RecordingProbe<T>
{
    private readonly List<T> _records = [];

    public IReadOnlyList<T> Records => _records;

    public void Record(T value) => _records.Add(value);
}

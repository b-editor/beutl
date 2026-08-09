namespace Beutl.Extensibility;

/// <summary>
/// Coordinates output work with host operations that can replace or mutate the output workspace.
/// </summary>
public interface IOutputOperationLeaseProvider
{
    /// <summary>
    /// Attempts to begin an output operation in the current host.
    /// </summary>
    /// <returns>
    /// A lease that must be held until the output operation completes, or <see langword="null"/>
    /// when the host cannot safely start output work.
    /// </returns>
    IDisposable? TryBeginOutputOperation();
}

/// <summary>
/// Allows output operations when no editor workspace coordinator is present.
/// </summary>
public sealed class StandaloneOutputOperationLeaseProvider : IOutputOperationLeaseProvider
{
    /// <summary>
    /// Gets the shared standalone provider.
    /// </summary>
    public static StandaloneOutputOperationLeaseProvider Instance { get; } = new();

    private StandaloneOutputOperationLeaseProvider()
    {
    }

    /// <inheritdoc />
    public IDisposable TryBeginOutputOperation()
    {
        return NoOpLease.Instance;
    }

    private sealed class NoOpLease : IDisposable
    {
        public static NoOpLease Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

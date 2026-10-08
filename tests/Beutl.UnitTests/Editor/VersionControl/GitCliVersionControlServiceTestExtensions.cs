using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

internal static class GitCliVersionControlServiceTestExtensions
{
    public static Task<TResult> ExecuteExclusiveAsync<TResult>(
        this GitCliVersionControlService service,
        Func<IProjectVersionControlTransaction, Task<TResult>> operation,
        CancellationToken cancellationToken)
        => ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(operation, cancellationToken);

    public static Task ExecuteExclusiveAsync(
        this GitCliVersionControlService service,
        Func<IProjectVersionControlTransaction, Task> operation,
        CancellationToken cancellationToken)
        => ((IProjectVersionControlBackend)service).ExecuteExclusiveAsync(
            async transaction =>
            {
                await operation(transaction);
                return true;
            },
            cancellationToken);
}

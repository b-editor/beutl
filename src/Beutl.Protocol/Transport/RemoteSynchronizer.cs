using System.Reactive.Linq;

namespace Beutl.Protocol.Transport;

/// <summary>
/// Integrates local synchronizers with remote transport layer.
/// Sends local operations to remote peers and applies remote operations locally.
/// </summary>
public class RemoteSynchronizer : IDisposable
{
    private readonly ISynchronizer _localSynchronizer;
    private readonly ITransport _transport;
    private readonly OperationExecutor _executor;
    private readonly IDisposable _localSubscription;
    private readonly IDisposable _remoteSubscription;
    private bool _disposed;

    public RemoteSynchronizer(
        ISynchronizer localSynchronizer,
        ITransport transport,
        OperationExecutor executor)
    {
        _localSynchronizer = localSynchronizer ?? throw new ArgumentNullException(nameof(localSynchronizer));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));

        // Subscribe to local operations and send them to remote
        _localSubscription = _localSynchronizer.Operations
            .Subscribe(
                operation => _ = SendOperationAsync(operation),
                error => Console.WriteLine($"Error in local operations: {error.Message}"),
                () => Console.WriteLine("Local operations completed")
            );

        // Subscribe to remote operations and execute them locally
        _remoteSubscription = _transport.IncomingOperations
            .Subscribe(
                operation => ExecuteRemoteOperation(operation),
                error => Console.WriteLine($"Error in remote operations: {error.Message}"),
                () => Console.WriteLine("Remote operations completed")
            );
    }

    private async Task SendOperationAsync(OperationBase operation)
    {
        try
        {
            await _transport.SendOperationAsync(operation);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send operation: {ex.Message}");
            // Consider implementing retry logic or dead-letter queue here
        }
    }

    private void ExecuteRemoteOperation(OperationBase operation)
    {
        try
        {
            // Execute the operation using the executor
            _executor.Execute(operation);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to execute remote operation: {ex.Message}");
            // Consider implementing conflict resolution here
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _localSubscription?.Dispose();
        _remoteSubscription?.Dispose();
        
        GC.SuppressFinalize(this);
    }
}

using System.Reactive.Linq;
using Beutl.Protocol.Operations;
using Beutl.Protocol.Synchronization;

namespace Beutl.Protocol.Transport;

/// <summary>
/// Integrates local synchronizers with remote transport layer.
/// Sends local operations to remote peers and applies remote operations locally.
/// </summary>
public class RemoteSynchronizer : IDisposable
{
    private readonly IOperationPublisher _localPublisher;
    private readonly ITransport _transport;
    private readonly OperationApplier _applier;
    private readonly IDisposable _localSubscription;
    private readonly IDisposable _remoteSubscription;
    private bool _disposed;

    public RemoteSynchronizer(
        IOperationPublisher localPublisher,
        ITransport transport,
        OperationApplier applier)
    {
        _localPublisher = localPublisher ?? throw new ArgumentNullException(nameof(localPublisher));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));

        // Subscribe to local operations and send them to remote
        _localSubscription = _localPublisher.Operations
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

    private async Task SendOperationAsync(SyncOperation operation)
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

    private void ExecuteRemoteOperation(SyncOperation operation)
    {
        try
        {
            // Execute the operation using the executor
            _applier.Apply(operation);
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

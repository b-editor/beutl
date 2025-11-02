using Beutl.Protocol.Operations;

namespace Beutl.Protocol.Synchronization;

public interface IOperationPublisher : IDisposable
{
    IObservable<SyncOperation> Operations { get; }
}

namespace Beutl.Protocol;

public interface ISynchronizer : IDisposable
{
    IObservable<OperationBase> Operations { get; }
}

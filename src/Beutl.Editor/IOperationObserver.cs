namespace Beutl.Editor;

public interface IOperationObserver : IDisposable
{
    IObservable<ChangeOperation> Operations { get; }
}

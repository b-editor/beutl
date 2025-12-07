using Beutl.Editor.Operations;

namespace Beutl.Editor.Observers;

public interface IOperationObserver : IDisposable
{
    IObservable<ChangeOperation> Operations { get; }
}

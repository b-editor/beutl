using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Beutl.Api.Services;

internal sealed class AiJobChangeNotifier : IBeutlApiResource, IDisposable
{
    private readonly Subject<Unit> _source = new();
    private readonly object _gate = new();
    private bool _disposed;

    public IObservable<Unit> Changes => _source.AsObservable();

    public void Notify()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _source.OnNext(Unit.Default);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _source.OnCompleted();
            _source.Dispose();
        }
    }
}

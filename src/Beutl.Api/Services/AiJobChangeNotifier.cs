using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Beutl.Api.Services;

internal sealed class AiJobChangeNotifier : IBeutlApiResource, IDisposable
{
    private readonly Subject<Unit> _source = new();
    private readonly ISubject<Unit> _serialized;
    private readonly IObservable<Unit> _changes;
    private readonly object _gate = new();
    private bool _disposed;

    public AiJobChangeNotifier()
    {
        _serialized = Subject.Synchronize(_source);
        _changes = _serialized.AsObservable();
    }

    public IObservable<Unit> Changes => _changes;

    public void Notify()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _serialized.OnNext(Unit.Default);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _serialized.OnCompleted();
            _source.Dispose();
        }
    }
}

using System.Reactive.Disposables;

namespace Beutl.Reactive;

public abstract class LightweightObservableBase<T> : IObservable<T>
{
    private Exception? _error;
    private List<IObserver<T>>? _observers = [];

    public IDisposable Subscribe(IObserver<T> observer)
    {
        _ = observer ?? throw new ArgumentNullException(nameof(observer));

        bool first = false;

        for (; ; )
        {
            if (Volatile.Read(ref _observers) == null)
            {
                if (_error != null)
                {
                    observer.OnError(_error);
                }
                else
                {
                    observer.OnCompleted();
                }

                return Disposable.Empty;
            }

            lock (this)
            {
                if (_observers == null)
                {
                    continue;
                }

                first = _observers.Count == 0;
                _observers.Add(observer);
                break;
            }
        }

        if (first)
        {
            Initialize();
        }

        Subscribed(observer, first);

        return new RemoveObserver(this, observer);
    }

    void Remove(IObserver<T> observer)
    {
        if (Volatile.Read(ref _observers) != null)
        {
            lock (this)
            {
                List<IObserver<T>>? observers = _observers;

                if (observers != null)
                {
                    observers.Remove(observer);

                    if (observers.Count == 0)
                    {
                        observers.TrimExcess();
                        Deinitialize();
                    }
                }
            }
        }
    }

    sealed class RemoveObserver : IDisposable
    {
        LightweightObservableBase<T>? _parent;

        IObserver<T>? _observer;

        public RemoveObserver(LightweightObservableBase<T> parent, IObserver<T> observer)
        {
            _parent = parent;
            Volatile.Write(ref _observer, observer);
        }

        public void Dispose()
        {
            IObserver<T>? observer = _observer;
            Interlocked.Exchange(ref _parent, null)?.Remove(observer!);
            _observer = null;
        }
    }

    protected abstract void Initialize();
    protected abstract void Deinitialize();

    protected void PublishNext(T value)
    {
        if (Volatile.Read(ref _observers) != null)
        {
            IObserver<T>[]? observers = null;
            IObserver<T>? singleObserver = null;
            lock (this)
            {
                if (_observers == null)
                {
                    return;
                }
                if (_observers.Count == 1)
                {
                    singleObserver = _observers[0];
                }
                else
                {
                    observers = [.. _observers];
                }
            }
            if (singleObserver != null)
            {
                singleObserver.OnNext(value);
            }
            else
            {
                foreach (IObserver<T>? observer in observers!)
                {
                    observer.OnNext(value);
                }
            }
        }
    }

    protected void PublishCompleted()
    {
        if (Volatile.Read(ref _observers) != null)
        {
            IObserver<T>[] observers;

            lock (this)
            {
                if (_observers == null)
                {
                    return;
                }
                observers = [.. _observers];
                Volatile.Write(ref _observers, null);
            }

            foreach (IObserver<T>? observer in observers)
            {
                observer.OnCompleted();
            }

            Deinitialize();
        }
    }

    protected void PublishError(Exception error)
    {
        if (Volatile.Read(ref _observers) != null)
        {

            IObserver<T>[] observers;

            lock (this)
            {
                if (_observers == null)
                {
                    return;
                }

                _error = error;
                observers = [.. _observers];
                Volatile.Write(ref _observers, null);
            }

            foreach (IObserver<T>? observer in observers)
            {
                observer.OnError(error);
            }

            Deinitialize();
        }
    }

    protected virtual void Subscribed(IObserver<T> observer, bool first)
    {
    }
}

using System.Collections;

namespace Beutl;

internal sealed class OnceEnumerable<T> : IEnumerable<T>
{
    private readonly T _value;

    public OnceEnumerable(T value)
    {
        _value = value;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return new Enumerator(_value);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new Enumerator(_value);
    }

    private sealed class Enumerator : IEnumerator<T>
    {
        private bool _flag;

        public Enumerator(T value)
        {
            Current = value;
        }

        public T Current { get; }

        object IEnumerator.Current => Current!;

        public void Dispose()
        {
            _flag = true;
        }

        public bool MoveNext()
        {
            if (_flag)
            {
                _flag = false;
                return true;
            }
            else
            {
                return false;
            }
        }

        public void Reset()
        {
            _flag = true;
        }
    }
}

using System.Collections;

namespace Beutl;

internal sealed class OnceEnumerable<T>(T value) : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator()
    {
        return new Enumerator(value);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new Enumerator(value);
    }

    private sealed class Enumerator(T value) : IEnumerator<T>
    {
        private bool _flag;

        public T Current { get; } = value;

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

//#define USE_ARRAY_POOL

using System.Buffers;
using System.Collections;

using BeUtl.Media.TextFormatting;

namespace BeUtl.Graphics.Shapes;

public class TextElements : IReadOnlyList<TextElement_>
{
    private readonly TextElement_[] _array;

    public TextElements(IEnumerable<TextElement_> items)
        : this(items.ToArray())
    {
    }

    internal TextElements(TextElement_[] array)
    {
        _array = array;
        Lines = new LineEnumerable(array);
    }

    public TextElement_ this[int index] => ((IReadOnlyList<TextElement_>)_array)[index];

    public int Count => ((IReadOnlyCollection<TextElement_>)_array).Count;

    public LineEnumerable Lines { get; }

    public IEnumerator<TextElement_> GetEnumerator()
    {
        return ((IEnumerable<TextElement_>)_array).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _array.GetEnumerator();
    }

    public readonly struct LineEnumerable
    {
        private readonly TextElement_[] _array;

        internal LineEnumerable(TextElement_[] array)
        {
            _array = array;
        }

        public LineEnumerator GetEnumerator()
        {
            int count = 0;
            foreach (TextElement_ item in _array)
            {
                count += item.CountLines();
            }

            FormattedText_[] buffer =
#if USE_ARRAY_POOL
                ArrayPool<FormattedText_>.Shared.Rent(count)
#else
                new FormattedText_[count]
#endif
                ;
            Span<FormattedText_> span = buffer;
            bool startWithNewLine = false;

            foreach (TextElement_ item in _array)
            {
                int ct = item.GetFormattedTexts(span, startWithNewLine, out startWithNewLine);
                span = span.Slice(ct);
            }

            return new LineEnumerator(buffer, count);
        }
    }

    // 内部の一時バッファにArrayPoolを使う。
    public struct LineEnumerator : IDisposable
    {
        private readonly int _arrayCount;
        private FormattedText_[]? _array;
        private int _index = 0;
        private int _count = 0;
        private int _prevIndex = 0;

        internal LineEnumerator(FormattedText_[] array, int count)
        {
            _array = array;
            _arrayCount = count;
        }

        public Span<FormattedText_> Current => _array.AsSpan().Slice(_index, _count);

        public bool MoveNext()
        {
            if (_array == null)
            {
                throw new ObjectDisposedException("LineEnumerator");
            }

            int index = _prevIndex + 1;
            _index = _prevIndex;

            while (index < _arrayCount)
            {
                ref FormattedText_ item = ref _array[index];
                if (item.BeginOnNewLine || index + 1 >= _arrayCount)
                {
                    break;
                }
                else
                {
                    index++;
                }
            }

            _count = index - _index;
            _prevIndex = index;

            return index < _arrayCount;
        }

        public void Reset()
        {
            _index = 0;
            _count = 0;
        }

        public void Dispose()
        {
#if USE_ARRAY_POOL

            if (_array != null)
            {
                ArrayPool<FormattedText_>.Shared.Return(_array);
            }
#endif
            _array = null;
        }
    }
}

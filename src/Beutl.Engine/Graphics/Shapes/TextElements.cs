using System.Buffers;
using System.Collections;

using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Shapes;

public class TextElements : IReadOnlyList<TextElement>, IAffectsRender
{
    private readonly TextElement[] _array;

    public TextElements(IEnumerable<TextElement> items)
        : this(items.ToArray())
    {
    }

    internal TextElements(TextElement[] array)
    {
        _array = array;
        Lines = new LineEnumerable(array);

        foreach (TextElement item in array)
        {
            item.Invalidated += OnItemInvalidated;
        }
    }

    private void OnItemInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(sender, e);
    }

    public TextElement this[int index] => ((IReadOnlyList<TextElement>)_array)[index];

    public int Count => ((IReadOnlyCollection<TextElement>)_array).Count;

    public LineEnumerable Lines { get; }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public IEnumerator<TextElement> GetEnumerator()
    {
        return ((IEnumerable<TextElement>)_array).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _array.GetEnumerator();
    }

    public class LineEnumerable
    {
        private readonly TextElement[] _array;
        private FormattedText[]? _formattedTexts;

        internal LineEnumerable(TextElement[] array)
        {
            _array = array;
        }

        public LineEnumerator GetEnumerator()
        {
            int count = 0;
            foreach (TextElement item in _array)
            {
                count += item.CountElements();
            }

            if (_formattedTexts?.Length != count)
            {
                _formattedTexts = new FormattedText[count];
                foreach (ref FormattedText item in _formattedTexts.AsSpan())
                {
                    item = new FormattedText();
                }
            }

            Span<FormattedText> span = _formattedTexts;
            bool startWithNewLine = false;

            foreach (TextElement item in _array)
            {
                int ct = item.GetFormattedTexts(span, startWithNewLine, out startWithNewLine);
                span = span.Slice(ct);
            }

            return new LineEnumerator(_formattedTexts, count);
        }
    }

    // 内部の一時バッファにArrayPoolを使う。
    public struct LineEnumerator : IDisposable
    {
        private readonly int _arrayCount;
        private FormattedText[]? _array;
        private int _index = 0;
        private int _count = 0;
        private int _prevIndex = 0;

        internal LineEnumerator(FormattedText[] array, int count)
        {
            _array = array;
            _arrayCount = count;
        }

        public readonly Span<FormattedText> Current => _array.AsSpan().Slice(_index, _count);

        public bool MoveNext()
        {
            ObjectDisposedException.ThrowIf(_array == null, this);

            int index = _prevIndex + 1;
            _index = _prevIndex;

            while (index < _arrayCount)
            {
                FormattedText item = _array[index];
                if (item.BeginOnNewLine/* || index + 1 >= _arrayCount*/)
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

            return _index < _arrayCount;
        }

        public void Reset()
        {
            _index = 0;
            _count = 0;
        }

        public void Dispose()
        {
            _array = null;
        }
    }
}

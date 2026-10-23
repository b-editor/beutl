using System.Collections;

using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Shapes;

public class TextElements : IReadOnlyList<TextElement>, IDisposable
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
    }

    public TextElement this[int index] => ((IReadOnlyList<TextElement>)_array)[index];

    public int Count => ((IReadOnlyCollection<TextElement>)_array).Count;

    public LineEnumerable Lines { get; }

    public bool IsDisposed { get; private set; }

    /// <remarks>
    /// Idempotent and one-shot; this instance and its <see cref="Lines"/> must not be used after disposal.
    /// </remarks>
    public void Dispose()
    {
        if (IsDisposed) return;
        Lines.Dispose();
        IsDisposed = true;
    }

    public IEnumerator<TextElement> GetEnumerator()
    {
        return ((IEnumerable<TextElement>)_array).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _array.GetEnumerator();
    }

    public class LineEnumerable : IDisposable
    {
        private readonly TextElement[] _array;
        private FormattedText[]? _formattedTexts;

        internal LineEnumerable(TextElement[] array)
        {
            _array = array;
        }

        public bool IsDisposed { get; private set; }

        /// <remarks>
        /// Idempotent and one-shot; do not enumerate after disposal. <see cref="GetEnumerator"/> throws
        /// <see cref="ObjectDisposedException"/> rather than re-allocating <see cref="FormattedText"/> instances
        /// that a later <see cref="Dispose"/> could not release.
        /// </remarks>
        public void Dispose()
        {
            if (IsDisposed) return;
            if (_formattedTexts is { } texts)
            {
                foreach (FormattedText item in texts)
                {
                    item.Dispose();
                }
            }

            _formattedTexts = null;
            IsDisposed = true;
        }

        public LineEnumerator GetEnumerator()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_formattedTexts is null)
            {
                int count = 0;
                foreach (TextElement item in _array)
                {
                    count += item.CountElements();
                }

                var formattedTexts = new FormattedText[count];
                foreach (ref FormattedText item in formattedTexts.AsSpan())
                {
                    item = new FormattedText();
                }

                Span<FormattedText> span = formattedTexts;
                bool startWithNewLine = false;
                foreach (TextElement item in _array)
                {
                    int ct = item.GetFormattedTexts(span, startWithNewLine, out startWithNewLine);
                    span = span.Slice(ct);
                }

                // Later element changes require a new TextElements instance.
                _formattedTexts = formattedTexts;
            }

            return new LineEnumerator(_formattedTexts, _formattedTexts.Length);
        }
    }

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
                if (item.BeginOnNewLine)
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

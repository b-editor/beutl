#define USE_ARRAY_POOL

using System.Buffers;
using System.Collections;
using System.Text.Json.Nodes;

using BeUtl.Media.TextFormatting;

namespace BeUtl.Graphics.Shapes;

public class TextElements : IReadOnlyList<TextElement>, ILogicalElement
{
    private readonly TextElement[] _array;
    private ILogicalElement? _parent;

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

    public static TextElements Empty { get; } = new(Array.Empty<TextElement>());

    public int Count => ((IReadOnlyCollection<TextElement>)_array).Count;

    public LineEnumerable Lines { get; }

    ILogicalElement? ILogicalElement.LogicalParent => _parent;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => _array;

    event EventHandler<LogicalTreeAttachmentEventArgs> ILogicalElement.AttachedToLogicalTree
    {
        add { }
        remove { }
    }

    event EventHandler<LogicalTreeAttachmentEventArgs> ILogicalElement.DetachedFromLogicalTree
    {
        add { }
        remove { }
    }

    public IEnumerator<TextElement> GetEnumerator()
    {
        return ((IEnumerable<TextElement>)_array).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _array.GetEnumerator();
    }

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        _parent = e.Parent;
        foreach (TextElement item in _array)
        {
            (item as ILogicalElement).NotifyAttachedToLogicalTree(e);
        }
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        _parent = null;
        foreach (TextElement item in _array)
        {
            (item as ILogicalElement).NotifyDetachedFromLogicalTree(e);
        }
    }

    public readonly struct LineEnumerable
    {
        private readonly TextElement[] _array;

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

            FormattedText[] buffer =
#if USE_ARRAY_POOL
                ArrayPool<FormattedText>.Shared.Rent(count)
#else
                new FormattedText_[count]
#endif
                ;
            Span<FormattedText> span = buffer;
            bool startWithNewLine = false;

            foreach (TextElement item in _array)
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
        private FormattedText[]? _array;
        private int _index = 0;
        private int _count = 0;
        private int _prevIndex = 0;

        internal LineEnumerator(FormattedText[] array, int count)
        {
            _array = array;
            _arrayCount = count;
        }

        public Span<FormattedText> Current => _array.AsSpan().Slice(_index, _count);

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
                ref FormattedText item = ref _array[index];
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
#if USE_ARRAY_POOL

            if (_array != null)
            {
                ArrayPool<FormattedText>.Shared.Return(_array);
            }
#endif
            _array = null;
        }
    }
}

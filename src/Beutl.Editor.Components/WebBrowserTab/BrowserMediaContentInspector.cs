using System.Text;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed class BrowserMediaContentInspector
{
    private static readonly string[] s_htmlTags =
        ["<!doctype html", "<html", "<head", "<body", "<script", "<iframe", "<title", "<div", "<h1", "<table", "<p", "<font", "<a", "<style", "<b", "<br", "<form", "<meta"];
    private readonly Encoding _encoding;
    private readonly Decoder _decoder;
    private char[] _characters = [];
    private string _pending = string.Empty;
    private State _state;
    private bool _sawComment;

    internal BrowserMediaContentInspector(ReadOnlySpan<byte> prefix, bool isFinal)
    {
        _encoding = Encoding.UTF8;
        if (prefix.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) _encoding = Encoding.UTF32;
        else if (prefix.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) _encoding = new UTF32Encoding(true, true);
        else if (prefix.StartsWith(new byte[] { 0xff, 0xfe })) _encoding = Encoding.Unicode;
        else if (prefix.StartsWith(new byte[] { 0xfe, 0xff })) _encoding = Encoding.BigEndianUnicode;
        _decoder = _encoding.GetDecoder();
        Inspect(prefix, isFinal);
    }

    internal bool IsHtml { get; private set; }

    internal void Inspect(ReadOnlySpan<byte> bytes, bool isFinal = false)
    {
        if (_state == State.Finished) return;
        int capacity = _encoding.GetMaxCharCount(bytes.Length);
        if (_characters.Length < capacity) _characters = new char[capacity];
        int count = _decoder.GetChars(bytes, _characters, isFinal);
        _pending += new string(_characters, 0, count);

        while (true)
        {
            if (_state is State.Comment or State.Instruction)
            {
                string delimiter = _state == State.Comment ? "-->" : "?>";
                int end = _pending.IndexOf(delimiter, StringComparison.Ordinal);
                if (end < 0)
                {
                    if (isFinal) Finish(_state == State.Comment);
                    else
                    {
                        // Only a delimiter's overlapping suffix is needed across reads.
                        // Long comments and declarations never accumulate in memory.
                        int keep = Math.Min(_pending.Length, delimiter.Length - 1);
                        _pending = _pending[^keep..];
                    }
                    return;
                }
                _pending = _pending[(end + delimiter.Length)..];
                _state = State.Preamble;
                continue;
            }

            _pending = _pending.TrimStart('\uFEFF').TrimStart();
            if (_pending.Length == 0)
            {
                if (isFinal) Finish(_sawComment);
                return;
            }
            if (_pending.StartsWith("<!--", StringComparison.Ordinal))
            {
                _sawComment = true;
                _state = State.Comment;
                _pending = _pending[4..];
                continue;
            }
            if (_pending.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                _state = State.Instruction;
                _pending = _pending[5..];
                continue;
            }
            if (!isFinal && ("<!--".StartsWith(_pending, StringComparison.Ordinal)
                || "<?xml".StartsWith(_pending, StringComparison.OrdinalIgnoreCase))) return;

            foreach (string tag in s_htmlTags)
            {
                if (_pending.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
                {
                    if (_pending.Length == tag.Length && !isFinal) return;
                    if (_pending.Length == tag.Length || char.IsWhiteSpace(_pending[tag.Length]) || _pending[tag.Length] is '>' or '/')
                    {
                        Finish(true);
                        return;
                    }
                }
                else if (!isFinal && tag.StartsWith(_pending, StringComparison.OrdinalIgnoreCase)) return;
            }
            Finish(false);
            return;
        }
    }

    private void Finish(bool isHtml)
    {
        IsHtml = isHtml;
        _state = State.Finished;
        _pending = string.Empty;
        _characters = [];
    }

    private enum State { Preamble, Comment, Instruction, Finished }
}

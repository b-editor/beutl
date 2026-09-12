using System.Text;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed class BrowserMediaContentInspector
{
    private static readonly string[] s_htmlTags =
    [
        "<!doctype html", "<html", "<head", "<body", "<base", "<link", "<meta", "<style", "<title",
        "<address", "<article", "<aside", "<footer", "<header", "<hgroup", "<h1", "<h2", "<h3", "<h4", "<h5", "<h6",
        "<main", "<nav", "<search", "<section", "<blockquote", "<dd", "<div", "<dl", "<dt", "<figcaption", "<figure",
        "<hr", "<li", "<menu", "<ol", "<p", "<pre", "<ul", "<a", "<abbr", "<b", "<bdi", "<bdo", "<br", "<cite",
        "<code", "<data", "<dfn", "<em", "<i", "<kbd", "<mark", "<q", "<rp", "<rt", "<ruby", "<s", "<samp",
        "<small", "<span", "<strong", "<sub", "<sup", "<time", "<u", "<var", "<wbr", "<area", "<audio", "<img",
        "<map", "<track", "<video", "<embed", "<iframe", "<object", "<picture", "<source", "<canvas", "<noscript",
        "<script", "<del", "<ins", "<caption", "<col", "<colgroup", "<table", "<tbody", "<td", "<tfoot", "<th",
        "<thead", "<tr", "<button", "<datalist", "<fieldset", "<form", "<input", "<label", "<legend", "<meter",
        "<optgroup", "<option", "<output", "<progress", "<select", "<textarea", "<details", "<dialog", "<summary",
        "<slot", "<template", "<font"
    ];
    private readonly Encoding _encoding;
    private readonly Decoder _decoder;
    private readonly BrowserMediaContentInspector? _utf8Fallback;
    private char[] _characters = [];
    private string _pending = string.Empty;
    private State _state;
    private bool _sawComment;
    private bool _isHtml;

    internal BrowserMediaContentInspector(ReadOnlySpan<byte> prefix, bool isFinal, string? charset)
    {
        Encoding? bomEncoding = GetBomEncoding(prefix);
        _encoding = bomEncoding ?? GetDeclaredEncoding(charset) ?? Encoding.UTF8;
        _decoder = _encoding.GetDecoder();
        // An incorrect charset must not disable the existing UTF-8/ASCII HTML check.
        // BOMs are authoritative; otherwise both interpretations remain bounded streaming probes.
        if (bomEncoding == null && _encoding.CodePage != Encoding.UTF8.CodePage)
            _utf8Fallback = new BrowserMediaContentInspector(Encoding.UTF8);
        Inspect(prefix, isFinal);
    }

    private BrowserMediaContentInspector(Encoding encoding)
    {
        _encoding = encoding;
        _decoder = encoding.GetDecoder();
    }

    private static Encoding? GetBomEncoding(ReadOnlySpan<byte> prefix)
    {
        if (prefix.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return Encoding.UTF32;
        if (prefix.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return new UTF32Encoding(true, true);
        if (prefix.StartsWith(new byte[] { 0xff, 0xfe })) return Encoding.Unicode;
        if (prefix.StartsWith(new byte[] { 0xfe, 0xff })) return Encoding.BigEndianUnicode;
        if (prefix.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return Encoding.UTF8;
        return null;
    }

    private static Encoding? GetDeclaredEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return null;
        try { return Encoding.GetEncoding(charset.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return null; }
    }

    internal bool IsHtml => _isHtml || _utf8Fallback?.IsHtml == true;

    internal void Inspect(ReadOnlySpan<byte> bytes, bool isFinal = false)
    {
        _utf8Fallback?.Inspect(bytes, isFinal);
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
        _isHtml = isHtml;
        _state = State.Finished;
        _pending = string.Empty;
        _characters = [];
    }

    private enum State { Preamble, Comment, Instruction, Finished }
}

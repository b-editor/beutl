using System.Runtime.InteropServices;
using System.Text;

namespace Beutl.Media.TextFormatting;

public struct FormattedTextTokenizer(string str)
{
    public bool CompatMode { get; set; } = false;

    public int LineCount { get; private set; } = 0;

    public List<Token> Result { get; } = [];

    public void Tokenize()
    {
        int lineCount = CompatMode ? 0 : 1;

        if (!CompatMode)
        {
            Tokenize(new StringSpan(str, 0, str.Length));
        }
        else
        {
            ReadOnlySpan<char> span = str.AsSpan();
            foreach (ReadOnlySpan<char> linesp in span.EnumerateLines())
            {
                int start = span.IndexOf(linesp, StringComparison.Ordinal);
                int len = linesp.Length;

                Tokenize(new StringSpan(str, start, len));

                Result.Add(new Token(StringSpan.Empty, TokenType.NewLine));
                lineCount++;
            }
        }

        LineCount = lineCount;
    }

    public void WriteTo(StringBuilder sb)
    {
        foreach (Token item in CollectionsMarshal.AsSpan(Result))
        {
            switch (item.Type)
            {
                case TokenType.TagStart:
                case TokenType.TagClose:
                case TokenType.Content:
                    sb.Append(item.Text.AsSpan());
                    break;
                case TokenType.NewLine:
                    sb.AppendLine();
                    break;
                default:
                    break;
            }
        }
    }

    private void Process(StringSpan s, int start, int length)
    {
        StringSpan prev = s.Slice(0, start);
        StringSpan tag = s.Slice(start, length);
        StringSpan next = s.Slice(start + length);

        if (prev.Length > 0 && prev.IsValid())
        {
            Tokenize(prev);
        }

        if (tag.Length > 0 && tag.IsValid())
        {
            TokenizeTag(tag);
        }

        if (next.Length > 0 && next.IsValid())
        {
            Tokenize(next);
        }
    }

    // 文字列をトークン化
    // Tokenize -> Process -> Tokenize
    //                     -> TokenizeTag
    //                     -> Tokenize
    private void Tokenize(StringSpan s)
    {
        ReadOnlySpan<char> span = s.AsSpan();
        int tagStart = span.IndexOf("<", StringComparison.Ordinal);
        int tagEnd = span.IndexOf(">", StringComparison.Ordinal);

        bool isMatch = tagStart >= 0 && tagEnd >= 0 &&
            tagStart < tagEnd;

        if (isMatch)
        {
            Process(s, tagStart, tagEnd - tagStart + 1);
        }
        else if (s.Length > 0)
        {
            Result.Add(new Token(s, TokenType.Content));
        }
    }

    // タグをトークンにして追加
    private void TokenizeTag(StringSpan s)
    {
        // TagClose
        if (s.AsSpan().StartsWith("</", StringComparison.Ordinal))
        {
            Result.Add(new Token(s, TokenType.TagClose));
        }
        else
        {
            Result.Add(new Token(s, TokenType.TagStart));
        }
    }

    public record struct Token(StringSpan Text, TokenType Type)
    {
        public override string ToString()
        {
            if (Type == TokenType.NewLine)
            {
                return "newline";
            }
            return Text.AsSpan().ToString();
        }
    }

    public enum TokenType
    {
        TagStart,
        TagClose,
        Content,
        NewLine,
    }
}

using System.Text;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

// The names of a codec, folded once for CodecSearchQuery.
internal sealed class CodecSearchKey(string name, string longName)
{
    public string Name { get; } = CodecSearchQuery.Fold(name);

    public string LongName { get; } = CodecSearchQuery.Fold(longName);
}

// A codec search. Every word of the query has to occur in the codec's name (libx264) or its long name
// (libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10). Case, full and half width, and punctuation are not told
// apart, so "h264" finds "H.264" and "proresks" finds "prores_ks".
internal sealed class CodecSearchQuery
{
    private const int NameScore = 3;
    private const int NamePrefixScore = 2;
    private const int LongNamePrefixScore = 1;
    private const int ContainsScore = 0;

    private readonly string _text;
    private readonly string[] _words;

    private CodecSearchQuery(string[] words)
    {
        _words = words;
        _text = string.Concat(words);
    }

    public bool IsEmpty => _words.Length == 0;

    public static CodecSearchQuery Parse(string? text)
    {
        return new CodecSearchQuery(
        [
            .. (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(Fold)
                .Where(word => word.Length > 0)
        ]);
    }

    // How well the codec matches, higher first: the query is its name, starts its name, starts its long name, or
    // occurs anywhere. null when a word is in neither name.
    public int? Score(CodecSearchKey key)
    {
        foreach (string word in _words)
        {
            if (!key.Name.Contains(word, StringComparison.Ordinal)
                && !key.LongName.Contains(word, StringComparison.Ordinal))
            {
                return null;
            }
        }

        if (key.Name == _text)
            return NameScore;

        if (key.Name.StartsWith(_text, StringComparison.Ordinal))
            return NamePrefixScore;

        if (key.LongName.StartsWith(_text, StringComparison.Ordinal))
            return LongNamePrefixScore;

        return ContainsScore;
    }

    // Folds full-width letters (NFKC) and case, and keeps only the letters and digits.
    internal static string Fold(string value)
    {
        string folded;
        try
        {
            folded = value.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // Ill-formed UTF-16, such as a lone surrogate typed into the search box.
            folded = value;
        }

        var builder = new StringBuilder(folded.Length);
        foreach (char c in folded)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}

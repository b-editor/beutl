using System.Text;

namespace Beutl.Controls.PropertyEditors;

// The names of a font family, normalized once for FontFamilySearchQuery.
internal sealed class FontFamilySearchKey
{
    public FontFamilySearchKey(IEnumerable<string> names)
    {
        Names =
        [
            .. names.Select(FontFamilySearchQuery.Normalize)
                .Where(name => name.Length > 0)
                .Distinct()
                .Select(FontFamilySearchQuery.Term.Create)
        ];
    }

    internal FontFamilySearchQuery.Term[] Names { get; }
}

// A font family search. Every word of the query has to occur in one of the family's names. Case, full
// and half width, and hiragana and katakana are not told apart, and the query also matches across the
// spaces and hyphens of a name, so "notosans" finds "Noto Sans".
internal sealed class FontFamilySearchQuery
{
    public const int NoMatch = -1;

    private const int ExactScore = 3;
    private const int PrefixScore = 2;
    private const int WordStartScore = 1;
    private const int ContainsScore = 0;

    private readonly Term _query;
    private readonly Term[] _words;

    private FontFamilySearchQuery(string text)
    {
        _query = Term.Create(text);
        _words = [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Term.Create)];
    }

    public bool IsEmpty => _words.Length == 0;

    public static FontFamilySearchQuery Parse(string? text) => new(Normalize(text ?? string.Empty));

    // How well the family matches, higher first: the query is one of its names, starts one, has every
    // word at the start of a word, or has them anywhere. NoMatch when a word is in none of the names.
    public int Score(FontFamilySearchKey key)
    {
        int best = NoMatch;
        foreach (Term name in key.Names)
        {
            best = Math.Max(best, Score(name));
        }

        return best;
    }

    private int Score(Term name)
    {
        bool hasCompact = _query.Compact.Length > 0;
        if (name.Text == _query.Text || (hasCompact && name.Compact == _query.Compact))
            return ExactScore;

        if (name.Text.StartsWith(_query.Text, StringComparison.Ordinal)
            || (hasCompact && name.Compact.StartsWith(_query.Compact, StringComparison.Ordinal)))
            return PrefixScore;

        bool atWordStarts = true;
        foreach (Term word in _words)
        {
            if (!TryFind(name.Text, word.Text, out bool atWordStart))
            {
                if (word.Compact.Length == 0 || !name.Compact.Contains(word.Compact, StringComparison.Ordinal))
                    return NoMatch;

                atWordStart = false;
            }

            atWordStarts &= atWordStart;
        }

        return atWordStarts ? WordStartScore : ContainsScore;
    }

    private static bool TryFind(string text, string word, out bool atWordStart)
    {
        atWordStart = false;
        int index = text.IndexOf(word, StringComparison.Ordinal);
        if (index < 0) return false;

        for (; index >= 0; index = text.IndexOf(word, index + 1, StringComparison.Ordinal))
        {
            if (index == 0 || !char.IsLetterOrDigit(text[index - 1]))
            {
                atWordStart = true;
                break;
            }
        }

        return true;
    }

    // Upper-cases, folds full-width letters and half-width katakana (NFKC) and hiragana into one form,
    // and collapses each run of white space, ideographic spaces included, into one space.
    internal static string Normalize(string value)
    {
        string folded;
        try
        {
            folded = value.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // Ill-formed UTF-16, such as a lone surrogate in a damaged name table.
            folded = value;
        }

        var builder = new StringBuilder(folded.Length);
        Span<char> buffer = stackalloc char[2];
        bool pendingSpace = false;
        foreach (Rune rune in folded.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            Rune kana = rune.Value is >= 0x3041 and <= 0x3096 ? new Rune(rune.Value + 0x60) : rune;
            builder.Append(buffer[..Rune.ToUpperInvariant(kana).EncodeToUtf16(buffer)]);
        }

        return builder.ToString();
    }

    // A normalized text and its letters and digits alone, which match across spaces, hyphens and dots.
    internal readonly record struct Term(string Text, string Compact)
    {
        public static Term Create(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (Rune rune in text.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(rune))
                    builder.Append(rune.ToString());
            }

            return new Term(text, builder.ToString());
        }
    }
}

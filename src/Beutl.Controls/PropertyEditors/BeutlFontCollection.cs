using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Beutl.Controls.PropertyEditors;

// Copies are shared across catalog revisions and kept alive for text already using them. Once the budget is
// reached, previews use an existing copy or the platform fallback; revisiting a copied font costs nothing more.
internal sealed class BeutlFontCollection(Media.FontManager fonts, long maxCopiedBytes = BeutlFontCollection.MaxCopiedBytes) : IFontCollection
{
    internal const long MaxCopiedBytes = 256L * 1024 * 1024;

    private static readonly Lock s_registrationGate = new();
    private static readonly ConditionalWeakTable<FontManager, ConditionalWeakTable<Media.FontManager, BeutlFontCollection>> s_registrations = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CopiedFont> _copies = [];
    private readonly Dictionary<string, CopiedFont> _familyCopies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadedFamilies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GlyphTypeface, CopiedFont> _owners = [];
    private long _revision = -1;
    private long _copiedBytes;

    public Uri Key { get; } = new($"fonts:Beutl-{Guid.NewGuid():N}");

    public int Count => GetFamilies().Length;

    public FontFamily this[int index] => GetFamilies()[index];

    internal long CopiedBytes
    {
        get { lock (_gate) return _copiedBytes; }
    }

    internal int CopiedFontCount
    {
        get { lock (_gate) return _copies.Count; }
    }

    public static BeutlFontCollection GetOrRegister(FontManager manager, Media.FontManager fonts, long maxCopiedBytes = MaxCopiedBytes)
    {
        lock (s_registrationGate)
        {
            var sources = s_registrations.GetOrCreateValue(manager);
            if (!sources.TryGetValue(fonts, out BeutlFontCollection? collection))
            {
                collection = new BeutlFontCollection(fonts, maxCopiedBytes);
                manager.AddFontCollection(collection);
                sources.Add(fonts, collection);
            }
            return collection;
        }
    }

    // Takes the collections made for fonts out of the managers they were added to, for a fork whose owner, such as an
    // editor, has closed. Text still drawn with its copies keeps them.
    public static void Unregister(Media.FontManager fonts)
    {
        lock (s_registrationGate)
        {
            foreach ((FontManager manager, ConditionalWeakTable<Media.FontManager, BeutlFontCollection> sources) in s_registrations)
            {
                if (sources.TryGetValue(fonts, out BeutlFontCollection? collection))
                {
                    sources.Remove(fonts);
                    manager.RemoveFontCollection(collection.Key);
                }
            }
        }
    }

    public bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight, FontStretch stretch,
        [NotNullWhen(true)] out GlyphTypeface? glyphTypeface)
        => TryGetTypeface(familyName, style, weight, stretch, nearest: false, out glyphTypeface);

    public bool TryGetNearestMatch(string familyName, FontStyle style, FontWeight weight, FontStretch stretch,
        [NotNullWhen(true)] out GlyphTypeface? glyphTypeface)
        => TryGetTypeface(familyName, style, weight, stretch, nearest: true, out glyphTypeface);

    private bool TryGetTypeface(string familyName, FontStyle style, FontWeight weight, FontStretch stretch,
        bool nearest, [NotNullWhen(true)] out GlyphTypeface? glyphTypeface)
    {
        lock (_gate)
        {
            Refresh();
            CopiedFont? requested = LoadFamily(familyName, style, weight);
            IEnumerable<CopiedFont> candidates = requested is null ? _familyCopies.Values : [requested];
            foreach (CopiedFont copy in candidates)
            {
                bool found = nearest
                    ? copy.TryGetNearestMatch(familyName, style, weight, stretch, out glyphTypeface)
                    : copy.TryGetGlyphTypeface(familyName, style, weight, stretch, out glyphTypeface);
                if (found && glyphTypeface is not null)
                {
                    _owners[glyphTypeface] = copy;
                    return true;
                }
            }
            glyphTypeface = null;
            return false;
        }
    }

    public bool TryMatchCharacter(int codepoint, FontStyle fontStyle, FontWeight fontWeight,
        FontStretch fontStretch, string? familyName, CultureInfo? culture, out Typeface typeface)
    {
        lock (_gate)
        {
            Refresh();
            if (familyName != null) LoadFamily(familyName, fontStyle, fontWeight);
            foreach (CopiedFont copy in _familyCopies.Values.Distinct())
                if (copy.TryMatchCharacter(codepoint, fontStyle, fontWeight, fontStretch, familyName, culture, out typeface))
                    return true;
            typeface = default;
            return false;
        }
    }

    public bool TryGetFamilyTypefaces(string familyName, [NotNullWhen(true)] out IReadOnlyList<Typeface>? familyTypefaces)
    {
        lock (_gate)
        {
            Refresh();
            LoadFamily(familyName, FontStyle.Normal, FontWeight.Normal);
            foreach (CopiedFont copy in _familyCopies.Values.Distinct())
                if (copy.TryGetFamilyTypefaces(familyName, out familyTypefaces))
                    return true;
            familyTypefaces = null;
            return false;
        }
    }

    public bool TryCreateSyntheticGlyphTypeface(GlyphTypeface glyphTypeface, FontStyle style, FontWeight weight,
        FontStretch stretch, [NotNullWhen(true)] out GlyphTypeface? syntheticGlyphTypeface)
    {
        lock (_gate)
        {
            if (_owners.TryGetValue(glyphTypeface, out CopiedFont? copy)
                && copy.TryCreateSyntheticGlyphTypeface(glyphTypeface, style, weight, stretch, out syntheticGlyphTypeface))
            {
                _owners[syntheticGlyphTypeface] = copy;
                return true;
            }
            syntheticGlyphTypeface = null;
            return false;
        }
    }

    private void Refresh()
    {
        long revision = fonts.Revision;
        if (_revision != revision)
        {
            _loadedFamilies.Clear();
            _revision = revision;
        }
    }

    private CopiedFont? LoadFamily(string familyName, FontStyle style, FontWeight weight)
    {
        _familyCopies.TryGetValue(familyName, out CopiedFont? previous);
        if (!_loadedFamilies.Add(familyName)) return previous;
        if (_copiedBytes >= maxCopiedBytes && previous is null) return null;
        var typeface = new Media.Typeface(new Media.FontFamily(familyName), (Media.FontStyle)style, (Media.FontWeight)weight);
        if (fonts.CopyFontData(typeface) is not { } data) return previous;
        string hash = Convert.ToHexString(SHA256.HashData(data));
        if (!_copies.TryGetValue(hash, out CopiedFont? copy))
        {
            // As with the original budget, the last admitted font can cross the limit.
            if (_copiedBytes >= maxCopiedBytes) return previous;
            copy = new CopiedFont(Key);
            using var stream = new MemoryStream(data, writable: false);
            if (!copy.Load(stream, familyName))
            {
                ((IDisposable)copy).Dispose();
                return null;
            }
            _copies.Add(hash, copy);
            _copiedBytes += data.Length;
        }
        copy.AddAlias(familyName);
        _familyCopies[familyName] = copy;
        return copy;
    }

    private FontFamily[] GetFamilies()
    {
        lock (_gate)
        {
            Refresh();
            return _familyCopies.Values.Distinct().SelectMany(copy => copy).Distinct().ToArray();
        }
    }

    public IEnumerator<FontFamily> GetEnumerator() => ((IEnumerable<FontFamily>)GetFamilies()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (CopiedFont copy in _copies.Values) ((IDisposable)copy).Dispose();
            _copies.Clear();
            _familyCopies.Clear();
            _loadedFamilies.Clear();
            _owners.Clear();
        }
    }

    private sealed class CopiedFont(Uri key) : FontCollectionBase
    {
        private GlyphTypeface? _primary;

        public override Uri Key => key;

        public bool Load(Stream stream, string familyName)
        {
            bool added = TryAddGlyphTypeface(stream, out _primary);
            if (_primary == null) return false;
            if (AddAlias(familyName) || added) return true;
            _primary.Dispose();
            _primary = null;
            return false;
        }

        public bool AddAlias(string familyName)
        {
            new Typeface(familyName).Normalize(out string lookupName);
            return TryAddGlyphTypeface(lookupName,
                new FontCollectionKey(_primary!.Style, _primary.Weight, _primary.Stretch), _primary);
        }
    }
}

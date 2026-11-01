using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Beutl.Controls.PropertyEditors;

// The fonts Beutl reads itself, built from its own copy of each font. A family loads the face Beutl resolves for its
// first request, so the picker's names cost one face each.
internal sealed class BeutlFontCollection(long maxCopiedBytes = BeutlFontCollection.MaxCopiedBytes) : FontCollectionBase
{
    // Each copy lives as long as the app, and the faces of a collection copy the tables they share once each, so the
    // families past this many bytes stay in the default font.
    internal const long MaxCopiedBytes = 256L * 1024 * 1024;

    public static readonly Uri CollectionKey = new("fonts:Beutl");

    private static readonly Lock s_registrationGate = new();
    private static (FontManager Manager, BeutlFontCollection Collection)? s_registration;

    private readonly Lock _gate = new();
    private readonly HashSet<string> _loadedFamilies = new(StringComparer.OrdinalIgnoreCase);
    private long _copiedBytes;

    public override Uri Key => CollectionKey;

    internal long CopiedBytes
    {
        get
        {
            lock (_gate) return _copiedBytes;
        }
    }

    public static BeutlFontCollection GetOrRegister(FontManager manager)
    {
        lock (s_registrationGate)
        {
            // Adding a collection under a taken key disposes the faces of the one it replaces, which drawn text can
            // still hold, so each manager gets a single collection.
            if (s_registration is not { } registration || registration.Manager != manager)
            {
                registration = (manager, new BeutlFontCollection());
                manager.AddFontCollection(registration.Collection);
                s_registration = registration;
            }

            return registration.Collection;
        }
    }

    public override bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight, FontStretch stretch,
        [NotNullWhen(true)] out GlyphTypeface? glyphTypeface)
    {
        lock (_gate)
        {
            if (!_loadedFamilies.Contains(familyName) && LoadFamily(familyName, style, weight))
                _loadedFamilies.Add(familyName);
        }

        return base.TryGetGlyphTypeface(familyName, style, weight, stretch, out glyphTypeface);
    }

    // False when Beutl has no such family yet, as before the project that bundles it opens.
    private bool LoadFamily(string familyName, FontStyle style, FontWeight weight)
    {
        // Past the budget no family is copied again, so a later lookup would find nothing new.
        if (_copiedBytes >= maxCopiedBytes)
            return true;

        var typeface = new Media.Typeface(new Media.FontFamily(familyName), (Media.FontStyle)style, (Media.FontWeight)weight);
        if (Media.FontManager.Instance.CopyFontData(typeface) is not { } data)
            return false;

        _copiedBytes += data.Length;
        using var stream = new MemoryStream(data, writable: false);
        // Adds the face under the names in its tables. Beutl names the family as the OS's font stack does, which can be
        // another name, and a lookup drops its style words, as "Light" from "Foo Light".
        bool added = TryAddGlyphTypeface(stream, out GlyphTypeface? glyphTypeface);
        new Typeface(familyName).Normalize(out string lookupName);
        if (glyphTypeface is not null
            && !TryAddGlyphTypeface(lookupName, new FontCollectionKey(glyphTypeface.Style, glyphTypeface.Weight, glyphTypeface.Stretch), glyphTypeface)
            && !added)
        {
            glyphTypeface.Dispose();
        }

        return true;
    }
}

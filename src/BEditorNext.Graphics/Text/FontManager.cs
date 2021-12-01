using BEditorNext.Configuration;

using SkiaSharp;

namespace BEditorNext.Graphics;

public sealed class FontManager
{
    public static readonly FontManager Instance = new();
    internal readonly Dictionary<FontFamily, TypefaceCollection> _fonts = new();
    private readonly string[] _fontDirs;

    private FontManager()
    {
        _fontDirs = GlobalConfiguration.Instance.FontConfig.FontDirectories.ToArray();
        var list = new List<SKTypeface>();

        foreach (string file in _fontDirs
            .Where(dir => Directory.Exists(dir))
            .Select(dir => Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            .SelectMany(files => files)
            .Where(file => Path.GetExtension(file) is ".ttf" or ".ttc" or ".otf"))
        {
            SKTypeface? face = LoadFont(file);

            if (face != null)
            {
                list.Add(face);
            }
        }

        foreach (IGrouping<string, SKTypeface> item in list.GroupBy(i => i.FamilyName))
        {
            var family = new FontFamily(item.Key);
            _fonts.Add(family, new TypefaceCollection(family, item));
        }
    }

    public IEnumerable<FontFamily> FontFamilies => _fonts.Keys;

    public int FontFamilyCount => _fonts.Count;

    public void AddFont(Stream stream)
    {
        AddFont(SKTypeface.FromStream(stream));
    }

    private void AddFont(SKTypeface typeface)
    {
        string familyName = typeface.FamilyName;
        var fontFamily = new FontFamily(familyName);

        if (_fonts.ContainsKey(fontFamily))
        {
            _fonts[fontFamily].Add(typeface.ToTypeface(), typeface);
        }
        else
        {
            _fonts.Add(fontFamily, new TypefaceCollection(fontFamily, new SKTypeface[] { typeface }));
        }
    }

    private static SKTypeface? LoadFont(string file)
    {
        try
        {
            return SKTypeface.FromFile(file);
        }
        catch
        {
            return null;
        }
    }
}


internal class TypefaceCollection : Dictionary<Typeface, SKTypeface>
{
    public TypefaceCollection(FontFamily family, IEnumerable<SKTypeface> typefaces)
    {
        foreach (SKTypeface typeface in typefaces)
        {
            Add(new Typeface(family, typeface.FontSlant.ToFontStyle(), (FontWeight)typeface.FontWeight), typeface);
        }
    }

    public SKTypeface Get(Typeface typeface)
    {
        return GetNearestMatch(this, typeface);
    }

    private static SKTypeface GetNearestMatch(IDictionary<Typeface, SKTypeface> typefaces, Typeface key)
    {
        if (typefaces.TryGetValue(key, out SKTypeface? typeface))
        {
            return typeface;
        }

        int initialWeight = (int)key.Weight;

        int weight = (int)key.Weight;

        weight -= weight % 50; // make sure we start at a full weight

        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < initialWeight; j += 50)
            {
                if (weight - j >= 100)
                {
                    if (typefaces.TryGetValue(new Typeface(key.FontFamily, (FontStyle)i, (FontWeight)(weight - j)), out typeface))
                    {
                        return typeface;
                    }
                }

                if (weight + j > 900)
                {
                    continue;
                }

                if (typefaces.TryGetValue(new Typeface(key.FontFamily, (FontStyle)i, (FontWeight)(weight + j)), out typeface))
                {
                    return typeface;
                }
            }
        }

        //Nothing was found so we try to get a regular typeface.
        return typefaces.TryGetValue(new Typeface(key.FontFamily), out typeface) ?
            typeface :
            throw new Exception();
    }
}

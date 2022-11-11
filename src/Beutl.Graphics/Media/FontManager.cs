using System.Diagnostics;

using Beutl.Configuration;

using SkiaSharp;

namespace Beutl.Media;

public sealed class FontManager
{
    public static readonly FontManager Instance = new();
    internal readonly Dictionary<FontFamily, TypefaceCollection> _fonts = new();
    private readonly string[] _fontDirs;

    private FontManager()
    {
        Typeface GetDefaultTypeface()
        {
            if (OperatingSystem.IsLinux())
            {
                using Process process = Process.Start(new ProcessStartInfo("/usr/bin/fc-match", "--format %{file}")
                {
                    RedirectStandardOutput = true
                })!;
                process.WaitForExit();

                string file = process.StandardOutput.ReadToEnd();
                SKTypeface sktypeface = SKTypeface.FromFile(file);
                Typeface typeface = sktypeface.ToTypeface();
                bool isAdded = AddFont(sktypeface);
                if (!isAdded)
                {
                    sktypeface.Dispose();
                }
                return typeface;
            }
            else
            {
                SKTypeface sk = SKTypeface.Default;
                AddFont(sk);
                return sk.ToTypeface();
            }
        }

        _fontDirs = GlobalConfiguration.Instance.FontConfig.FontDirectories.ToArray();
        var list = new List<SKTypeface>();

        foreach (string file in _fontDirs
            .Where(dir => Directory.Exists(dir))
            .Select(dir => Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            .SelectMany(files => files)
            .Where(file =>
            {
                ReadOnlySpan<char> ext = Path.GetExtension(file.AsSpan());
                return ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase);
            }))
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
            _fonts.Add(family, new TypefaceCollection(item));
        }

        DefaultTypeface = GetDefaultTypeface();
    }

    public IEnumerable<FontFamily> FontFamilies => _fonts.Keys;

    public int FontFamilyCount => _fonts.Count;

    public Typeface DefaultTypeface { get; }

    public void AddFont(Stream stream)
    {
        AddFont(SKTypeface.FromStream(stream));
    }

    private bool AddFont(SKTypeface typeface)
    {
        string familyName = typeface.FamilyName;
        var fontFamily = new FontFamily(familyName);

        if (_fonts.TryGetValue(fontFamily, out TypefaceCollection? value))
        {
            TypefaceCollection collection = value;
            var tf = typeface.ToTypeface();

            if (!collection.ContainsKey(tf))
            {
                collection.Add(tf, typeface);
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            _fonts.Add(fontFamily, new TypefaceCollection(new SKTypeface[] { typeface }));
            return true;
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
    public TypefaceCollection(IEnumerable<SKTypeface> typefaces)
    {
        foreach (SKTypeface typeface in typefaces)
        {
            TryAdd(typeface.ToTypeface(), typeface);
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

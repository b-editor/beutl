using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Beutl.Configuration;

using SkiaSharp;

namespace Beutl.Media;

public sealed class FontManager
{
    public static readonly FontManager Instance = new();
    internal readonly Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> _fonts = [];
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

        _fontDirs = [.. GlobalConfiguration.Instance.FontConfig.FontDirectories];
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
            _fonts.Add(family, TypefaceCollection.Create([.. item]));
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

        ref FrozenDictionary<Typeface, SKTypeface>? value
            = ref CollectionsMarshal.GetValueRefOrAddDefault(_fonts, fontFamily, out bool exists);

        if (exists)
        {
            var tf = typeface.ToTypeface();

            if (!value!.ContainsKey(tf))
            {
                value = value.Append(new(tf, typeface))
                    .ToFrozenDictionary();

                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            value = TypefaceCollection.Create([typeface]);
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

    public ImmutableArray<Typeface> GetTypefaces(FontFamily fontFamily)
    {
        return _fonts.TryGetValue(fontFamily, out FrozenDictionary<Typeface, SKTypeface>? value)
            ? value.Keys
            : [];
    }
}

internal static class TypefaceCollection
{
    public static FrozenDictionary<Typeface, SKTypeface> Create(SKTypeface[] typefaces)
    {
        var list = new List<KeyValuePair<Typeface, SKTypeface>>(typefaces.Length);
        foreach (SKTypeface typeface in typefaces)
        {
            list.Add(new(typeface.ToTypeface(), typeface));
        }

        return list.ToFrozenDictionary();
    }

    public static SKTypeface Get(this FrozenDictionary<Typeface, SKTypeface> typefaces, Typeface typeface)
    {
        return GetNearestMatch(typefaces, typeface);
    }

    private static SKTypeface GetNearestMatch(FrozenDictionary<Typeface, SKTypeface> typefaces, Typeface key)
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

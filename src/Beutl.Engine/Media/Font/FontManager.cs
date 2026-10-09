using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Media;

public sealed class FontManager
{
    public static readonly FontManager Instance = new();
    private readonly ILogger _logger = Log.CreateLogger<FontManager>();
    private readonly Lock _gate = new();
    private long _revision;
    internal long Revision => Volatile.Read(ref _revision);
    internal readonly Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> _fonts = [];
    internal readonly Dictionary<FontFamily, FontName> _fontNames = [];
    private Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> _projectFonts = [];
    private WeakReference<Project>? _projectFontsOwner;
    private readonly Dictionary<string, SKTypeface> _projectFontCache = [];
    private readonly HashSet<FontFamily> _reportedMissingFamilies = [];
    // Keyed by reference to a matched typeface; null when it has no wght axis.
    private readonly Dictionary<SKTypeface, WeightAxis?> _weightAxes = [];
    // Variable typefaces moved to another weight, keyed by the typeface they were cloned from and the wght
    // value. Like the registered typefaces, they live as long as the manager.
    private readonly Dictionary<(SKTypeface Typeface, float Weight), SKTypeface> _weightInstances = [];
    private readonly RenderFaceCache _renderFaces = new(FreeTypeFonts.Manager);
    private readonly string[] _fontDirs;
    private FontFamily[] _fallbackFamilies = [];
    private FontFamily? _emojiFamily;
    private readonly Dictionary<FontFamily, Dictionary<Typeface, Lazy<SKTypeface?>>> _deferredFonts = [];

    private FontManager()
    {
        // A material package installs its fonts under the home directory, which is not one
        // of the OS font directories the user configures.
        _fontDirs =
        [
            .. GlobalConfiguration.Instance.FontConfig.FontDirectories,
            BeutlEnvironment.GetMaterialsDirectoryPath()
        ];

        RegisterInstalledFonts();
        DefaultTypeface = ResolveDefaultTypeface();
    }

    private void RegisterInstalledFonts()
    {
        var list = new List<SKTypeface>();

        foreach (string file in _fontDirs
            .Where(dir => Directory.Exists(dir))
            .SelectMany(EnumerateFontCandidates))
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
            SKTypeface[] typefaces = [.. item];
            if (typefaces.Length == 0) continue;
            _fonts.Add(family, TypefaceCollection.Create(typefaces));

            if (!_fontNames.ContainsKey(family))
            {
                TryRegisterFontName(family, typefaces[0]);
            }
        }
    }

    private void TryRegisterFontName(FontFamily family, SKTypeface typeface)
    {
        // The OpenType 'name' table.
        const uint NameTableTag = 0x6E616D65;

        try
        {
            byte[]? buffer = typeface.GetTableData(NameTableTag);
            using var ms = new MemoryStream(buffer);
            var fontName = FontName.ReadFontName(ms);
            _fontNames.Add(family, fontName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read font name from {FontFamily}", family);
        }
    }

    private Typeface ResolveDefaultTypeface()
    {
        if (OperatingSystem.IsLinux())
        {
            var output = new StringBuilder();
            string applicationPath = "/usr/bin/fc-match";
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, "fc-match");
                if (File.Exists(fullPath))
                {
                    applicationPath = fullPath;
                    break;
                }
            }
            using Process process = Process.Start(new ProcessStartInfo(applicationPath, "--format %{file}")
            {
                RedirectStandardOutput = true
            })!;
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                    output.Append(e.Data);
            };
            process.BeginOutputReadLine();
            process.WaitForExit();

            process.CancelOutputRead();

            string file = output.ToString();
            SKTypeface? sktypeface = SKTypeface.FromFile(file);
            if (sktypeface != null)
            {
                Typeface typeface = Typeface.FromSKTypeface(sktypeface);
                bool isAdded = AddFont(sktypeface);
                if (!isAdded)
                {
                    sktypeface.Dispose();
                }
                return typeface;
            }
        }

        SKTypeface sk = SKTypeface.Default;
        AddFont(sk);
        return Typeface.FromSKTypeface(sk);
    }

    public IEnumerable<FontFamily> FontFamilies
    {
        get
        {
            // Dictionary はスレッドセーフでない。AddFont と並行して列挙すると
            // 内部状態が壊れて無限ループや null 例外になり得るのでスナップショットを返す。
            lock (_gate)
            {
                return _fonts.Keys.Concat(_projectFonts.Keys).Concat(_deferredFonts.Keys).Distinct().ToArray();
            }
        }
    }

    public int FontFamilyCount
    {
        get
        {
            lock (_gate) return _fonts.Keys.Concat(_projectFonts.Keys).Concat(_deferredFonts.Keys).Distinct().Count();
        }
    }

    public Typeface DefaultTypeface { get; }

    internal void SetFallbackFonts(IEnumerable<FontFamily> families, FontFamily? emojiFamily)
    {
        FontFamily[] fallbackFamilies = families.Distinct().ToArray();
        lock (_gate)
        {
            if (_fallbackFamilies.SequenceEqual(fallbackFamilies) && _emojiFamily == emojiFamily)
                return;

            _fallbackFamilies = fallbackFamilies;
            _emojiFamily = emojiFamily;
            Interlocked.Increment(ref _revision);
        }
    }

    internal (FontFamily[] Families, FontFamily? Emoji) GetFallbackFamilies()
    {
        lock (_gate)
        {
            // The array is replaced as a unit by SetFallbackFonts and is never mutated.
            return (_fallbackFamilies, _emojiFamily);
        }
    }

    internal void RegisterFont(Typeface typeface, Func<Stream> openStream)
    {
        lock (_gate)
        {
            if (!_deferredFonts.TryGetValue(typeface.FontFamily, out var fonts))
            {
                fonts = [];
                if (_fonts.TryGetValue(typeface.FontFamily, out var loaded))
                {
                    foreach (var entry in loaded)
                        fonts.Add(entry.Key, new Lazy<SKTypeface?>(() => entry.Value));
                }
                _deferredFonts.Add(typeface.FontFamily, fonts);
            }
            if (fonts.TryAdd(typeface, new Lazy<SKTypeface?>(() => LoadRegisteredFont(typeface, openStream))))
                Interlocked.Increment(ref _revision);
        }
    }

    private SKTypeface? LoadRegisteredFont(Typeface typeface, Func<Stream> openStream)
    {
        SKTypeface? face;
        try
        {
            using Stream stream = openStream();
            face = SKTypeface.FromStream(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load registered font {FontFamily}", typeface.FontFamily);
            return null;
        }
        if (face is null)
            return null;
        if (face.FamilyName != typeface.FontFamily.Name)
        {
            face.Dispose();
            return null;
        }
        if (!AddFont(face, typeface.Weight))
            face.Dispose();
        // Materializing a catalog entry does not change font selection, so leave Revision alone.
        return _fonts[typeface.FontFamily].Get(typeface);
    }

    internal bool TryResolveSkia(Typeface typeface, out SKTypeface face)
    {
        lock (_gate)
        {
            if (TryResolveTypeface(typeface, out face!))
            {
                face = InstantiateWeight(_renderFaces.Get(face), typeface.Weight);
                return true;
            }
            return false;
        }
    }

    public void LoadProjectFonts(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var fonts = new List<SKTypeface>();
        if (project.Uri is { IsFile: true } uri)
        {
            string directory = Path.Combine(Path.GetDirectoryName(uri.LocalPath)!, "resources", "fonts");
            if (Directory.Exists(directory))
            {
                foreach (string file in EnumerateFontCandidates(directory))
                    fonts.AddRange(LoadProjectFontFaces(file));
            }
        }

        lock (_gate)
        {
            // A bundle's family takes precedence as a unit, including nearest-weight
            // matching. Keep system fonts separate so the next project can restore them.
            _projectFonts = fonts.GroupBy(font => font.FamilyName).ToDictionary(
                group => new FontFamily(group.Key), group => TypefaceCollection.Create(group.ToArray()));
            _projectFontsOwner = new WeakReference<Project>(project);
            Interlocked.Increment(ref _revision);
        }
    }

    public void ClearProjectFonts(Project? expectedProject = null)
    {
        lock (_gate)
        {
            if (expectedProject is not null
                && (_projectFontsOwner is null || !_projectFontsOwner.TryGetTarget(out Project? owner)
                    || !ReferenceEquals(owner, expectedProject)))
                return;
            _projectFonts = [];
            _projectFontsOwner = null;
            Interlocked.Increment(ref _revision);
        }
    }

    public void AddFont(Stream stream) => AddFont(stream, null);

    internal void AddFont(Stream stream, FontWeight? weight)
    {
        SKTypeface? typeface = SKTypeface.FromStream(stream);
        if (typeface == null) return;
        // 重複登録された SKTypeface は AddFont 側で false が返るので、
        // 戻り値を無視せず必ず Dispose する。
        if (!AddFont(typeface, weight))
        {
            typeface.Dispose();
        }
        else
        {
            Interlocked.Increment(ref _revision);
        }
    }

    private bool AddFont(SKTypeface typeface, FontWeight? weight = null)
    {
        string familyName = typeface.FamilyName;
        var fontFamily = new FontFamily(familyName);
        var tf = new Typeface(fontFamily, typeface.FontSlant.ToFontStyle(), weight ?? (FontWeight)typeface.FontWeight);

        lock (_gate)
        {
            ref FrozenDictionary<Typeface, SKTypeface>? value
                = ref CollectionsMarshal.GetValueRefOrAddDefault(_fonts, fontFamily, out bool exists);

            if (exists)
            {
                if (!value!.ContainsKey(tf))
                {
                    value = value.Append(new(tf, typeface))
                        .ToFrozenDictionary();
                    if (_deferredFonts.TryGetValue(fontFamily, out var deferred))
                        deferred[tf] = new Lazy<SKTypeface?>(() => typeface);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                value = new Dictionary<Typeface, SKTypeface> { [tf] = typeface }.ToFrozenDictionary();
                if (_deferredFonts.TryGetValue(fontFamily, out var deferred))
                    deferred[tf] = new Lazy<SKTypeface?>(() => typeface);
                return true;
            }
        }
    }

    /// <summary>
    /// Enumerates the font files under <paramref name="root"/>, skipping entries the
    /// process cannot read.
    /// </summary>
    /// <remarks>
    /// A material package can leave an unreadable subdirectory or a disconnected mount
    /// under the scanned root. <see cref="FontManager"/> builds its map during static
    /// initialization, so letting that surface would keep the process from starting.
    /// </remarks>
    public static IEnumerable<string> EnumerateFontCandidates(string root)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            string identity;
            try { identity = FilePathComparison.ResolveCanonicalPath(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { continue; }
            if (!visited.Add(identity)) continue;
            foreach (string entry in EnumerateDirectory(directory))
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                string ext = Path.GetExtension(entry);
                if (ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                    yield return entry;
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectory(string directory)
    {
        IEnumerator<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System,
            }).GetEnumerator();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { yield break; }
        using (entries)
        {
            while (true)
            {
                bool hasNext;
                try { hasNext = entries.MoveNext(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
                if (!hasNext) yield break;
                yield return entries.Current;
            }
        }
    }

    public static IEnumerable<SKTypeface> OpenFontFaces(string file)
    {
        int count = GetFontFaceCount(file);
        for (int index = 0; index < count; index++)
            if (SKTypeface.FromFile(file, index) is { } face) yield return face;
    }

    private static int GetFontFaceCount(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            Span<byte> header = stackalloc byte[12];
            return GetFontFaceCount(header[..stream.Read(header)], stream.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static int GetFontFaceCount(ReadOnlySpan<byte> header, long length)
    {
        if (header.Length < 12 || !header[..4].SequenceEqual("ttcf"u8)) return 1;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
        return count > 0 && count <= 1024 && 12L + count * 4 <= length ? (int)count : 0;
    }

    private List<SKTypeface> LoadProjectFontFaces(string file)
    {
        var faces = new List<SKTypeface>();
        try
        {
            byte[] bytes = File.ReadAllBytes(file);
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            int count = GetFontFaceCount(bytes, bytes.LongLength);
            for (int index = 0; index < count; index++)
                if (LoadFont(file, bytes, hash, index) is { } typeface) faces.Add(typeface);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load font from {File}", file);
        }
        return faces;
    }

    private SKTypeface? LoadFont(string file, byte[]? bytes = null, string? hash = null, int index = 0)
    {
        try
        {
            if (bytes is not null)
            {
                string key = hash + ":" + index;
                lock (_gate)
                {
                    // Registered faces can still be held by renderers. Cache immutable font
                    // bytes across project switches instead of disposing or duplicating them.
                    if (_projectFontCache.TryGetValue(key, out SKTypeface? cached)) return cached;
                    using var stream = new MemoryStream(bytes, writable: false);
                    SKTypeface? face = SKTypeface.FromStream(stream, index);
                    if (face is not null) _projectFontCache.Add(key, face);
                    return face;
                }
            }
            return SKTypeface.FromFile(file, index);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load font from {File}", file);
            return null;
        }
    }

    public ImmutableArray<Typeface> GetTypefaces(FontFamily fontFamily)
    {
        lock (_gate)
        {
            if (_projectFonts.TryGetValue(fontFamily, out var project))
                return project.Keys;
            if (_deferredFonts.TryGetValue(fontFamily, out var deferred))
                return [.. deferred.Keys];
            return _fonts.TryGetValue(fontFamily, out var value) ? value.Keys : [];
        }
    }

    internal SKTypeface ResolveSkia(Typeface typeface)
    {
        lock (_gate)
        {
            // An unregistered family is ordinary input (uninstalled font, or a subfamily name such
            // as "Inter 28pt"), so this runs inside the render pass and must not throw.
            if (TryResolveTypeface(typeface, out SKTypeface? face))
            {
                return InstantiateWeight(_renderFaces.Get(face), typeface.Weight);
            }

            // ToSkia() runs per text layout, so an unregistered family in a multi-frame render
            // would log once per frame per layout; report each missing family once.
            if (_reportedMissingFamilies.Add(typeface.FontFamily))
            {
                _logger.LogWarning(
                    "Font family '{FontFamily}' is not registered; falling back to '{Fallback}'",
                    typeface.FontFamily.Name,
                    DefaultTypeface.FontFamily.Name);
            }

            return TryResolveTypeface(new Typeface(DefaultTypeface.FontFamily, typeface.Style, typeface.Weight), out var fallback)
                ? InstantiateWeight(_renderFaces.Get(fallback), typeface.Weight)
                : _renderFaces.Get(SKTypeface.Default);
        }
    }

    internal SKTypeface GetRenderFace(SKTypeface typeface)
    {
        lock (_gate) return _renderFaces.Get(typeface);
    }

    private bool TryResolveTypeface(Typeface typeface, out SKTypeface face)
    {
        if (_projectFonts.TryGetValue(typeface.FontFamily, out var project))
        {
            face = project.Get(typeface);
            return true;
        }
        if (_deferredFonts.TryGetValue(typeface.FontFamily, out var deferred)
            && deferred.Get(typeface).Value is { } loaded)
        {
            face = loaded;
            return true;
        }
        if (_fonts.TryGetValue(typeface.FontFamily, out var fonts))
        {
            face = fonts.Get(typeface);
            return true;
        }
        face = null!;
        return false;
    }

    // A variable font registers once, as its default instance, so every weight resolves to that instance.
    // Move the matched typeface along its wght axis to the requested weight instead; a typeface without the
    // axis, or already at that weight, is returned as is. Callers hold _gate.
    private SKTypeface InstantiateWeight(SKTypeface typeface, FontWeight weight)
    {
        if (!_weightAxes.TryGetValue(typeface, out WeightAxis? axis))
        {
            axis = WeightAxis.Find(typeface);
            _weightAxes.Add(typeface, axis);
        }

        if (axis is not { } weightAxis)
        {
            return typeface;
        }

        // Skia clamps to the axis as well; clamping first keeps a single instance per rendered weight.
        float value = Math.Clamp((float)weight, weightAxis.Min, weightAxis.Max);
        if (value == weightAxis.Position)
        {
            return typeface;
        }

        if (!_weightInstances.TryGetValue((typeface, value), out SKTypeface? instance))
        {
            ReadOnlySpan<SKFontVariationPositionCoordinate> position = [new() { Axis = WeightAxis.Tag, Value = value }];
            instance = typeface.Clone(position) ?? typeface;
            _weightInstances.Add((typeface, value), instance);
        }

        return instance;
    }

    private readonly record struct WeightAxis(float Min, float Max, float Position)
    {
        public static readonly SKFourByteTag Tag = new('w', 'g', 'h', 't');

        public static WeightAxis? Find(SKTypeface typeface)
        {
            foreach (SKFontVariationAxis axis in typeface.VariationDesignParameters)
            {
                if (!axis.Tag.Equals(Tag))
                {
                    continue;
                }

                // A typeface created at a named instance sits at that instance rather than the axis default.
                float position = axis.Default;
                foreach (SKFontVariationPositionCoordinate coordinate in typeface.VariationDesignPosition)
                {
                    if (coordinate.Axis.Equals(Tag))
                    {
                        position = coordinate.Value;
                    }
                }

                return new WeightAxis(axis.Min, axis.Max, position);
            }

            return null;
        }
    }

    public bool IsRegistered(FontFamily fontFamily)
    {
        lock (_gate)
        {
            return _projectFonts.ContainsKey(fontFamily) || _fonts.ContainsKey(fontFamily) || _deferredFonts.ContainsKey(fontFamily);
        }
    }
}

// Maps a matched typeface to the face text draws with. On Windows that face is FreeType's, created from the
// same font data (see FreeTypeFonts); without a manager it is the typeface itself. FontManager holds _gate.
internal sealed class RenderFaceCache(SKFontManager? manager)
{
    // Keyed by reference. Like the registered typefaces, the faces live as long as the cache.
    private readonly Dictionary<SKTypeface, SKTypeface> _faces = [];

    public SKTypeface Get(SKTypeface typeface)
    {
        if (manager is null)
        {
            return typeface;
        }

        if (!_faces.TryGetValue(typeface, out SKTypeface? face))
        {
            // CreateTypeface takes the stream over. A font the manager cannot open keeps drawing as is.
            SKStreamAsset? stream = typeface.OpenStream(out int index);
            face = (stream is null ? null : manager.CreateTypeface(stream, index)) ?? typeface;
            _faces.Add(typeface, face);
        }

        return face;
    }
}

internal static class TypefaceCollection
{
    public static FrozenDictionary<Typeface, SKTypeface> Create(SKTypeface[] typefaces)
    {
        // Two files can describe the same family/style/weight — a material package
        // shipping a font the system already has, say — and ToFrozenDictionary throws on
        // a duplicate key, which would take out the whole FontManager initializer.
        var map = new Dictionary<Typeface, SKTypeface>(typefaces.Length);
        foreach (SKTypeface typeface in typefaces)
        {
            map.TryAdd(Typeface.FromSKTypeface(typeface), typeface);
        }

        return map.ToFrozenDictionary();
    }

    public static TValue Get<TValue>(this IReadOnlyDictionary<Typeface, TValue> typefaces, Typeface typeface)
    {
        return GetNearestMatch(typefaces, typeface);
    }

    private static TValue GetNearestMatch<TValue>(IReadOnlyDictionary<Typeface, TValue> typefaces, Typeface key)
    {
        if (typefaces.TryGetValue(key, out TValue? typeface))
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
        return typefaces.TryGetValue(new Typeface(key.FontFamily), out typeface) ? typeface : typefaces.Values.First();
    }
}

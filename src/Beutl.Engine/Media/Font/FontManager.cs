using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Media;

// A manager made by the constructor holds the installed fonts. Fork makes a manager that resolves the fonts loaded
// into it (LoadProjectFonts) ahead of its parent's: the host shares one manager, an open project forks it, and an
// editor forks its project's. A fork asks its parent for everything it does not hold, so it sees the installed fonts,
// the fallback order and its parent's fonts as they change, and loading fonts into it leaves its parent and siblings
// alone.
public sealed class FontManager
{
    private readonly ILogger _logger = Log.CreateLogger<FontManager>();
    private readonly Lock _gate = new();
    // The manager this one was forked from; null for one made by the constructor.
    private readonly FontManager? _parent;
    // The manager made by the constructor at the top of the forks.
    private readonly FontManager _root;
    private long _revision;
    // The fonts loaded into this manager, ahead of its parent's (a fork) or the installed ones.
    private Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> _projectFonts = [];
    private WeakReference<Project>? _projectFontsOwner;
    private Dictionary<FontFamily, List<(FontCollection Collection, int Index)>> _lazyProjectFaces = [];
    private readonly HashSet<FontFamily> _reportedMissingFamilies = [];
    private readonly string[] _fontDirs;

    // The rest belongs to the root and is left empty in a fork.
    internal readonly Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> _fonts = [];
    internal readonly Dictionary<FontFamily, FontName> _fontNames = [];
    // Faces loaded from a project, by content hash and face index. Weak, so the faces of a closed project go once
    // nothing (a fork, or text drawn with them) holds them, and a project opened again shares the live ones.
    private readonly Dictionary<string, WeakReference<SKTypeface>> _projectFontCache = [];
    private readonly FaceCache _faces = new(FreeTypeFonts.Manager);
    private FontFamily[] _fallbackFamilies = [];
    private FontFamily? _emojiFamily;
    private readonly Dictionary<FontFamily, Dictionary<Typeface, Lazy<SKTypeface?>>> _deferredFonts = [];
    // macOS faces after the first of a collection, loaded with their family on its first lookup.
    private readonly Dictionary<FontFamily, List<(FontCollection Collection, int Index)>> _lazyFaces = [];

    public FontManager(FontConfig configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _root = this;
        // A material package installs its fonts under the home directory, which is not one
        // of the OS font directories the user configures.
        _fontDirs =
        [
            .. configuration.FontDirectories,
            BeutlEnvironment.GetMaterialsDirectoryPath()
        ];

        RegisterInstalledFonts();
        DefaultTypeface = ResolveDefaultTypeface();
    }

    private FontManager(FontManager parent)
    {
        _parent = parent;
        _root = parent._root;
        _fontDirs = parent._fontDirs;
        DefaultTypeface = parent.DefaultTypeface;
    }

    // Changes whenever the fonts this manager resolves do: its own counter only grows, and so does its parent's.
    internal long Revision => Volatile.Read(ref _revision) + (_parent?.Revision ?? 0);

    /// <summary>
    /// Creates a manager that resolves the fonts loaded into it ahead of this one's, and the rest through this one.
    /// </summary>
    /// <remarks>
    /// A fork holds no installed fonts of its own, so it costs little to make. It lives as long as something holds
    /// it, such as a render or an export that captured it, even after its owner, say a closed project, lets it go.
    /// </remarks>
    public FontManager Fork() => new(this);

    private void RegisterInstalledFonts()
    {
        var list = new List<SKTypeface>();

        var names = new Dictionary<FontFamily, FontName>();
        foreach (string file in _fontDirs
            .Where(dir => Directory.Exists(dir))
            .SelectMany(EnumerateFontCandidates))
        {
            SKTypeface? face = LoadFont(file);

            if (face != null)
            {
                list.Add(face);
            }

            int count = GetFontFaceCount(file);
            if (count > 1 && OperatingSystem.IsMacOS())
            {
                // Copying every later face of the system collections would take hundreds of megabytes.
                try
                {
                    using FileStream stream = File.OpenRead(file);
                    var collection = new FontCollection(file);
                    foreach ((int index, string family, FontName? name) in FontCollection.ReadLaterFamilies(stream, count))
                    {
                        AddLazyFace(_lazyFaces, new FontFamily(family), collection, index);
                        if (name is not null) names.TryAdd(new FontFamily(family), name);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError(ex, "Failed to load font from {File}", file);
                }
            }
            else
            {
                for (int index = 1; index < count; index++)
                    if (LoadFont(file, index: index) is { } later) list.Add(later);
            }
        }

        foreach (IGrouping<string, SKTypeface> item in list.GroupBy(i => i.FamilyName))
        {
            var family = new FontFamily(item.Key);
            SKTypeface[] typefaces = [.. item];
            _fonts.Add(family, TypefaceCollection.Create(typefaces));
            TryRegisterFontName(family, typefaces[0]);
        }

        foreach ((FontFamily family, FontName name) in names)
            _fontNames.TryAdd(family, name);
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
        SKTypeface sk = OperatingSystem.IsLinux() ? DefaultFontResolver.Resolve() : SKTypeface.Default;
        Typeface typeface = Typeface.FromSKTypeface(sk);
        if (!AddFont(sk) && !ReferenceEquals(sk, SKTypeface.Default))
            sk.Dispose();
        return typeface;
    }

    public IEnumerable<FontFamily> FontFamilies
    {
        get
        {
            // Dictionary はスレッドセーフでない。AddFont と並行して列挙すると
            // 内部状態が壊れて無限ループや null 例外になり得るのでスナップショットを返す。
            if (_parent is not null)
            {
                FontFamily[] own;
                lock (_gate)
                    own = [.. _projectFonts.Keys, .. _lazyProjectFaces.Keys];
                return own.Concat(_parent.FontFamilies).Distinct().ToArray();
            }

            lock (_gate)
            {
                return _fonts.Keys.Concat(_projectFonts.Keys).Concat(_deferredFonts.Keys)
                    .Concat(_lazyFaces.Keys).Concat(_lazyProjectFaces.Keys).Distinct().ToArray();
            }
        }
    }

    public int FontFamilyCount => FontFamilies.Count();

    public Typeface DefaultTypeface { get; }

    // The folders the installed fonts were read from at startup; a later change to the settings applies after restart.
    internal IReadOnlyList<string> FontDirectories => _fontDirs;

    internal FontName? GetFontName(FontFamily family)
    {
        if (_parent is not null)
            return _parent.GetFontName(family);

        lock (_gate)
        {
            return _fontNames.GetValueOrDefault(family);
        }
    }

    // The fallback order belongs to the root, so every fork falls back alike.
    internal void SetFallbackFonts(IEnumerable<FontFamily> families, FontFamily? emojiFamily)
    {
        if (_parent is not null)
        {
            _root.SetFallbackFonts(families, emojiFamily);
            return;
        }

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
        if (_parent is not null)
            return _root.GetFallbackFamilies();

        lock (_gate)
        {
            // The array is replaced as a unit by SetFallbackFonts and is never mutated.
            return (_fallbackFamilies, _emojiFamily);
        }
    }

    internal void RegisterFont(Typeface typeface, Func<Stream> openStream)
    {
        if (_parent is not null)
        {
            _root.RegisterFont(typeface, openStream);
            return;
        }

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
            // The pinned bundled face takes precedence over an installed duplicate, without
            // opening its stream until requested. Other styles in the family remain available.
            fonts[typeface] = new Lazy<SKTypeface?>(() => LoadRegisteredFont(typeface, openStream));
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
        if (!AddFont(face, typeface.Weight, replaceExisting: true))
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
                face = _root._faces.Get(face, typeface.Weight);
                return true;
            }
            return false;
        }
    }

    // The face a typeface resolves to as a standalone font, for UI that draws it through another font stack;
    // null for an unregistered family.
    internal byte[]? CopyFontData(Typeface typeface)
    {
        SKTypeface face;
        lock (_gate)
        {
            if (!TryResolveTypeface(typeface, out face))
                return null;
        }

        // The face is held here, so its data is read outside the gate.
        using SKStreamAsset? stream = face.OpenStream(out int index);
        using SKData? data = stream is null ? null : SKData.Create(stream);
        if (data is null)
            return null;
        // A face of a collection is copied alone, without the faces it shares the file with.
        return data.AsSpan().StartsWith("ttcf"u8) ? FontCollection.CopyFace(data.AsSpan(), index) : data.ToArray();
    }

    public void LoadProjectFonts(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var fonts = new List<SKTypeface>();
        var lazyFaces = new Dictionary<FontFamily, List<(FontCollection Collection, int Index)>>();
        if (project.Uri is { IsFile: true } uri)
        {
            string directory = Path.Combine(Path.GetDirectoryName(uri.LocalPath)!, "resources", "fonts");
            if (Directory.Exists(directory))
            {
                foreach (string file in EnumerateFontCandidates(directory))
                    fonts.AddRange(LoadProjectFontFaces(file, lazyFaces));
            }
        }

        lock (_gate)
        {
            // A bundle's family takes precedence as a unit, including nearest-weight
            // matching. Keep system fonts separate so the next project can restore them.
            _projectFonts = fonts.GroupBy(font => font.FamilyName).ToDictionary(
                group => new FontFamily(group.Key), group => TypefaceCollection.Create(group.ToArray()));
            _lazyProjectFaces = lazyFaces;
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
            _lazyProjectFaces = [];
            _projectFontsOwner = null;
            Interlocked.Increment(ref _revision);
        }
    }

    // Adds an installed font, which every fork shares.
    public void AddFont(Stream stream)
    {
        if (_parent is not null)
        {
            _root.AddFont(stream);
            return;
        }

        SKTypeface? typeface = SKTypeface.FromStream(stream);
        if (typeface == null) return;
        // 重複登録された SKTypeface は AddFont 側で false が返るので、
        // 戻り値を無視せず必ず Dispose する。
        if (!AddFont(typeface))
        {
            typeface.Dispose();
        }
        else
        {
            Interlocked.Increment(ref _revision);
        }
    }

    private bool AddFont(SKTypeface typeface, FontWeight? weight = null, bool replaceExisting = false)
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
                if (!value!.ContainsKey(tf) || replaceExisting)
                {
                    // Replaced faces can still be held by renderers; keep their handles alive.
                    value = value.Where(entry => entry.Key != tf).Append(new(tf, typeface))
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
    /// under the scanned root. <see cref="FontManager"/> builds its map during
    /// construction, so letting that surface would keep the process from starting.
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
        FontCollection? collection = null;
        for (int index = 0; index < count; index++)
        {
            SKTypeface? face = index == 0 || !OperatingSystem.IsMacOS()
                ? SKTypeface.FromFile(file, index)
                : (collection ??= new FontCollection(file)).LoadCopy(index);
            if (face is not null) yield return face;
        }
    }

    // The family of every face of a file; on macOS later faces are named from their tables instead of copied.
    internal static IEnumerable<string> ReadFamilyNames(string file)
    {
        int count = GetFontFaceCount(file);
        for (int index = 0; index < (OperatingSystem.IsMacOS() ? Math.Min(count, 1) : count); index++)
        {
            using SKTypeface? face = SKTypeface.FromFile(file, index);
            if (face is not null) yield return face.FamilyName;
        }

        if (count > 1 && OperatingSystem.IsMacOS())
        {
            using FileStream stream = File.OpenRead(file);
            foreach ((_, string family, _) in FontCollection.ReadLaterFamilies(stream, count))
                yield return family;
        }
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

    private List<SKTypeface> LoadProjectFontFaces(
        string file, Dictionary<FontFamily, List<(FontCollection Collection, int Index)>> lazyFaces)
    {
        var faces = new List<SKTypeface>();
        SKData? data = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(file);
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            int count = GetFontFaceCount(bytes, bytes.LongLength);
            int eager = OperatingSystem.IsMacOS() ? Math.Min(count, 1) : count;
            for (int index = 0; index < eager; index++)
            {
                // The faces of one file share a single copy of it.
                if (LoadProjectFace(hash, index, () => SKTypeface.FromData(data ??= SKData.CreateCopy(bytes), index)) is { } typeface)
                    faces.Add(typeface);
            }

            if (eager < count)
            {
                var collection = new FontCollection(file, bytes) { Hash = hash };
                foreach ((int index, string family, _) in FontCollection.ReadLaterFamilies(new MemoryStream(bytes, writable: false), count))
                    AddLazyFace(lazyFaces, new FontFamily(family), collection, index);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load font from {File}", file);
        }
        finally
        {
            data?.Dispose();
        }
        return faces;
    }

    // Every manager of a root shares the faces of the same file while any of them, or text drawn with them, holds them.
    private SKTypeface? LoadProjectFace(string hash, int index, Func<SKTypeface?> load)
    {
        string key = hash + ":" + index;
        Dictionary<string, WeakReference<SKTypeface>> cache = _root._projectFontCache;
        lock (cache)
        {
            if (cache.TryGetValue(key, out WeakReference<SKTypeface>? cached) && cached.TryGetTarget(out SKTypeface? live))
                return live;
            SKTypeface? face = load();
            if (face is not null)
            {
                // Entries of faces that went are dropped as faces are added, so the cache does not grow with them.
                if (cache.Count > 0 && cache.Count % 64 == 0)
                {
                    foreach ((string staleKey, WeakReference<SKTypeface> stale) in cache)
                    {
                        if (!stale.TryGetTarget(out _))
                            cache.Remove(staleKey);
                    }
                }

                cache[key] = new WeakReference<SKTypeface>(face);
            }
            return face;
        }
    }

    private SKTypeface? LoadFont(string file, int index = 0)
    {
        try
        {
            return SKTypeface.FromFile(file, index);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load font from {File}", file);
            return null;
        }
    }

    private static void AddLazyFace(
        Dictionary<FontFamily, List<(FontCollection Collection, int Index)>> lazyFaces,
        FontFamily family, FontCollection collection, int index)
    {
        if (!lazyFaces.TryGetValue(family, out var faces))
            lazyFaces[family] = faces = [];
        faces.Add((collection, index));
    }

    // Loads the lazy faces of the family with their own keys. Callers hold _gate.
    private void LoadLazyFaces(FontFamily family)
    {
        if (_lazyProjectFaces.Remove(family, out var projectFaces))
        {
            foreach (IGrouping<string, SKTypeface> loaded in projectFaces
                         .Select(lazy => LoadProjectFace(lazy.Collection.Hash!, lazy.Index, () => LoadLazyFace(lazy.Collection, lazy.Index)))
                         .OfType<SKTypeface>()
                         .GroupBy(face => face.FamilyName))
            {
                var loadedFamily = new FontFamily(loaded.Key);
                SKTypeface[] known = _projectFonts.TryGetValue(loadedFamily, out var existing) ? [.. existing.Values] : [];
                _projectFonts[loadedFamily] = TypefaceCollection.Create([.. known, .. loaded]);
            }
        }

        if (_lazyFaces.Remove(family, out var faces))
        {
            foreach ((FontCollection collection, int index) in faces)
            {
                if (LoadLazyFace(collection, index) is { } face && !AddFont(face))
                    face.Dispose();
            }
        }
    }

    private SKTypeface? LoadLazyFace(FontCollection collection, int index)
    {
        try
        {
            return collection.LoadCopy(index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to load font from {File}", collection.File);
            return null;
        }
    }

    public ImmutableArray<Typeface> GetTypefaces(FontFamily fontFamily)
    {
        lock (_gate)
        {
            LoadLazyFaces(fontFamily);
            if (_projectFonts.TryGetValue(fontFamily, out var project))
                return project.Keys;
            if (_parent is not null)
                return _parent.GetTypefaces(fontFamily);
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
                return _root._faces.Get(face, typeface.Weight);
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
                ? _root._faces.Get(fallback, typeface.Weight)
                : _root._faces.GetRenderFace(SKTypeface.Default);
        }
    }

    // Test-only (via InternalsVisibleTo): the face a registered typeface renders with.
    internal SKTypeface GetRenderFace(SKTypeface typeface)
    {
        return _root._faces.GetRenderFace(typeface);
    }

    // Callers hold _gate. A fork asks its parent, under the parent's gate, for a family it does not hold.
    private bool TryResolveTypeface(Typeface typeface, out SKTypeface face)
    {
        LoadLazyFaces(typeface.FontFamily);
        if (_projectFonts.TryGetValue(typeface.FontFamily, out var project))
        {
            face = project.Get(typeface);
            return true;
        }
        if (_parent is not null)
        {
            lock (_parent._gate)
                return _parent.TryResolveTypeface(typeface, out face);
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

    public bool IsRegistered(FontFamily fontFamily)
    {
        lock (_gate)
        {
            if (_projectFonts.ContainsKey(fontFamily) || _lazyProjectFaces.ContainsKey(fontFamily))
                return true;
            if (_parent is not null)
                return _parent.IsRegistered(fontFamily);
            return _fonts.ContainsKey(fontFamily) || _deferredFonts.ContainsKey(fontFamily) || _lazyFaces.ContainsKey(fontFamily);
        }
    }
}

// Maps a matched typeface to the face text draws with. Entries are keyed weakly by the matched typeface, so the faces
// made for fonts a closed project loaded go with them. Shared by a root and its forks, so it locks for itself.
internal sealed class FaceCache(SKFontManager? manager)
{
    private readonly Lock _gate = new();
    private readonly ConditionalWeakTable<SKTypeface, SKTypeface> _renderFaces = new();
    private readonly ConditionalWeakTable<SKTypeface, WeightInstances> _weights = new();

    public SKTypeface Get(SKTypeface typeface, FontWeight weight)
    {
        lock (_gate) return InstantiateWeight(GetRenderFaceCore(typeface), weight);
    }

    // On Windows the face is FreeType's, created from the same font data (see FreeTypeFonts); without a manager it is
    // the typeface itself.
    public SKTypeface GetRenderFace(SKTypeface typeface)
    {
        lock (_gate) return GetRenderFaceCore(typeface);
    }

    private SKTypeface GetRenderFaceCore(SKTypeface typeface)
    {
        if (manager is null)
        {
            return typeface;
        }

        if (!_renderFaces.TryGetValue(typeface, out SKTypeface? face))
        {
            // CreateTypeface takes the stream over. A font the manager cannot open keeps drawing as is.
            SKStreamAsset? stream = typeface.OpenStream(out int index);
            face = (stream is null ? null : manager.CreateTypeface(stream, index)) ?? typeface;
            _renderFaces.Add(typeface, face);
        }

        return face;
    }

    // A variable font registers once, as its default instance, so every weight resolves to that instance.
    // Move the matched typeface along its wght axis to the requested weight instead; a typeface without the
    // axis, or already at that weight, is returned as is.
    private SKTypeface InstantiateWeight(SKTypeface typeface, FontWeight weight)
    {
        WeightInstances instances = _weights.GetValue(typeface, static face => new WeightInstances(WeightAxis.Find(face)));
        if (instances.Axis is not { } weightAxis)
        {
            return typeface;
        }

        // Skia clamps to the axis as well; clamping first keeps a single instance per rendered weight.
        float value = Math.Clamp((float)weight, weightAxis.Min, weightAxis.Max);
        if (value == weightAxis.Position)
        {
            return typeface;
        }

        if (!instances.Faces.TryGetValue(value, out SKTypeface? instance))
        {
            ReadOnlySpan<SKFontVariationPositionCoordinate> position = [new() { Axis = WeightAxis.Tag, Value = value }];
            instance = typeface.Clone(position) ?? typeface;
            instances.Faces.Add(value, instance);
        }

        return instance;
    }

    // Axis is null when the typeface has no wght axis; Faces holds its instances by wght value.
    private sealed class WeightInstances(WeightAxis? axis)
    {
        public WeightAxis? Axis { get; } = axis;

        public Dictionary<float, SKTypeface> Faces { get; } = [];
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

    public static TValue Get<TValue>(this IReadOnlyDictionary<Typeface, TValue> typefaces, Typeface key)
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

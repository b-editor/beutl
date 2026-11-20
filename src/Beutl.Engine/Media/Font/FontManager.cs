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

// A manager made by the constructor holds the installed fonts and the active project's palette, which follows
// LoadProjectFonts. ForProject returns a scope of it that shares the installed fonts but always resolves one
// project's bundled fonts, so a render or export holding the scope is unaffected when another project becomes
// active, or when its own project closes. Every scope of a manager shares its gate.
public sealed class FontManager
{
    private readonly ILogger _logger = Log.CreateLogger<FontManager>();
    private readonly Lock _gate;
    // The manager this one is a project scope of; null for a manager made by the constructor.
    private readonly FontManager? _parent;
    // Root only. Every revision of a root and its scopes is drawn from this counter, so a manager's Revision,
    // the largest of the revisions it depends on, changes whenever one of them does.
    private long _revisionSource;
    // Root only: installed fonts and fallback order, shared with every scope.
    private long _catalogRevision;
    // Root only: the last switch of the active project palette.
    private long _paletteRevision;
    // Root: the active project palette, replaced by LoadProjectFonts. Scope: its project's fonts.
    private ProjectFontSet? _projectFonts;
    // Root only. Holds a scope while its project is alive; the scope does not keep the project alive.
    private readonly ConditionalWeakTable<Project, FontManager>? _scopes;
    // Project faces by content hash and face index. Weak, so a closed project's faces go once nothing
    // (a scope, the palette, or text drawn with them) holds them, and a reopened project shares the live ones.
    private readonly Dictionary<string, WeakReference<SKTypeface>> _projectFaceCache;
    internal readonly Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> _fonts;
    internal readonly Dictionary<FontFamily, FontName> _fontNames;
    private readonly HashSet<FontFamily> _reportedMissingFamilies;
    private readonly FaceCache _faces;
    private readonly string[] _fontDirs;
    private FontFamily[] _fallbackFamilies = [];
    private FontFamily? _emojiFamily;
    private readonly Dictionary<FontFamily, Dictionary<Typeface, Lazy<SKTypeface?>>> _deferredFonts;
    // macOS faces after the first of a collection, loaded with their family on its first lookup.
    private readonly Dictionary<FontFamily, List<(FontCollection Collection, int Index)>> _lazyFaces;

    public FontManager(FontConfig configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _gate = new();
        _scopes = new();
        _projectFaceCache = [];
        _fonts = [];
        _fontNames = [];
        _reportedMissingFamilies = [];
        _faces = new FaceCache(FreeTypeFonts.Manager);
        _deferredFonts = [];
        _lazyFaces = [];
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

    private FontManager(FontManager parent, ProjectFontSet projectFonts)
    {
        _parent = parent;
        _gate = parent._gate;
        _projectFaceCache = parent._projectFaceCache;
        _fonts = parent._fonts;
        _fontNames = parent._fontNames;
        _reportedMissingFamilies = parent._reportedMissingFamilies;
        _faces = parent._faces;
        _deferredFonts = parent._deferredFonts;
        _lazyFaces = parent._lazyFaces;
        _fontDirs = parent._fontDirs;
        _projectFonts = projectFonts;
        DefaultTypeface = parent.DefaultTypeface;
    }

    private FontManager Root => _parent ?? this;

    // Compared for inequality by the text caches; it never returns to an earlier value.
    internal long Revision
    {
        get
        {
            long revision = Math.Max(Volatile.Read(ref Root._catalogRevision), Volatile.Read(ref _paletteRevision));
            return Volatile.Read(ref _projectFonts) is { } projectFonts
                ? Math.Max(revision, Volatile.Read(ref projectFonts.Revision))
                : revision;
        }
    }

    private long NextRevision() => Interlocked.Increment(ref Root._revisionSource);

    // Callers hold _gate.
    private void BumpCatalogRevision() => Volatile.Write(ref Root._catalogRevision, NextRevision());

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
            lock (_gate)
            {
                return EnumerateFamilyKeys().Distinct().ToArray();
            }
        }
    }

    public int FontFamilyCount
    {
        get
        {
            lock (_gate)
            {
                return EnumerateFamilyKeys().Distinct().Count();
            }
        }
    }

    // Callers hold _gate.
    private IEnumerable<FontFamily> EnumerateFamilyKeys()
    {
        IEnumerable<FontFamily> project = [], lazyProject = [];
        if (_projectFonts is { } projectFonts)
        {
            project = projectFonts.Families.Keys;
            lazyProject = projectFonts.LazyFaces.Keys;
        }
        return _fonts.Keys.Concat(project).Concat(_deferredFonts.Keys).Concat(_lazyFaces.Keys).Concat(lazyProject);
    }

    public Typeface DefaultTypeface { get; }

    // The folders the installed fonts were read from at startup; a later change to the settings applies after restart.
    internal IReadOnlyList<string> FontDirectories => _fontDirs;

    internal FontName? GetFontName(FontFamily family)
    {
        lock (_gate)
        {
            return _fontNames.GetValueOrDefault(family);
        }
    }

    internal void SetFallbackFonts(IEnumerable<FontFamily> families, FontFamily? emojiFamily)
    {
        if (_parent is not null)
        {
            _parent.SetFallbackFonts(families, emojiFamily);
            return;
        }

        FontFamily[] fallbackFamilies = families.Distinct().ToArray();
        lock (_gate)
        {
            if (_fallbackFamilies.SequenceEqual(fallbackFamilies) && _emojiFamily == emojiFamily)
                return;

            _fallbackFamilies = fallbackFamilies;
            _emojiFamily = emojiFamily;
            BumpCatalogRevision();
        }
    }

    internal (FontFamily[] Families, FontFamily? Emoji) GetFallbackFamilies()
    {
        if (_parent is not null)
            return _parent.GetFallbackFamilies();

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
            _parent.RegisterFont(typeface, openStream);
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
            BumpCatalogRevision();
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
                face = _faces.Get(face, typeface.Weight);
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

    /// <summary>
    /// Returns the manager that resolves <paramref name="project"/>'s bundled fonts ahead of the installed ones,
    /// whichever project is active.
    /// </summary>
    /// <remarks>
    /// A project has one scope for as long as it is alive, so text laid out with it keeps its caches. The scope
    /// does not keep the project alive, and a render or export that captured it keeps resolving the project's
    /// fonts after the project closes or another one becomes active. <see cref="LoadProjectFonts"/> on either
    /// manager reads the project's fonts again for both.
    /// </remarks>
    public FontManager ForProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (_parent is not null)
            return IsScopeOf(project) ? this : _parent.ForProject(project);

        return GetOrCreateScope(project, out _);
    }

    // Root only.
    private FontManager GetOrCreateScope(Project project, out bool created)
    {
        lock (_gate)
        {
            if (_scopes!.TryGetValue(project, out FontManager? existing))
            {
                created = false;
                return existing;
            }
        }

        // Files are read outside the gate; a scope made meanwhile by another thread wins.
        var projectFonts = new ProjectFontSet(project);
        (projectFonts.Families, projectFonts.LazyFaces) = ReadProjectFonts(project);
        lock (_gate)
        {
            if (_scopes!.TryGetValue(project, out FontManager? existing))
            {
                created = false;
                return existing;
            }

            projectFonts.Revision = NextRevision();
            var scope = new FontManager(this, projectFonts);
            _scopes.Add(project, scope);
            created = true;
            return scope;
        }
    }

    private bool IsScopeOf(Project project)
        => _parent is not null && _projectFonts!.Owner.TryGetTarget(out Project? owner) && ReferenceEquals(owner, project);

    /// <summary>
    /// Reads <paramref name="project"/>'s bundled fonts again. On a manager made by the constructor, they also
    /// become the active palette, ahead of the installed fonts, until another project is loaded or
    /// <see cref="ClearProjectFonts"/>. A project scope accepts only its own project.
    /// </summary>
    public void LoadProjectFonts(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (_parent is not null)
        {
            if (!IsScopeOf(project))
                throw new ArgumentException("A project scope loads only its own project's fonts.", nameof(project));
            Reload(_projectFonts!, project);
            return;
        }

        FontManager scope = GetOrCreateScope(project, out bool created);
        ProjectFontSet projectFonts = scope._projectFonts!;
        if (!created)
            Reload(projectFonts, project);

        lock (_gate)
        {
            // A bundle's family takes precedence as a unit, including nearest-weight
            // matching. Keep system fonts separate so the next project can restore them.
            Volatile.Write(ref _projectFonts, projectFonts);
            Volatile.Write(ref _paletteRevision, NextRevision());
        }
    }

    private void Reload(ProjectFontSet projectFonts, Project project)
    {
        (Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> families,
            Dictionary<FontFamily, List<(FontCollection Collection, int Index)>> lazyFaces) = ReadProjectFonts(project);
        lock (_gate)
        {
            projectFonts.Families = families;
            projectFonts.LazyFaces = lazyFaces;
            Volatile.Write(ref projectFonts.Revision, NextRevision());
        }
    }

    /// <summary>
    /// Clears the active palette, or, with <paramref name="expectedProject"/>, only while that project is active.
    /// A scope's fonts stay in place; a project scope has no palette to clear.
    /// </summary>
    public void ClearProjectFonts(Project? expectedProject = null)
    {
        if (_parent is not null)
            throw new InvalidOperationException("A project scope always resolves its own project's fonts.");

        lock (_gate)
        {
            if (_projectFonts is null)
                return;
            if (expectedProject is not null
                && (!_projectFonts.Owner.TryGetTarget(out Project? owner) || !ReferenceEquals(owner, expectedProject)))
                return;
            Volatile.Write(ref _projectFonts, null);
            Volatile.Write(ref _paletteRevision, NextRevision());
        }
    }

    private (Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> Families,
        Dictionary<FontFamily, List<(FontCollection Collection, int Index)>> LazyFaces) ReadProjectFonts(Project project)
    {
        var fonts = new List<SKTypeface>();
        var lazyFaces = new Dictionary<FontFamily, List<(FontCollection Collection, int Index)>>();
        if (project.Uri is { IsFile: true } uri)
        {
            string directory = Path.Combine(Path.GetDirectoryName(uri.LocalPath)!, "resources", "fonts");
            if (Directory.Exists(directory))
            {
                PruneProjectFaceCache();
                foreach (string file in EnumerateFontCandidates(directory))
                    fonts.AddRange(LoadProjectFontFaces(file, lazyFaces));
            }
        }

        return (fonts.GroupBy(font => font.FamilyName).ToDictionary(
            group => new FontFamily(group.Key), group => TypefaceCollection.Create(group.ToArray())), lazyFaces);
    }

    public void AddFont(Stream stream)
    {
        if (_parent is not null)
        {
            _parent.AddFont(stream);
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
            BumpCatalogRevision();
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

    // Projects, and the same project read again, share the faces of the same file while any of them is alive.
    private SKTypeface? LoadProjectFace(string hash, int index, Func<SKTypeface?> load)
    {
        string key = hash + ":" + index;
        lock (_gate)
        {
            if (_projectFaceCache.TryGetValue(key, out WeakReference<SKTypeface>? cached)
                && cached.TryGetTarget(out SKTypeface? live))
                return live;
            SKTypeface? face = load();
            if (face is not null) _projectFaceCache[key] = new WeakReference<SKTypeface>(face);
            return face;
        }
    }

    private void PruneProjectFaceCache()
    {
        lock (_gate)
        {
            foreach ((string key, WeakReference<SKTypeface> face) in _projectFaceCache)
            {
                if (!face.TryGetTarget(out _))
                    _projectFaceCache.Remove(key);
            }
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
        if (_projectFonts is { } projectFonts && projectFonts.LazyFaces.Remove(family, out var projectFaces))
        {
            foreach (IGrouping<string, SKTypeface> loaded in projectFaces
                         .Select(lazy => LoadProjectFace(lazy.Collection.Hash!, lazy.Index, () => LoadLazyFace(lazy.Collection, lazy.Index)))
                         .OfType<SKTypeface>()
                         .GroupBy(face => face.FamilyName))
            {
                var loadedFamily = new FontFamily(loaded.Key);
                SKTypeface[] known = projectFonts.Families.TryGetValue(loadedFamily, out var existing) ? [.. existing.Values] : [];
                projectFonts.Families[loadedFamily] = TypefaceCollection.Create([.. known, .. loaded]);
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
            if (_projectFonts?.Families.TryGetValue(fontFamily, out var project) == true)
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
                return _faces.Get(face, typeface.Weight);
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
                ? _faces.Get(fallback, typeface.Weight)
                : _faces.GetRenderFace(SKTypeface.Default);
        }
    }

    // Test-only (via InternalsVisibleTo): the face a registered typeface renders with.
    internal SKTypeface GetRenderFace(SKTypeface typeface)
    {
        lock (_gate) return _faces.GetRenderFace(typeface);
    }

    private bool TryResolveTypeface(Typeface typeface, out SKTypeface face)
    {
        LoadLazyFaces(typeface.FontFamily);
        if (_projectFonts?.Families.TryGetValue(typeface.FontFamily, out var project) == true)
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

    public bool IsRegistered(FontFamily fontFamily)
    {
        lock (_gate)
        {
            return _fonts.ContainsKey(fontFamily) || _deferredFonts.ContainsKey(fontFamily) || _lazyFaces.ContainsKey(fontFamily)
                || _projectFonts?.Families.ContainsKey(fontFamily) == true || _projectFonts?.LazyFaces.ContainsKey(fontFamily) == true;
        }
    }
}

// A project's bundled fonts, shared by its scope and, while it is active, the root's palette. Fields change
// under the manager's gate.
internal sealed class ProjectFontSet(Project owner)
{
    public readonly WeakReference<Project> Owner = new(owner);
    public Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> Families = [];
    // macOS faces after the first of a collection, loaded with their family on its first lookup.
    public Dictionary<FontFamily, List<(FontCollection Collection, int Index)>> LazyFaces = [];
    public long Revision;
}

// Maps a matched typeface to the face text draws with. Entries are keyed weakly by the matched typeface, so the
// faces made for a closed project's fonts go with them. FontManager holds its gate.
internal sealed class FaceCache(SKFontManager? manager)
{
    private readonly ConditionalWeakTable<SKTypeface, SKTypeface> _renderFaces = new();
    private readonly ConditionalWeakTable<SKTypeface, WeightInstances> _weights = new();

    public SKTypeface Get(SKTypeface typeface, FontWeight weight) => InstantiateWeight(GetRenderFace(typeface), weight);

    // On Windows the face is FreeType's, created from the same font data (see FreeTypeFonts); without a manager it
    // is the typeface itself.
    public SKTypeface GetRenderFace(SKTypeface typeface)
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

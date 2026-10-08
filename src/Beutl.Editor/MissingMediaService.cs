using System.Collections.Concurrent;
using System.Security.Cryptography;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using SkiaSharp;

namespace Beutl.Editor;

public enum MissingMediaKind { Video, Sound, Image, Model, Cube, Font }

public sealed record MissingMediaReference(IFileSource? Source, Element? Element);

public sealed record MissingMedia(
    MissingMediaKind Kind,
    Uri? ExpectedUri,
    FontFamily? FontFamily,
    IReadOnlyList<MissingMediaReference> References,
    MediaFileFingerprint? Fingerprint)
{
    public string Name => ExpectedUri is { } uri ? Path.GetFileName(uri.LocalPath) : FontFamily!.Name;
}

/// <summary>Finds missing references without dropping their original URIs.</summary>
public sealed class MissingMediaService
{
    private readonly Func<FontFamily, bool> _fontExists;
    private readonly Func<string, IEnumerable<string>> _enumerateEntries;
    private readonly ConcurrentDictionary<(MissingMediaKind Kind, Uri Uri),
        (HashSet<IFileSource> Sources, MediaFileFingerprint? Fingerprint)> _validated = new();

    public MissingMediaService() : this(font => FontManager.Instance.IsRegistered(font)) { }

    internal MissingMediaService(Func<FontFamily, bool> fontExists, Func<string, IEnumerable<string>>? enumerateEntries = null)
    {
        _fontExists = fontExists;
        _enumerateEntries = enumerateEntries ?? (directory => Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }));
    }

    public IReadOnlyList<MissingMedia> FindMissing(Scene scene)
        => FindMissingAsync(scene).GetAwaiter().GetResult();

    public Task<IReadOnlyList<MissingMedia>> FindMissingAsync(Scene scene, CancellationToken token = default,
        bool revalidateExisting = true)
    {
        // Capture the editable graph before moving filesystem probes and decoders
        // off the UI thread. Worker tasks never traverse mutable engine objects.
        var references = CaptureReferences(scene);
        var current = references.Where(item => item.ExpectedUri != null).Select(item => (item.Kind, item.ExpectedUri!)).ToHashSet();
        foreach (var key in _validated.Keys.Where(key => !current.Contains(key))) _validated.TryRemove(key, out _);
        return Task.Run<IReadOnlyList<MissingMedia>>(async () =>
        {
            var missing = new List<MissingMedia>();
            foreach (var item in references)
            {
                token.ThrowIfCancellationRequested();
                if (item.FontFamily is { } font)
                {
                    if (!_fontExists(font)) missing.Add(item);
                    continue;
                }
                var key = (item.Kind, item.ExpectedUri!);
                var sources = item.References.Select(reference => reference.Source!)
                    .ToHashSet<IFileSource>(ReferenceEqualityComparer.Instance);
                if (!revalidateExisting && _validated.TryGetValue(key, out var cached)
                    && cached.Fingerprint == item.Fingerprint && cached.Sources.SetEquals(sources)) continue;
                try
                {
                    await ValidateAsync(item, item.ExpectedUri!.LocalPath, token);
                    token.ThrowIfCancellationRequested();
                    _validated[key] = (sources, item.Fingerprint);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { _validated.TryRemove(key, out _); missing.Add(item); }
            }
            token.ThrowIfCancellationRequested();
            return missing;
        }, token);
    }

    private static IReadOnlyList<MissingMedia> CaptureReferences(Scene scene)
    {
        var groups = new Dictionary<(MissingMediaKind Kind, string Key), List<MissingMediaReference>>();
        var values = new Dictionary<(MissingMediaKind Kind, string Key), (Uri? Uri, FontFamily? Font)>();
        var searcher = new ObjectSearcher(scene, (stack, value) =>
        {
            Uri? uri = GetUri(value);
            FontFamily? font = value as FontFamily;
            MissingMediaKind? kind = GetKind(value);
            if (kind == null) return false;
            string key;
            if (uri is { IsAbsoluteUri: true, IsFile: true })
            {
                key = uri.AbsoluteUri;
            }
            else if (font != null)
            {
                key = font.Name;
            }
            else return false;

            var groupKey = (kind.Value, key);
            if (!groups.TryGetValue(groupKey, out var references))
            {
                groups[groupKey] = references = [];
                values[groupKey] = (uri, font);
            }
            references.Add(new MissingMediaReference(value as IFileSource, stack.OfType<Element>().FirstOrDefault()));
            return false;
        });
        searcher.SearchAll();
        return groups.Select(pair =>
        {
            var value = values[pair.Key];
            scene.MediaFingerprints.TryGetValue(pair.Key.Key, out var fingerprint);
            return new MissingMedia(pair.Key.Kind, value.Uri, value.Font, pair.Value, fingerprint);
        }).OrderBy(item => item.Kind).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static Uri[] GetFileUris(Scene scene) => new ObjectSearcher(scene, value => GetUri(value) is { IsFile: true })
        .SearchAll().Select(value => GetUri(value)!).Distinct().ToArray();

    internal static HashSet<(object Source, Uri? Uri)> GetReferenceKeys(Scene scene)
        => new ObjectSearcher(scene, value => value is FontFamily || GetUri(value) is { IsFile: true })
            .SearchAll().Select(value => (value, GetUri(value))).ToHashSet();

    private static Uri? GetUri(object value) => value switch
    {
        MediaSource { HasUri: true } source => source.Uri,
        ModelSource { HasUri: true } source => source.Uri,
        _ => null
    };

    public static MissingMediaKind? GetKind(object value) => value switch
    {
        VideoSource => MissingMediaKind.Video,
        SoundSource => MissingMediaKind.Sound,
        ImageSource => MissingMediaKind.Image,
        ModelSource => MissingMediaKind.Model,
        CubeSource => MissingMediaKind.Cube,
        FontFamily => MissingMediaKind.Font,
        _ => null
    };

    public async Task<IReadOnlyDictionary<MissingMedia, IReadOnlyList<string>>> FindMatchesAsync(
        IEnumerable<MissingMedia> missing, string directory, CancellationToken token = default)
    {
        MissingMedia[] items = missing.ToArray();
        return await Task.Run(async () =>
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var bySize = new Dictionary<long, List<string>>();
            var fonts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            bool findFonts = items.Any(item => item.Kind == MissingMediaKind.Font);
            foreach (string path in EnumerateMediaFiles(directory, token))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    Add(byName, Path.GetFileName(path), path);
                    Add(bySize, new FileInfo(path).Length, path);
                    if (findFonts && IsFontFile(path))
                    {
                        foreach (var face in FontManager.OpenFontFaces(path))
                            using (face)
                                if (!fonts.TryGetValue(face.FamilyName, out var paths) || !paths.Contains(path))
                                    Add(fonts, face.FamilyName, path);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            var matches = new Dictionary<MissingMedia, IReadOnlyList<string>>();
            foreach (MissingMedia item in items)
            {
                token.ThrowIfCancellationRequested();
                if (item.FontFamily is { } font)
                {
                    if (fonts.TryGetValue(font.Name, out var fontPaths) && fontPaths.Count > 0)
                        matches[item] = fontPaths.Order(StringComparer.Ordinal).ToArray();
                    continue;
                }

                byName.TryGetValue(item.Name, out var named);
                if (item.Fingerprint is not { } fingerprint)
                {
                    if (named is { Count: 1 }) matches[item] = [named[0]];
                    continue;
                }

                // With a saved fingerprint, even a unique name must have the right content.
                // Size-indexed fallback also finds files that have been renamed.
                if (!bySize.TryGetValue(fingerprint.Length, out var sameSize)) continue;
                var candidates = sameSize;
                var matchingHashes = new List<string>();
                foreach (string path in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (await MatchesFingerprintAsync(item, path, token: token, hashes: hashes))
                            matchingHashes.Add(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
                }
                // Identical copies are interchangeable; prefer the original name.
                string? match = matchingHashes.FirstOrDefault(path => named?.Contains(path) == true)
                                ?? matchingHashes.FirstOrDefault();
                if (match != null) matches[item] = [match];
            }
            return (IReadOnlyDictionary<MissingMedia, IReadOnlyList<string>>)matches;
        }, token);
    }

    public Task<IReadOnlyList<string>> FindFontFamilyFilesAsync(FontFamily family, string directory, CancellationToken token = default,
        string? selectedPath = null)
        => Task.Run<IReadOnlyList<string>>(() => SelectFontFamilyFiles(family,
            EnumerateMediaFiles(directory, token).Where(IsFontFile), selectedPath, token), token);

    public Task<IReadOnlyList<string>> SelectFontFamilyFilesAsync(FontFamily family, string selectedPath,
        IEnumerable<string> candidates, CancellationToken token = default)
        => Task.Run<IReadOnlyList<string>>(() => SelectFontFamilyFiles(family, candidates, selectedPath, token), token);

    private static IReadOnlyList<string> SelectFontFamilyFiles(FontFamily family, IEnumerable<string> candidates,
        string? selectedPath, CancellationToken token)
    {
        var result = new List<string>();
        var accepted = new HashSet<(int Style, int Weight)>();
        var paths = candidates.Order(StringComparer.Ordinal);
        foreach (string path in (selectedPath == null ? paths : new[] { selectedPath }.Concat(paths)).Distinct(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var faces = new HashSet<(int Style, int Weight)>();
            try
            {
                foreach (var face in FontManager.OpenFontFaces(path))
                    using (face)
                        if (string.Equals(face.FamilyName, family.Name, StringComparison.Ordinal))
                            faces.Add(((int)face.FontSlant, face.FontWeight));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            // Collections cannot be copied one face at a time. Do not bundle a
            // conflicting file that could override the user's selected face.
            if (faces.Count == 0 || faces.Overlaps(accepted)) continue;
            accepted.UnionWith(faces);
            result.Add(path);
        }
        return result;
    }

    internal static async Task<bool> MatchesFingerprintAsync(MissingMedia item, string path, ModelSource? model = null,
        CancellationToken token = default, Dictionary<string, string>? hashes = null)
    {
        if (item.Fingerprint is not { } expected) return false;
        async Task<bool> MatchesFileAsync(string file, MediaFileFingerprint fingerprint)
        {
            if (new FileInfo(file).Length != fingerprint.Length) return false;
            string hash;
            if (hashes is null || !hashes.TryGetValue(file, out hash!))
            {
                hash = await HashFileAsync(file, token);
                if (hashes is not null) hashes[file] = hash;
            }
            return string.Equals(hash, fingerprint.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        if (!await MatchesFileAsync(path, expected)) return false;
        if (item.Kind != MissingMediaKind.Model) return true;
        if (model is null) { model = new ModelSource(); model.ReadFrom(new Uri(Path.GetFullPath(path))); }
        string[] dependencies = model.Dependencies.Where(file => !FilePathComparison.AreSameCanonicalPath(file, path)).ToArray();
        // Old metadata cannot prove the identity of an external dependency bundle.
        if (expected.Dependencies is null) return dependencies.Length == 0;
        if (dependencies.Length != expected.Dependencies.Count) return false;
        foreach (string dependency in dependencies)
        {
            string relative = Path.GetRelativePath(Path.GetDirectoryName(path)!, dependency).Replace('\\', '/');
            if (!expected.Dependencies.TryGetValue(relative, out var fingerprint)
                || !await MatchesFileAsync(dependency, fingerprint)) return false;
        }
        return true;
    }

    private IEnumerable<string> EnumerateMediaFiles(string root, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            foreach (string entry in EnumerateReadableEntries(directory, token))
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else yield return entry;
            }
        }
    }

    private IEnumerable<string> EnumerateReadableEntries(string directory, CancellationToken token)
    {
        IEnumerator<string> entries;
        try { entries = _enumerateEntries(directory).GetEnumerator(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        using (entries)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool hasNext;
                try { hasNext = entries.MoveNext(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
                if (!hasNext) yield break;
                yield return entries.Current;
            }
        }
    }

    private static void Add<TKey>(Dictionary<TKey, List<string>> index, TKey key, string path) where TKey : notnull
    {
        if (!index.TryGetValue(key, out var paths)) index[key] = paths = [];
        paths.Add(path);
    }

    private static bool IsFontFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".ttc" or ".otf";

    /// <summary>Decodes a candidate before any live source is changed.</summary>
    public Task<IFileSource?> ValidateAsync(MissingMedia item, string path, CancellationToken token = default)
        => Task.Run<IFileSource?>(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(path)) throw new FileNotFoundException("Media file not found.", path);
            switch (item.Kind)
            {
                case MissingMediaKind.Image:
                    using (var bitmap = Bitmap.FromFile(path))
                        if (bitmap.Width <= 0 || bitmap.Height <= 0) throw new InvalidDataException("Invalid image.");
                    break;
                case MissingMediaKind.Video:
                case MissingMediaKind.Sound:
                    using (var reader = MediaReader.Open(path, new MediaOptions(item.Kind == MissingMediaKind.Video ? MediaMode.Video : MediaMode.Audio)))
                        if (item.Kind == MissingMediaKind.Video ? !reader.HasVideo : !reader.HasAudio)
                            throw new InvalidDataException("The file does not contain the required media stream.");
                    break;
                case MissingMediaKind.Model:
                    var model = new ModelSource();
                    model.ReadFrom(new Uri(Path.GetFullPath(path)));
                    if (model.MeshCount == 0) throw new InvalidDataException("The file does not contain model geometry.");
                    var dependencies = model.Dependencies.AsEnumerable();
                    // Assimp can omit optional material libraries. A saved bundle
                    // still requires them when validating its original location.
                    if (item.Fingerprint?.Dependencies is { } saved && item.ExpectedUri is { IsFile: true } expected
                        && FilePathComparison.AreSameCanonicalPath(path, expected.LocalPath))
                        dependencies = dependencies.Concat(saved.Keys.Select(relative => Path.GetFullPath(relative, Path.GetDirectoryName(path)!)));
                    foreach (string dependency in dependencies.Distinct(StringComparer.Ordinal))
                    {
                        token.ThrowIfCancellationRequested();
                        using var stream = File.OpenRead(dependency);
                    }
                    return model;
                case MissingMediaKind.Cube:
                    using (var stream = File.OpenRead(path)) CubeFile.FromStream(stream);
                    break;
                case MissingMediaKind.Font:
                    bool found = false;
                    foreach (var face in FontManager.OpenFontFaces(path))
                        using (face)
                            if (string.Equals(face.FamilyName, item.FontFamily!.Name, StringComparison.Ordinal)) found = true;
                    if (!found) throw new InvalidDataException("Choose a font file belonging to the missing font family.");
                    break;
            }
            return null;
        }, token);

    public async Task UpdateFingerprintsAsync(Scene scene, CancellationToken token = default, bool force = true)
    {
        Uri[] uris = GetFileUris(scene);
        var previous = new Dictionary<string, MediaFileFingerprint>(scene.MediaFingerprints);
        var modelDependencies = new ObjectSearcher(scene, value => value is ModelSource { HasUri: true, MeshCount: > 0 })
            .SearchAll().OfType<ModelSource>().GroupBy(model => model.Uri.AbsoluteUri)
            .ToDictionary(group => group.Key, group => group.First().Dependencies
                .Where(path => !FilePathComparison.AreSameCanonicalPath(path, group.First().Uri.LocalPath)).ToArray());
        var updated = await Task.Run(async () =>
        {
            var result = new Dictionary<string, MediaFileFingerprint>(StringComparer.Ordinal);
            foreach (Uri uri in uris)
            {
                token.ThrowIfCancellationRequested();
                previous.TryGetValue(uri.AbsoluteUri, out var fingerprint);
                try
                {
                    // Ordinary reference scans reuse completed hashes. Explicit
                    // saves/audits force reads, including timestamp-preserving overwrites.
                    if ((force || fingerprint == null) && File.Exists(uri.LocalPath))
                    {
                        MediaFileFingerprint next = await CaptureFileFingerprintAsync(uri.LocalPath, fingerprint, token);
                        if (modelDependencies.TryGetValue(uri.AbsoluteUri, out var paths))
                        {
                            // The same manifest may load with optional sidecars
                            // absent. Do not erase those saved bundle members.
                            if (next.Sha256 == fingerprint?.Sha256 && fingerprint.Dependencies is { } saved)
                                paths = paths.Concat(saved.Keys.Select(relative => Path.GetFullPath(relative, Path.GetDirectoryName(uri.LocalPath)!)))
                                    .Distinct(StringComparer.Ordinal).ToArray();
                            var dependencies = new Dictionary<string, MediaFileFingerprint>(StringComparer.Ordinal);
                            foreach (string path in paths)
                            {
                                string relative = Path.GetRelativePath(Path.GetDirectoryName(uri.LocalPath)!, path).Replace('\\', '/');
                                MediaFileFingerprint? old = null;
                                fingerprint?.Dependencies?.TryGetValue(relative, out old);
                                dependencies[relative] = await CaptureFileFingerprintAsync(path, old, token);
                            }
                            var retained = fingerprint?.Dependencies;
                            if (retained is null || retained.Count != dependencies.Count
                                || dependencies.Any(pair => !retained.TryGetValue(pair.Key, out var old) || old != pair.Value))
                                retained = dependencies;
                            next = next with { Dependencies = retained };
                        }
                        fingerprint = next;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (fingerprint != null) result[uri.AbsoluteUri] = fingerprint;
            }
            return result;
        }, token);
        token.ThrowIfCancellationRequested();
        // Edits can occur while hashing. Merge only references still in the live graph
        // and preserve newer entries, including fingerprints for offline sources.
        var current = GetFileUris(scene).Select(uri => uri.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
        foreach (string key in scene.MediaFingerprints.Keys.Where(key => !current.Contains(key)).ToArray())
            scene.MediaFingerprints.Remove(key);
        foreach (var pair in updated)
        {
            if (current.Contains(pair.Key) && (!scene.MediaFingerprints.TryGetValue(pair.Key, out var existing)
                || previous.TryGetValue(pair.Key, out var old) && existing == old))
                scene.MediaFingerprints[pair.Key] = pair.Value;
        }
    }

    private static async Task<MediaFileFingerprint> CaptureFileFingerprintAsync(string path, MediaFileFingerprint? previous, CancellationToken token)
    {
        var file = new FileInfo(path);
        // Size and mtime can survive an overwrite; they do not establish content identity.
        string hash = await HashFileAsync(file.FullName, token);
        return new(file.Length, file.LastWriteTimeUtc.Ticks, hash)
        {
            // Retain the bundle manifest while its source is offline and cannot
            // repopulate dependencies, provided the main file still matches.
            Dependencies = previous?.Sha256 == hash ? previous.Dependencies : null
        };
    }

    internal static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
}

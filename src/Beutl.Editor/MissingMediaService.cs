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

    public MissingMediaService() : this(font => FontManager.Instance.IsRegistered(font)) { }

    internal MissingMediaService(Func<FontFamily, bool> fontExists) => _fontExists = fontExists;

    public IReadOnlyList<MissingMedia> FindMissing(Scene scene)
    {
        var groups = new Dictionary<(MissingMediaKind Kind, string Key), List<MissingMediaReference>>();
        var values = new Dictionary<(MissingMediaKind Kind, string Key), (Uri? Uri, FontFamily? Font)>();
        var fileExists = new Dictionary<string, bool>(StringComparer.Ordinal);
        var fontExists = new Dictionary<FontFamily, bool>();
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
                if (!fileExists.TryGetValue(key, out bool exists))
                    fileExists[key] = exists = File.Exists(uri.LocalPath);
                if (exists) return false;
            }
            else if (font != null)
            {
                key = font.Name;
                if (!fontExists.TryGetValue(font, out bool exists))
                    fontExists[font] = exists = _fontExists(font);
                if (exists) return false;
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

    private static Uri? GetUri(object value) => value switch
    {
        MediaSource { HasUri: true } source => source.Uri,
        ModelSource { HasUri: true } source => source.Uri,
        _ => null
    };

    private static MissingMediaKind? GetKind(object value) => value switch
    {
        VideoSource => MissingMediaKind.Video,
        SoundSource => MissingMediaKind.Sound,
        ImageSource => MissingMediaKind.Image,
        ModelSource => MissingMediaKind.Model,
        CubeSource => MissingMediaKind.Cube,
        FontFamily => MissingMediaKind.Font,
        _ => null
    };

    public async Task<IReadOnlyDictionary<MissingMedia, string>> FindMatchesAsync(
        IEnumerable<MissingMedia> missing, string directory, CancellationToken token = default)
    {
        MissingMedia[] items = missing.ToArray();
        return await Task.Run(async () =>
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            // Scan once, skip inaccessible subtrees and symlink loops.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var bySize = new Dictionary<long, List<string>>();
            var fonts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            bool findFonts = items.Any(item => item.Kind == MissingMediaKind.Font);
            foreach (string path in Directory.EnumerateFiles(directory, "*", options))
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
            var matches = new Dictionary<MissingMedia, string>();
            foreach (MissingMedia item in items)
            {
                token.ThrowIfCancellationRequested();
                if (item.FontFamily is { } font)
                {
                    if (fonts.TryGetValue(font.Name, out var fontPaths) && fontPaths.Count == 1)
                        matches[item] = fontPaths[0];
                    continue;
                }

                byName.TryGetValue(item.Name, out var named);
                if (item.Fingerprint is not { } fingerprint)
                {
                    if (named is { Count: 1 }) matches[item] = named[0];
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
                        if (!hashes.TryGetValue(path, out var hash))
                            hashes[path] = hash = await HashFileAsync(path, token);
                        if (string.Equals(hash, fingerprint.Sha256, StringComparison.OrdinalIgnoreCase))
                            matchingHashes.Add(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                // Identical copies are interchangeable; prefer the original name.
                string? match = matchingHashes.FirstOrDefault(path => named?.Contains(path) == true)
                                ?? matchingHashes.FirstOrDefault();
                if (match != null) matches[item] = match;
            }
            return (IReadOnlyDictionary<MissingMedia, string>)matches;
        }, token);
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
                    return model;
                case MissingMediaKind.Cube:
                    using (var stream = File.OpenRead(path)) CubeFile.FromStream(stream);
                    break;
                case MissingMediaKind.Font:
                    bool found = false;
                    foreach (var face in FontManager.OpenFontFaces(path))
                        using (face)
                            if (string.Equals(face.FamilyName, item.FontFamily!.Name, StringComparison.OrdinalIgnoreCase)) found = true;
                    if (!found) throw new InvalidDataException("Choose a font file belonging to the missing font family.");
                    break;
            }
            return null;
        }, token);

    public async Task UpdateFingerprintsAsync(Scene scene, CancellationToken token = default)
    {
        Uri[] uris = GetFileUris(scene);
        var previous = new Dictionary<string, MediaFileFingerprint>(scene.MediaFingerprints);
        var updated = await Task.Run(async () =>
        {
            var result = new Dictionary<string, MediaFileFingerprint>(StringComparer.Ordinal);
            foreach (Uri uri in uris)
            {
                token.ThrowIfCancellationRequested();
                previous.TryGetValue(uri.AbsoluteUri, out var fingerprint);
                try
                {
                    var file = new FileInfo(uri.LocalPath);
                    if (file.Exists && (fingerprint == null || fingerprint.Length != file.Length
                        || fingerprint.LastWriteTimeUtcTicks != file.LastWriteTimeUtc.Ticks))
                        fingerprint = new(file.Length, file.LastWriteTimeUtc.Ticks, await HashFileAsync(file.FullName, token));
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

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
}

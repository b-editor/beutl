using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

internal sealed record MediaRepairSelection(MissingMedia Media, string NewPath, IReadOnlyList<string>? FontFiles = null);

// A repair changes file references in place, so both the sources and their saved metadata need
// explicit rollback if the caller cannot save the repaired scene.
internal sealed class MissingMediaRepair(
    Scene scene, HistoryManager history, FontManager fonts, Action reloadFonts, MissingMediaRepair.Item[] items) : IDisposable
{
    internal sealed record Item(MediaRepairSelection Selection, IFileSource? ValidatedSource, bool SynchronizeModelGeometry);

    private static readonly ILogger s_logger = Log.CreateLogger<MissingMediaRepair>();
    private readonly Dictionary<string, MediaFileFingerprint> _fingerprints = new(scene.MediaFingerprints);
    private readonly List<Action> _rollback = [];
    private string? _fontsDirectory;
    private HashSet<string>? _originalFonts;
    private bool _committed;

    public MediaRepairSelection[] Applied { get; } = items.Select(item => item.Selection).ToArray();

    public static async Task<Item> ValidateAsync(MissingMediaService detector, MediaRepairSelection selection,
        CancellationToken token)
    {
        var (media, path, fontFiles) = selection;
        IFileSource? source = await detector.ValidateAsync(media, path, token);
        if (media.FontFamily is { } family)
        {
            var fresh = await detector.FindFontFamilyFilesAsync(family, Path.GetDirectoryName(path)!, token, path);
            fontFiles = await detector.SelectFontFamilyFilesAsync(family, path, (fontFiles ?? []).Concat(fresh), token);
            foreach (string file in fontFiles)
                await detector.ValidateAsync(media, file, token);
        }

        // Preserve the saved child meshes and their edits unless the fingerprint proves this is a different model.
        bool synchronize = media.Kind == MissingMediaKind.Model && media.Fingerprint is not null
            && !await MissingMediaService.MatchesFingerprintAsync(media, path, source as ModelSource, token);
        return new Item(selection with { FontFiles = fontFiles }, source, synchronize);
    }

    public static MissingMediaRepair? Create(Scene scene, HistoryManager history, FontManager fonts, Action reloadFonts,
        IReadOnlyList<MediaReference> references, IReadOnlyList<Item> items)
    {
        var current = references.Where(reference => reference.Value is IFileSource)
            .ToLookup(reference => (MissingMediaService.GetKind(reference.Value), ((IFileSource)reference.Value).Uri));
        var protectedSources = references.Where(reference => reference.Element?.IsRecoveryProtected == true)
            .Select(reference => reference.Value).ToHashSet(ReferenceEqualityComparer.Instance);

        var resolved = new List<Item>();
        foreach (Item item in items)
        {
            var media = item.Selection.Media;
            if (media.ExpectedUri is not { } uri)
            {
                resolved.Add(item);
                continue;
            }

            var matches = current[(media.Kind, uri)];
            if (!matches.Any()) return null;

            // A recovered element keeps its original bytes on save. Only fix sources that can be persisted.
            MissingMediaReference[] editable = matches.Where(reference => !protectedSources.Contains(reference.Value))
                .Select(reference => new MissingMediaReference((IFileSource)reference.Value, reference.Element)).ToArray();
            if (editable.Length > 0)
                resolved.Add(item with { Selection = item.Selection with { Media = media with { References = editable } } });
        }

        return new MissingMediaRepair(scene, history, fonts, reloadFonts, [.. resolved]);
    }

    public async Task ApplyAsync(CancellationToken token)
    {
        var fontItems = items.Where(item => item.Selection.Media.FontFamily is not null).ToArray();
        if (fontItems.Length > 0)
        {
            string projectDirectory = Path.GetDirectoryName((scene.FindHierarchicalParent<Project>()?.Uri ?? scene.Uri)!.LocalPath)!;
            _fontsDirectory = Path.Combine(projectDirectory, "resources", "fonts");
            _originalFonts = Directory.Exists(_fontsDirectory)
                ? Directory.GetFiles(_fontsDirectory).ToHashSet(StringComparer.Ordinal) : [];
            var paths = fontItems.ToDictionary(item => item.Selection.Media.FontFamily!.Name, item => item.Selection.FontFiles!);
            var relocation = new ResourceRelocationService(family => paths[family]);
            RelocationResult result = await relocation.RelocateFontsAsync(fonts,
                fontItems.Select(item => item.Selection.Media.FontFamily!), projectDirectory, token);
            if (result.FailedResources.Count > 0) throw new IOException(MissingMediaStrings.FontCopyFailed);
            reloadFonts();
        }

        token.ThrowIfCancellationRequested();
        using (history.SuppressRecording())
        {
            foreach (Item item in items.Where(item => item.Selection.Media.ExpectedUri is not null))
            {
                var uri = UriHelper.CreateFromPath(item.Selection.NewPath);
                foreach (IFileSource source in item.Selection.Media.References.Select(reference => reference.Source!)
                             .Distinct<IFileSource>(ReferenceEqualityComparer.Instance))
                {
                    token.ThrowIfCancellationRequested();
                    if (source is ModelSource model)
                    {
                        var snapshot = model.CaptureState();
                        _rollback.Add(() => model.RelinkFrom(snapshot));
                        if (model.FindHierarchicalParent<Model3D>() is { } owner)
                            _rollback.Add(owner.CaptureGeometryRollback());
                    }
                    else
                    {
                        Uri original = source.Uri;
                        _rollback.Add(() => source.ReadFrom(original));
                    }

                    ResourceRelocationService.RelinkFileSource(source, uri, item.ValidatedSource, item.SynchronizeModelGeometry);
                }
            }
        }
    }

    public void Commit() => _committed = true;

    public void Dispose()
    {
        if (_committed || (_rollback.Count == 0 && _originalFonts is null)) return;

        using (history.SuppressRecording())
            foreach (var restore in _rollback) restore();
        scene.SetMediaFingerprints(_fingerprints);
        if (_originalFonts is not null)
        {
            try
            {
                if (Directory.Exists(_fontsDirectory))
                    foreach (string file in Directory.GetFiles(_fontsDirectory!).Where(file => !_originalFonts.Contains(file)))
                        File.Delete(file);
                reloadFonts();
            }
            catch (Exception ex) { s_logger.LogWarning(ex, "Could not roll back copied repair fonts."); }
        }
    }
}

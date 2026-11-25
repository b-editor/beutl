using System.Globalization;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Services.Captions;

internal static class CaptionFileImport
{
    private static readonly Lazy<CaptionCatalog> s_catalog = new(() => CaptionCatalog.ComposeWithDefaultElementFactory(
        Strings.AiSubtitle_DefaultTemplate, [], CaptionPresentationDefaults.ElementFactory));

    // A file's cues keep the length it gives them, however short.
    private static readonly MediaTime s_shortestCue = MediaTime.FromSeconds(0.001);

    public static bool IsCaptionFile(string file) => s_catalog.Value.Codecs.TryGetByFileName(file, out _);

    // The default look and the text templates saved so far, which change while Beutl runs.
    public static IReadOnlyList<CaptionTemplate> GetTemplates(ObjectTemplateService templates)
    {
        CaptionCatalog catalog = CaptionCatalog.ComposeWithDefaultElementFactory(
            Strings.AiSubtitle_DefaultTemplate, CaptionCatalog.FindObjectTemplates(templates),
            CaptionPresentationDefaults.ElementFactory);
        return [.. catalog.Templates.Templates.Select(item => catalog.Templates.GetTemplate(item.Id))];
    }

    // Cues keep the times in their file. Each file gets its own layers above the rest of the import.
    public static async Task<IReadOnlyList<string>> AppendDescriptionsAsync(
        Scene scene,
        IReadOnlyList<string> files,
        List<ElementDescription> descriptions,
        CaptionTemplate template,
        INotificationService? notifications,
        CancellationToken cancellationToken)
    {
        int layer = 0;
        var skippedSome = new List<string>();
        foreach (string file in files)
        {
            if (!s_catalog.Value.Codecs.TryGetByFileName(file, out CaptionCodecInfo? codec)) continue;

            byte[]? bytes = await TryReadAsync(file, cancellationToken);
            CaptionImportResult? import = bytes is null ? null : s_catalog.Value.Serializer.ImportFile(bytes, codec.Format);
            if (import?.Document is not { Count: > 0 } document)
            {
                notifications?.ShowError(
                    Strings.AddElement,
                    string.Format(CultureInfo.CurrentCulture, Strings.SubtitleFile_CouldNotRead, Path.GetFileName(file)));
                continue;
            }

            List<ElementDescription> clips = CaptionSceneImporter.CreateDescriptions(
                scene,
                layer,
                document,
                template,
                cancellationToken,
                elementName: Path.GetFileNameWithoutExtension(file),
                minimumLength: s_shortestCue);
            descriptions.AddRange(clips);
            layer = clips.SelectMany(CaptionSceneImporter.GetExtents).Max(extent => extent.Layer) + 1;
            // Everything but markup means a cue was left out.
            if (import.Diagnostics.Any(item => item.Kind != CaptionDiagnosticKinds.UnsupportedMarkup))
                skippedSome.Add(file);
        }

        return skippedSome;
    }

    // A file can be locked, or gone by now.
    private static async Task<byte[]?> TryReadAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(file, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

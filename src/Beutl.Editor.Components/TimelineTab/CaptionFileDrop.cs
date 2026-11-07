using System.Globalization;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Components.TimelineTab;

// Turns a dropped subtitle file into one text clip per cue, at the times the file gives, on a new layer above the scene.
internal static class CaptionFileDrop
{
    private static readonly Lazy<CaptionCatalog> s_catalog = new(() => CaptionCatalog.ComposeWithDefaultElementFactory(
        Strings.AiSubtitle_DefaultTemplate, [], CaptionPresentationDefaults.ElementFactory));

    // A file's cues keep the length it gives them, however short.
    private static readonly MediaTime s_shortestCue = MediaTime.FromSeconds(0.001);

    public static bool IsCaptionFile(string file) => s_catalog.Value.Codecs.TryGetByFileName(file, out _);

    // The default look and the text templates saved so far, which change while Beutl runs.
    public static IReadOnlyList<CaptionTemplate> GetTemplates()
    {
        CaptionCatalog catalog = s_catalog.Value;
        catalog.RefreshObjectTemplates(CaptionCatalog.FindObjectTemplates());
        return [.. catalog.Templates.Templates.Select(item => catalog.Templates.GetTemplate(item.Id))];
    }

    public static Task AddAsync(TimelineTabViewModel viewModel, string file, CaptionTemplate? template = null)
        => AddAsync(viewModel, [file], [], template);

    // Adds the files' clips with whatever else was dropped, so that one undo takes back the whole drop. Each file goes
    // on layers of its own above the rest.
    public static async Task AddAsync(
        TimelineTabViewModel viewModel,
        IReadOnlyList<string> files,
        IReadOnlyList<ElementDescription> others,
        CaptionTemplate? template = null)
    {
        int layer = 0;
        var descriptions = new List<ElementDescription>(others);
        var skippedSome = new List<string>();
        foreach (string file in files)
        {
            if (!s_catalog.Value.Codecs.TryGetByFileName(file, out CaptionCodecInfo? codec)) continue;

            byte[]? bytes = await TryReadAsync(file);
            CaptionImportResult? import = bytes is null ? null : s_catalog.Value.Serializer.ImportFile(bytes, codec.Format);
            if (import?.Document is not { Count: > 0 } document)
            {
                NotificationService.ShowError(
                    Strings.AddElement,
                    string.Format(CultureInfo.CurrentCulture, Strings.SubtitleFile_CouldNotRead, Path.GetFileName(file)));
                continue;
            }

            List<ElementDescription> clips = AiCaptionSceneImporter.CreateDescriptions(
                viewModel.Scene,
                layer,
                document,
                template ?? CaptionPresentationDefaults.CreateDefaultText(Strings.AiSubtitle_DefaultTemplate),
                elementName: Path.GetFileNameWithoutExtension(file),
                minimumLength: s_shortestCue);
            descriptions.AddRange(clips);
            layer = clips.SelectMany(AiCaptionSceneImporter.GetExtents).Max(extent => extent.Layer) + 1;
            // Everything but markup means a cue was left out.
            if (import.Diagnostics.Any(item => item.Kind != CaptionDiagnosticKinds.UnsupportedMarkup))
                skippedSome.Add(file);
        }

        if (descriptions.Count == 0) return;

        ElementAddResult result = await viewModel.AddElementsWithResultAsync(descriptions);
        if (!result.IsSuccess) return;

        // The file decides where its clips go, which can be far from where the timeline is looking.
        if (result.Items.Count > others.Count)
        {
            Element first = result.Items[others.Count].PrimaryElement;
            viewModel.ScrollTo.Execute((first.Range, first.ZIndex.Value));
        }

        foreach (string file in skippedSome)
        {
            NotificationService.ShowWarning(
                Strings.AddElement,
                string.Format(CultureInfo.CurrentCulture, Strings.SubtitleFile_SomeSkipped, Path.GetFileName(file)));
        }
    }

    // A file can be locked, or gone by now.
    private static async Task<byte[]?> TryReadAsync(string file)
    {
        try
        {
            return await File.ReadAllBytesAsync(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

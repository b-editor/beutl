using System.Globalization;
using System.Text;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

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
        catalog.RefreshObjectTemplates(ObjectTemplateService.Instance.FindByBaseType(typeof(Beutl.Graphics.Drawable)));
        return [.. catalog.Templates.Templates.Select(item => catalog.Templates.GetTemplate(item.Id))];
    }

    public static async Task AddAsync(TimelineTabViewModel viewModel, string file, CaptionTemplate? template = null)
    {
        if (!s_catalog.Value.Codecs.TryGetByFileName(file, out CaptionCodecInfo? codec)) return;

        byte[] bytes = await File.ReadAllBytesAsync(file);
        CaptionImportResult import = Import(bytes, codec.Format);
        if (import.Document is not { Count: > 0 } document)
        {
            NotificationService.ShowError(
                Strings.AddElement,
                string.Format(CultureInfo.CurrentCulture, Strings.SubtitleFile_CouldNotRead, Path.GetFileName(file)));
            return;
        }

        CaptionSceneImportResult result = await AiCaptionSceneImporter.AddAsync(
            viewModel.Scene,
            viewModel.EditorContext.GetRequiredService<IElementAdder>(),
            document,
            template ?? CaptionPresentationDefaults.CreateDefaultText(Strings.AiSubtitle_DefaultTemplate),
            elementName: Path.GetFileNameWithoutExtension(file),
            minimumLength: s_shortestCue);
        if (result.IsSuccess)
        {
            // The file decides where the clips go, which can be far from where the timeline is looking.
            if (result.FirstElement is { } first)
                viewModel.ScrollTo.Execute((first.Range, first.ZIndex.Value));

            // Everything but markup means a cue was left out.
            if (import.Diagnostics.Any(item => item.Kind != CaptionDiagnosticKinds.UnsupportedMarkup))
            {
                NotificationService.ShowWarning(
                    Strings.AddElement,
                    string.Format(CultureInfo.CurrentCulture, Strings.SubtitleFile_SomeSkipped, Path.GetFileName(file)));
            }

            return;
        }

        if (result.FailureId == ElementAddFailureIds.LockedLayer)
        {
            NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
        }
        else
        {
            NotificationService.ShowError(Strings.AddElement, MessageStrings.UnexpectedError);
        }
    }

    // The serializer reads UTF-8, but subtitle tools also write UTF-16 and, on Windows, the system's legacy code page.
    internal static CaptionImportResult Import(byte[] bytes, CaptionFormatId format)
    {
        CaptionDocumentSerializer serializer = s_catalog.Value.Serializer;
        if (bytes is [0xFF, 0xFE, ..])
            return serializer.Import(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, bytes, 2, bytes.Length - 2), format);
        if (bytes is [0xFE, 0xFF, ..])
            return serializer.Import(Encoding.Convert(Encoding.BigEndianUnicode, Encoding.UTF8, bytes, 2, bytes.Length - 2), format);

        CaptionImportResult result = serializer.Import(bytes, format);
        if (result.IsSuccess || result.Diagnostics.All(item => item.Kind != CaptionDiagnosticKinds.InvalidUtf8))
            return result;

        Encoding legacy = CodePagesEncodingProvider.Instance.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage)
            ?? Encoding.Latin1;
        return serializer.Import(Encoding.Convert(legacy, Encoding.UTF8, bytes), format);
    }
}

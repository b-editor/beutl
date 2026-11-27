using System.ComponentModel;
using System.Globalization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record ImportSubtitlesResponse(IReadOnlyList<string> CreatedElementIds, int CueCount, bool HasSkippedCues);
public sealed record SubtitleCue(string ElementId, int Layer, double StartSeconds, double EndSeconds, string Text);
public sealed record ListSubtitlesResponse(IReadOnlyList<SubtitleCue> Cues);
public sealed record ExportSubtitlesResponse(string OutputPath, string Format, int CueCount, int SkippedClipCount);

[McpServerToolType]
public sealed class SubtitleTools(
    AgentSessionManager sessions, ObjectTemplateService templates, GlobalConfiguration configuration, DestructiveGuard destructiveGuard) : ToolBase
{
    [McpServerTool(Name = "import_subtitles")]
    [Description("Imports a subtitle file as timeline clips in one undo step. Keeps cue timing and places overlapping cues on separate unlocked layers above the scene. Returns the new element IDs and whether any cues were skipped.")]
    public ValueTask<ToolResult<ImportSubtitlesResponse>> ImportSubtitles(
        [Description("Absolute path to an SRT (.srt), ASS (.ass or .ssa), or WebVTT (.vtt) file.")]
        string path,
        [Description("Optional saved text object, element, or element-set template ID from list_templates or list_element_templates. Omit for the editor's default subtitle template.")]
        string? templateId = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            string file = ToolPaths.RequireAbsolute(path, nameof(path));
            if (!CaptionFileImport.Codecs.TryGetByFileName(file, out CaptionCodecInfo? codec) || !codec.CanDecode)
                throw Invalid(Strings.SubtitleFile_UnsupportedFormat, nameof(path));

            IEditingSession session = sessions.RequireSession();
            Scene scene = session.ReadOnSession(() => RequireSceneRoot(session));
            CaptionImportResult? import = await CaptionFileImport.ReadAsync(file, codec.Format, cancellationToken).ConfigureAwait(false);
            if (import?.Document is not { Count: > 0 } document)
                throw Invalid(string.Format(CultureInfo.CurrentCulture, Strings.SubtitleFile_CouldNotRead, Path.GetFileName(file)), nameof(path));

            CaptionTemplate template = ReadTemplate(templateId);
            IReadOnlyList<string> createdIds;
            if (session is LiveEditingSession live)
            {
                List<ElementDescription> descriptions = live.ReadOnSession(Describe);
                ElementAddResult result = await live.Elements.AddElementsAsync(
                    scene, descriptions, CaptureLockedTransitionValidation, cancellationToken).ConfigureAwait(false);
                if (result.Failure is { } failure)
                {
                    if (failure.Exception is ReconcileException rejected) throw rejected;
                    throw Invalid(failure.Message, nameof(path));
                }

                createdIds = ElementIds(result.Elements);
            }
            else
            {
                createdIds = session.EditOnSession(() =>
                {
                    List<ElementDescription> descriptions = Describe();
                    List<Element> added = [];
                    session.History.ExecuteInTransaction(() =>
                    {
                        Action? validate = CaptureLockedTransitionValidation(scene);
                        added = CaptionSceneImporter.AddToFileScene(
                            scene, descriptions, configuration.EditorConfig.AutoAdjustSceneDuration.Value, cancellationToken);
                        validate?.Invoke();
                    }, CommandNames.AddElement, cancellationToken: cancellationToken);
                    session.MarkDirtyIfFileSession();
                    return ElementIds(added);
                }, cancellationToken);
            }

            return new ImportSubtitlesResponse(createdIds, document.Count, CaptionFileImport.HasSkippedCues(import));

            List<ElementDescription> Describe()
            {
                // Reading the file can overlap a change to the active file-session scene.
                if (!ReferenceEquals(scene, session.Root)) throw new SessionUnavailableException();
                return CaptionFileImport.CreateDescriptions(scene, file, document, template, layer: 0, cancellationToken);
            }
        });

    [McpServerTool(Name = "list_subtitles", ReadOnly = true)]
    [Description("Lists subtitle cues from scene text clips, with element IDs, layers, start and end in timeline seconds, and extracted text. Inspect a clip with read_document and use apply_edit or timeline tools to edit it.")]
    public ToolResult<ListSubtitlesResponse> ListSubtitles(
        [Description("Optional timeline layer indices (ZIndex). Omit to inspect all layers.")]
        int[]? layers = null)
        => Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            HashSet<int>? selectedLayers = layers?.ToHashSet();
            return session.ReadOnSession(() => new ListSubtitlesResponse(
                RequireSceneRoot(session).Children
                    .Where(element => selectedLayers is null || selectedLayers.Contains(element.ZIndex.Value))
                    .Select(element =>
                    {
                        TimeRange range = element.Range;
                        return new SubtitleCue(element.Id.Value.ToString(), element.ZIndex.Value,
                            range.Start.ToSeconds(), range.End.ToSeconds(), SceneCaptionExporter.ExtractText(element, range.Start));
                    })
                    .Where(cue => cue.Text.Length > 0)
                    .OrderBy(cue => cue.StartSeconds)
                    .ThenBy(cue => cue.EndSeconds)
                    .ToArray()));
        });

    [McpServerTool(Name = "export_subtitles")]
    [Description("Exports chosen clips or layers to a subtitle file using the editor's text extraction. Reports the number of cues written and the number of clips skipped because they had no readable text.")]
    public ValueTask<ToolResult<ExportSubtitlesResponse>> ExportSubtitles(
        [Description("Absolute output path ending in .srt, .ass, or .vtt. The extension selects the format.")]
        string outputPath,
        [Description("Element IDs from list_subtitles or read_document_summary. Supply this or layers.")]
        string[]? elementIds = null,
        [Description("Timeline layer indices (ZIndex) to export. Supply this or elementIds.")]
        int[]? layers = null,
        [Description("Allow replacing an existing subtitle file.")]
        bool confirmOverwrite = false,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            if ((elementIds is null) == (layers is null) || elementIds is { Length: 0 } || layers is { Length: 0 })
                throw Invalid(Strings.ExportSubtitles_SelectSource, nameof(elementIds));

            string file = ToolPaths.ResolveForWrite(outputPath, nameof(outputPath));
            if (!CaptionFileImport.Codecs.TryGetByFileName(file, out CaptionCodecInfo? codec)
                || !codec.CanEncode || !file.EndsWith(codec.FileExtensions[0], StringComparison.OrdinalIgnoreCase))
                throw Invalid(Strings.SubtitleFile_UnsupportedFormat, nameof(outputPath));
            destructiveGuard.EnsureOverwriteAllowed(file, confirmOverwrite);

            IEditingSession session = sessions.RequireSession();
            var (document, skipped) = session.ReadOnSession(() =>
            {
                Scene scene = RequireSceneRoot(session);
                IEnumerable<Element> elements;
                if (elementIds is not null)
                {
                    elements = TimelineEditTools.ReadElements(scene, elementIds, includeLinked: false);
                }
                else
                {
                    HashSet<int> selectedLayers = layers!.ToHashSet();
                    elements = scene.Children.Where(element => selectedLayers.Contains(element.ZIndex.Value));
                }

                return SceneCaptionExporter.BuildDocument(elements, codec.Format);
            });
            if (document.Count == 0)
            {
                string message = Strings.ExportSubtitles_NoText;
                if (skipped > 0)
                    message += Environment.NewLine + string.Format(CultureInfo.CurrentCulture, Strings.ExportSubtitles_SkippedClips, skipped);
                throw Invalid(message, nameof(elementIds));
            }

            try
            {
                byte[] bytes = CaptionFileImport.Serializer.Export(document, codec.Format);
                await CaptionFileWriter.WriteAsync(file, bytes, confirmOverwrite, cancellationToken).ConfigureAwait(false);
            }
            catch (CaptionExportException ex)
            {
                throw Invalid(ex.Message, nameof(outputPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A different writer could have created the file after the first check.
                destructiveGuard.EnsureOverwriteAllowed(file, confirmOverwrite);
                throw Invalid(MessageStrings.UnableToSaveFile, nameof(outputPath));
            }

            return new ExportSubtitlesResponse(file, codec.Format.Value, document.Count, skipped);
        });

    private CaptionTemplate ReadTemplate(string? templateId)
    {
        if (templateId is null)
            return CaptionPresentationDefaults.CreateDefaultText(Strings.AiSubtitle_DefaultTemplate);
        if (!Guid.TryParse(templateId, out Guid id) || id == Guid.Empty)
            throw Invalid(Strings.TemplateInvalidId, nameof(templateId));
        ObjectTemplateItem template = templates.FindById(id)
            ?? throw new ReconcileException(new ToolError(ErrorCode.StaleHandle,
                string.Format(CultureInfo.CurrentCulture, Strings.TemplateNotFound, templateId), nameof(templateId)));
        return CaptionCatalog.TryCreateTemplate(template)
            ?? throw Invalid(Strings.SubtitleFile_UnsupportedTemplate, nameof(templateId));
    }

    private static string[] ElementIds(IEnumerable<Element> elements)
        => elements.Select(element => element.Id.Value.ToString()).ToArray();

    private static ReconcileException Invalid(string message, string target)
        => new(new ToolError(ErrorCode.ValidationRejected, message, target));
}

using System.Globalization;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Services;

internal sealed record SceneFileImportResult(
    ElementAddResult AddResult, int FirstCaptionIndex, IReadOnlyList<string> FilesWithSkippedCues)
{
    public Element? FirstCaption => AddResult.Items.Count > FirstCaptionIndex
        ? AddResult.Items[FirstCaptionIndex].PrimaryElement
        : null;
}

internal sealed class SceneFileImporter(
    IElementAdder adder,
    DecoderRegistry? decoders = null,
    ObjectTemplateService? templates = null,
    INotificationService? notifications = null,
    Func<bool>? hasFileSourceOverride = null)
{
    private static readonly MediaTime s_defaultLength = MediaTime.FromSeconds(5);

    public async Task<SceneFileImportResult?> AddAsync(
        Scene scene,
        IReadOnlyList<string> files,
        MediaTime start,
        int layer,
        Point? position = null,
        Func<IReadOnlyList<CaptionTemplate>, Task<CaptionTemplate?>>? chooseCaptionTemplate = null,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0) return null;

        // A drag starts with whichever file was grabbed, so base the layout on the names.
        var byName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.NumericOrdering);
        string[] ordered = [.. files.Distinct(StringComparer.Ordinal)
            .OrderBy(Path.GetFileName, byName)
            .ThenBy(static file => file, StringComparer.Ordinal)];
        string[] captions = [.. ordered.Where(CaptionFileImport.IsCaptionFile)];
        CaptionTemplate? captionTemplate = null;
        if (captions.Length > 0)
        {
            if (chooseCaptionTemplate is not null && templates is not null)
            {
                IReadOnlyList<CaptionTemplate> choices = CaptionFileImport.GetTemplates(templates);
                captionTemplate = choices.Count == 1 ? choices[0] : await chooseCaptionTemplate(choices);
                if (captionTemplate is null) return null;
            }
            else
            {
                captionTemplate = CaptionPresentationDefaults.CreateDefaultText(Strings.AiSubtitle_DefaultTemplate);
            }
        }

        // Template files and media containers may have to be opened to lay out the batch.
        string[] clipFiles = [.. ordered.Except(captions)];
        List<ElementDescription> descriptions = await Task.Run(
            () => CreateClipDescriptions(clipFiles, start, layer, position, cancellationToken),
            cancellationToken);
        if (descriptions.Count == 0 && captions.Length == 0)
        {
            // Let the element adder explain the refusal when no file is recognized.
            descriptions.Add(new ElementDescription(start, s_defaultLength, layer, new ElementSource.File(ordered[0]), Position: position));
        }

        int firstCaptionIndex = descriptions.Count;
        IReadOnlyList<string> skippedCues = captionTemplate is null ? []
            : await CaptionFileImport.AppendDescriptionsAsync(
                scene, captions, descriptions, captionTemplate, notifications, cancellationToken);
        if (descriptions.Count == 0) return null;

        ElementAddResult result = await adder.AddAsync(descriptions, cancellationToken);
        return new SceneFileImportResult(result, firstCaptionIndex, skippedCues);
    }

    public void ReportSkippedCues(IReadOnlyList<string> files)
    {
        foreach (string file in files)
        {
            notifications?.ShowWarning(
                Strings.AddElement,
                string.Format(CultureInfo.CurrentCulture, Strings.SubtitleFile_SomeSkipped, Path.GetFileName(file)));
        }
    }

    private List<ElementDescription> CreateClipDescriptions(
        IReadOnlyList<string> files, MediaTime start, int layer, Point? position, CancellationToken cancellationToken)
    {
        var clips = new List<(string File, ObjectTemplateItem? Template)>();
        var sounds = new List<string>();
        var video = decoders is null ? [] : MediaFileMatcher.VideoExtensions(decoders);
        var audio = decoders is null ? [] : MediaFileMatcher.AudioExtensions(decoders);
        bool includeUnmatchedFiles = hasFileSourceOverride?.Invoke() == true;
        int clipLayerCount = 0;
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetExtension(file), ".json", StringComparison.OrdinalIgnoreCase)
                && templates?.TryLoadFromFile(file) is { } template
                && ElementTemplateResolver.CanCreateDescription(template))
            {
                clips.Add((file, template));
            }
            else if (decoders is null || MediaFileMatcher.IsImage(file))
            {
                clips.Add((file, null));
            }
            else if (MediaFileMatcher.Matches(file, video)
                     && (files.Count == 1 || !MediaFileMatcher.Matches(file, audio) || HoldsVideo(file, decoders)))
            {
                clips.Add((file, null));
                // A video's own sound uses the next layer.
                clipLayerCount = Math.Max(clipLayerCount, 2);
            }
            else if (MediaFileMatcher.Matches(file, audio))
            {
                sounds.Add(file);
            }
            else if (includeUnmatchedFiles)
            {
                // A package can replace the file handler and accept formats the built-in decoders do not know.
                clips.Add((file, null));
            }
        }

        if (clips.Count > 0)
            clipLayerCount = Math.Max(clipLayerCount, 1);

        var descriptions = new List<ElementDescription>(clips.Count + sounds.Count);
        foreach ((string file, ObjectTemplateItem? template) in clips)
        {
            ElementDescription description;
            if (template is null)
            {
                description = new ElementDescription(start, s_defaultLength, layer, new ElementSource.File(file), Position: position);
            }
            else
            {
                description = ElementTemplateResolver.CreateDescription(template, start, layer);
                if (sounds.Count > 0 && description.Source is ElementSource.ElementSetTemplate source)
                {
                    var set = new Lazy<ElementSet>(source.Factory);
                    // Leave room for all the template layers. Preflight reuses the instance and reports any load failure.
                    try
                    {
                        if (set.Value.Elements.Count > 0)
                        {
                            (_, int first, int last) = DuplicateHelper.ComputePlacementRange(set.Value.Elements);
                            clipLayerCount = Math.Max(clipLayerCount, checked(last - first + 1));
                        }
                    }
                    catch (Exception)
                    {
                        // The adder reports the original failure when it opens the set.
                    }
                    description = new ElementDescription(start, null, layer, new ElementSource.ElementSetTemplate(() => set.Value));
                }
            }
            descriptions.Add(description with { FollowsPrevious = true });
        }

        foreach (string sound in sounds)
        {
            descriptions.Add(new ElementDescription(start, s_defaultLength, checked(layer + clipLayerCount), new ElementSource.File(sound))
            {
                FollowsPrevious = true,
            });
        }
        return descriptions;
    }

    // Some containers, such as .webm, are registered for both video and audio.
    private static bool HoldsVideo(string file, DecoderRegistry decoders)
    {
        try
        {
            using var reader = MediaReader.Open(file, new MediaOptions(MediaMode.AudioVideo), decoders);
            return reader.HasVideo;
        }
        catch (Exception)
        {
            // The element adder reports a file that cannot be opened.
            return true;
        }
    }
}

using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public sealed class ElementClipboardService : IElementClipboardService
{
    private static readonly ILogger s_logger = Log.CreateLogger<ElementClipboardService>();

    private readonly HistoryManager _historyManager;
    private readonly IClipboardGateway _clipboard;
    private readonly IElementDuplicateService _duplicateService;
    private readonly IElementAdder? _elementAdder;
    private readonly Func<Color> _imageAccentColorFactory;

    /// <param name="imageAccentColorFactory">Accent color for an element created
    /// from a bitmap paste. Production passes <c>ElementAccentColorGenerator.GenerateColor</c>;
    /// tests can pass a fixed color.</param>
    public ElementClipboardService(
        HistoryManager historyManager,
        IClipboardGateway clipboard,
        IElementDuplicateService duplicateService,
        Func<Color> imageAccentColorFactory,
        IElementAdder? elementAdder = null)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _duplicateService = duplicateService ?? throw new ArgumentNullException(nameof(duplicateService));
        _imageAccentColorFactory = imageAccentColorFactory ?? throw new ArgumentNullException(nameof(imageAccentColorFactory));
        _elementAdder = elementAdder;
    }

    public async Task<bool> CopyAsync(IReadOnlyList<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count == 0) return false;

        var entries = new List<ClipboardEntry>(3);
        string singleJson = CoreSerializer.SerializeToJsonString(elements[0]);
        entries.Add(new(BeutlClipboardFormats.Element, singleJson));
        entries.Add(new(BeutlClipboardFormats.Text, singleJson));

        if (elements.Count > 1)
        {
            JsonNode multiNode = new JsonArray(
                elements.Select(JsonNode (e) => CoreSerializer.SerializeToJsonObject(e)).ToArray());
            entries.Add(new ClipboardEntry(BeutlClipboardFormats.Elements, multiNode.ToJsonString()));
        }

        return await _clipboard.SetAsync(entries);
    }

    public async Task<bool> CutAsync(Scene scene, IReadOnlyList<Element> elements, bool ripple = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        Element[] editable = elements.Where(e => !scene.IsElementLocked(e)).ToArray();
        if (editable.Length == 0) return false;

        // Abort the delete half if the clipboard write failed, else the user
        // loses the elements with no way to paste them back.
        if (!await CopyAsync(editable))
        {
            s_logger.LogWarning(
                "CutAsync aborted: clipboard unavailable, preserving {Count} element(s).",
                editable.Length);
            return false;
        }

        // A lock applied to a clip or its layer during the awaited clipboard write must still be
        // honored: re-filter before removal so a now-locked clip is not deleted (it stays copyable).
        editable = editable.Where(e => !scene.IsElementLocked(e)).ToArray();
        if (editable.Length == 0) return true;

        RippleHelper.RemoveAndShiftAfter(scene, editable, ripple, scene.RemoveChild);

        scene.RemoveElementsFromGroups(editable.Select(e => e.Id.Value).ToArray());

        _historyManager.Commit(CommandNames.CutElement);
        return true;
    }

    public async Task<ElementPasteOutcome> PasteAsync(Scene scene, MediaTime clickedFrame, int clickedLayer)
    {
        ArgumentNullException.ThrowIfNull(scene);

        IReadOnlyList<string> formats = await _clipboard.GetFormatsAsync();

        if (formats.Contains(BeutlClipboardFormats.Elements))
        {
            return await PasteElementsAsync(scene, clickedFrame, clickedLayer);
        }

        if (formats.Contains(BeutlClipboardFormats.Element))
        {
            return await PasteSingleElementAsync(scene, clickedFrame, clickedLayer);
        }

        if (formats.Contains(BeutlClipboardFormats.Files))
        {
            return await PasteFilesAsync(scene, clickedFrame, clickedLayer);
        }

        if (formats.Contains(BeutlClipboardFormats.Bitmap))
        {
            return await PasteBitmapAsync(scene, clickedFrame, clickedLayer);
        }

        return ElementPasteOutcome.Empty;
    }

    private async Task<ElementPasteOutcome> PasteElementsAsync(Scene scene, MediaTime clickedFrame, int clickedLayer)
    {
        if (scene.IsLayerLocked(clickedLayer))
        {
            s_logger.LogWarning("PasteElementsAsync skipped: layer {Layer} is locked.", clickedLayer);
            return ElementPasteOutcome.Empty;
        }

        string? json = await _clipboard.TryGetStringAsync(BeutlClipboardFormats.Elements);
        if (json is null) return ElementPasteOutcome.Empty;
        if (ClipboardJson.TryParse(json) is not JsonArray array || array.Count == 0)
        {
            return ElementPasteOutcome.Empty;
        }

        // Re-check after the awaited clipboard read: the layer may have been locked during the await.
        if (scene.IsLayerLocked(clickedLayer))
        {
            s_logger.LogWarning("PasteElementsAsync skipped: layer {Layer} was locked during paste.", clickedLayer);
            return ElementPasteOutcome.Empty;
        }

        var oldElements = new Element[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            var element = new Element();
            CoreSerializer.PopulateFromJsonObject(element, array[i]!.AsObject());
            oldElements[i] = element;
        }

        DuplicateOutcome outcome = _duplicateService.DuplicateAtClickedPosition(
            scene, oldElements, clickedFrame, clickedLayer);

        if (!outcome.Success)
        {
            // Log like the single-element / bitmap paths so a no-op
            // multi-element paste is diagnosable (unsaved scene, staging I/O).
            s_logger.LogWarning(
                "PasteElementsAsync skipped: DuplicateAtClickedPosition failed for {Count} element(s) at ({Frame}, layer {Layer}).",
                oldElements.Length, clickedFrame, clickedLayer);
            return ElementPasteOutcome.Empty;
        }

        return new ElementPasteOutcome(
            Pasted: true,
            ScrollTo: outcome.ScrollToRange,
            ScrollToZIndex: outcome.ScrollToZIndex,
            AddFailure: null);
    }

    private async Task<ElementPasteOutcome> PasteSingleElementAsync(Scene scene, MediaTime clickedFrame, int clickedLayer)
    {
        if (scene.Uri is null)
        {
            s_logger.LogWarning("PasteSingleElementAsync skipped: scene has no Uri.");
            return ElementPasteOutcome.Empty;
        }

        if (scene.IsLayerLocked(clickedLayer))
        {
            s_logger.LogWarning("PasteSingleElementAsync skipped: layer {Layer} is locked.", clickedLayer);
            return ElementPasteOutcome.Empty;
        }

        string? json = await _clipboard.TryGetStringAsync(BeutlClipboardFormats.Element);
        if (json is null) return ElementPasteOutcome.Empty;
        if (ClipboardJson.TryParse(json) is not JsonObject obj) return ElementPasteOutcome.Empty;

        // Re-check after the awaited clipboard read: the layer may have been locked during the await.
        if (scene.IsLayerLocked(clickedLayer))
        {
            s_logger.LogWarning("PasteSingleElementAsync skipped: layer {Layer} was locked during paste.", clickedLayer);
            return ElementPasteOutcome.Empty;
        }

        var oldElement = new Element();
        CoreSerializer.PopulateFromJsonObject(oldElement, obj);

        ObjectRegenerator.Regenerate(oldElement, out Element newElement);
        newElement.Start.Value = clickedFrame;
        newElement.ZIndex.Value = clickedLayer;

        return AddPastedElement(scene, newElement);
    }

    private async Task<ElementPasteOutcome> PasteFilesAsync(Scene scene, MediaTime clickedFrame, int clickedLayer)
    {
        if (_elementAdder is null) return ElementPasteOutcome.Empty;

        IReadOnlyList<string>? files = await _clipboard.TryGetFilePathsAsync();
        if (files is null || files.Count == 0) return ElementPasteOutcome.Empty;

        ElementAddResult result = await _elementAdder.AddAsync(files
            .Select(file => new ElementDescription(
                clickedFrame,
                MediaTime.FromSeconds(5),
                clickedLayer,
                new ElementSource.File(file)))
            .ToArray(), CancellationToken.None);
        if (!result.IsSuccess)
        {
            return new ElementPasteOutcome(
                Pasted: false,
                ScrollTo: default,
                ScrollToZIndex: 0,
                AddFailure: result.Failure);
        }

        Element scrollTarget = result.Items[^1].PrimaryElement;
        return new ElementPasteOutcome(
            Pasted: true,
            ScrollTo: scrollTarget.Range,
            ScrollToZIndex: scrollTarget.ZIndex.Value,
            AddFailure: null);
    }

    private async Task<ElementPasteOutcome> PasteBitmapAsync(Scene scene, MediaTime clickedFrame, int clickedLayer)
    {
        if (scene.Uri is null)
        {
            s_logger.LogWarning("PasteBitmapAsync skipped: scene has no Uri.");
            return ElementPasteOutcome.Empty;
        }

        if (scene.IsLayerLocked(clickedLayer))
        {
            s_logger.LogWarning("PasteBitmapAsync skipped: layer {Layer} is locked.", clickedLayer);
            return ElementPasteOutcome.Empty;
        }

        ReadOnlyMemory<byte>? png = await _clipboard.TryGetBitmapPngAsync();
        if (png is null) return ElementPasteOutcome.Empty;

        // Re-check after the awaited clipboard read: the layer may have been locked during the await.
        if (scene.IsLayerLocked(clickedLayer))
        {
            s_logger.LogWarning("PasteBitmapAsync skipped: layer {Layer} was locked during paste.", clickedLayer);
            return ElementPasteOutcome.Empty;
        }

        string dir = Path.GetDirectoryName(scene.Uri.LocalPath)!;
        string resDir = Path.Combine(dir, "resources");
        Directory.CreateDirectory(resDir);

        string imageFile = RandomFileNameGenerator.Generate(resDir, "png");
        await File.WriteAllBytesAsync(imageFile, png.Value.ToArray());

        // The file write is another await; re-check the lock before the scene mutation and delete the
        // just-written resource so a row locked during the write leaves no element and no orphan file.
        if (scene.IsLayerLocked(clickedLayer))
        {
            s_logger.LogWarning("PasteBitmapAsync skipped: layer {Layer} was locked during paste.", clickedLayer);
            DeleteOrphanResource(imageFile);
            return ElementPasteOutcome.Empty;
        }

        var sourceImage = new SourceImage();
        sourceImage.Source.Value = ImageSource.Open(imageFile);
        var newElement = new Element
        {
            Start = { Value = clickedFrame },
            Length = { Value = MediaTime.FromSeconds(5) },
            ZIndex = { Value = clickedLayer },
            AccentColor = { Value = _imageAccentColorFactory() },
            Name = { Value = Path.GetFileName(imageFile) },
        };
        newElement.AddObject(sourceImage);

        ElementPasteOutcome outcome = AddPastedElement(scene, newElement);
        if (!outcome.Pasted)
        {
            // The placement search did not find a usable slot, so the written image would be orphaned.
            DeleteOrphanResource(imageFile);
        }

        return outcome;
    }

    private void DeleteOrphanResource(string imageFile)
    {
        try
        {
            File.Delete(imageFile);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup: a failed delete must not turn the graceful refusal into a throw.
            s_logger.LogWarning(ex, "PasteBitmapAsync: failed to delete orphaned resource {File}.", imageFile);
        }
    }

    // The element's file is written when the scene is next saved.
    private ElementPasteOutcome AddPastedElement(Scene scene, Element newElement)
    {
        // AddChild's default Auto handling would slide the clip into the next gap and shorten it.
        // Paste instead searches for a free slot like duplicate, keeping the clip length and using
        // the clicked start as the anchor.
        var rowOffsets = new HashSet<int> { 0 };
        HashSet<int> lockedRows = scene.Layers
            .Where(l => l.IsLocked.Value)
            .Select(l => l.ZIndex.Value)
            .ToHashSet();
        var anchor = ElementPlacementHelper.FindFreeAnchor(
            scene,
            newElement.Range,
            rowOffsets,
            lockedRows,
            newElement.Start.Value,
            newElement.ZIndex.Value);
        if (anchor is null)
        {
            s_logger.LogWarning("AddPastedElement skipped: free-slot search was exhausted.");
            return ElementPasteOutcome.Empty;
        }

        (MediaTime start, int zIndex) = anchor.Value;

        // The search only returns unlocked destinations; keep the guard at the
        // insertion boundary so placement never depends on that invariant alone.
        if (ElementPlacementHelper.AnyDestinationRowLocked(zIndex, rowOffsets, lockedRows))
        {
            s_logger.LogWarning(
                "AddPastedElement skipped: free-slot search returned a locked layer {Layer}.", zIndex);
            return ElementPasteOutcome.Empty;
        }

        newElement.Start.Value = start;
        newElement.ZIndex.Value = zIndex;
        newElement.Uri = ElementFileNaming.GetUri(scene.Uri!, newElement.Id.Value);
        scene.AddChild(newElement, ElementOverlapHandling.Allow);
        _historyManager.Commit(CommandNames.PasteElement);

        return new ElementPasteOutcome(
            Pasted: true,
            ScrollTo: newElement.Range,
            ScrollToZIndex: newElement.ZIndex.Value,
            AddFailure: null);
    }
}

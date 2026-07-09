using System.Text.Json.Nodes;
using Beutl.Editor.Models;
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
    /// from a bitmap paste. Production passes <c>ColorGenerator.GenerateColor</c>;
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

        string singleJson = CoreSerializer.SerializeToJsonString(elements[0]);
        var entries = new List<ClipboardEntry>(3)
        {
            new(BeutlClipboardFormats.Element, singleJson, null),
            new(BeutlClipboardFormats.Text, singleJson, null),
        };

        if (elements.Count > 1)
        {
            JsonNode multiNode = new JsonArray(
                elements.Select(JsonNode (e) => CoreSerializer.SerializeToJsonObject(e)).ToArray());
            entries.Add(new ClipboardEntry(BeutlClipboardFormats.Elements, multiNode.ToJsonString(), null));
        }

        return await _clipboard.SetAsync(entries);
    }

    public async Task<bool> CutAsync(Scene scene, IReadOnlyList<Element> elements, bool ripple = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count == 0) return false;

        // Abort the delete half if the clipboard write failed, else the user
        // loses the elements with no way to paste them back.
        if (!await CopyAsync(elements))
        {
            s_logger.LogWarning(
                "CutAsync aborted: clipboard unavailable, preserving {Count} element(s).",
                elements.Count);
            return false;
        }

        RippleHelper.RemoveAndShiftAfter(scene, elements, ripple, scene.RemoveChild);
        _historyManager.Commit(CommandNames.CutElement);
        return true;
    }

    public async Task<ElementPasteOutcome> PasteAsync(Scene scene, TimeSpan clickedFrame, int clickedLayer)
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

    private async Task<ElementPasteOutcome> PasteElementsAsync(Scene scene, TimeSpan clickedFrame, int clickedLayer)
    {
        string? json = await _clipboard.TryGetStringAsync(BeutlClipboardFormats.Elements);
        if (json is null) return ElementPasteOutcome.Empty;
        if (ClipboardJson.TryParse(json) is not JsonArray array || array.Count == 0)
        {
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
            NewElements: [],
            ScrollTo: outcome.ScrollToRange,
            ScrollToZIndex: outcome.ScrollToZIndex);
    }

    private async Task<ElementPasteOutcome> PasteSingleElementAsync(Scene scene, TimeSpan clickedFrame, int clickedLayer)
    {
        if (scene.Uri is null)
        {
            s_logger.LogWarning("PasteSingleElementAsync skipped: scene has no Uri.");
            return ElementPasteOutcome.Empty;
        }

        string? json = await _clipboard.TryGetStringAsync(BeutlClipboardFormats.Element);
        if (json is null) return ElementPasteOutcome.Empty;
        if (ClipboardJson.TryParse(json) is not JsonObject obj) return ElementPasteOutcome.Empty;

        var oldElement = new Element();
        CoreSerializer.PopulateFromJsonObject(oldElement, obj);

        ObjectRegenerator.Regenerate(oldElement, out Element newElement);
        newElement.Start = clickedFrame;
        newElement.ZIndex = clickedLayer;

        CoreSerializer.StoreToUri(newElement, RandomFileNameGenerator.GenerateUri(scene.Uri, EditorConstants.ElementFileExtension));

        scene.AddChild(newElement);
        _historyManager.Commit(CommandNames.PasteElement);

        return new ElementPasteOutcome(
            Pasted: true,
            NewElements: [newElement],
            ScrollTo: newElement.Range,
            ScrollToZIndex: newElement.ZIndex);
    }

    private async Task<ElementPasteOutcome> PasteFilesAsync(Scene scene, TimeSpan clickedFrame, int clickedLayer)
    {
        if (_elementAdder is null) return ElementPasteOutcome.Empty;

        IReadOnlyList<string>? files = await _clipboard.TryGetFilePathsAsync();
        if (files is null || files.Count == 0) return ElementPasteOutcome.Empty;

        foreach (string file in files)
        {
            _elementAdder.AddElement(new ElementDescription(
                clickedFrame, TimeSpan.FromSeconds(5), clickedLayer, FileName: file));
        }

        return new ElementPasteOutcome(true, [], default, 0);
    }

    private async Task<ElementPasteOutcome> PasteBitmapAsync(Scene scene, TimeSpan clickedFrame, int clickedLayer)
    {
        if (scene.Uri is null)
        {
            s_logger.LogWarning("PasteBitmapAsync skipped: scene has no Uri.");
            return ElementPasteOutcome.Empty;
        }

        ReadOnlyMemory<byte>? png = await _clipboard.TryGetBitmapPngAsync();
        if (png is null) return ElementPasteOutcome.Empty;

        string dir = Path.GetDirectoryName(scene.Uri.LocalPath)!;
        string resDir = Path.Combine(dir, "resources");
        Directory.CreateDirectory(resDir);

        string imageFile = RandomFileNameGenerator.Generate(resDir, "png");
        await File.WriteAllBytesAsync(imageFile, png.Value.ToArray());

        var sourceImage = new SourceImage();
        sourceImage.Source.CurrentValue = ImageSource.Open(imageFile);
        var newElement = new Element
        {
            Start = clickedFrame,
            Length = TimeSpan.FromSeconds(5),
            ZIndex = clickedLayer,
            AccentColor = _imageAccentColorFactory(),
            Name = Path.GetFileName(imageFile),
        };
        newElement.AddObject(sourceImage);

        CoreSerializer.StoreToUri(newElement, RandomFileNameGenerator.GenerateUri(dir, EditorConstants.ElementFileExtension));

        scene.AddChild(newElement);
        _historyManager.Commit(CommandNames.PasteElement);

        return new ElementPasteOutcome(
            Pasted: true,
            NewElements: [newElement],
            ScrollTo: newElement.Range,
            ScrollToZIndex: newElement.ZIndex);
    }
}

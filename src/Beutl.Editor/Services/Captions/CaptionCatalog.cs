namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Provides one reusable, dynamically updated view of the caption codecs and templates available
/// to the editor.
/// </summary>
public sealed class CaptionCatalog
{
    private readonly CaptionTemplate _defaultTemplate;
    private readonly CaptionTemplateRegistry _templates;

    private CaptionCatalog(CaptionTemplate defaultTemplate, IEnumerable<ObjectTemplateItem> objectTemplates)
    {
        _defaultTemplate = defaultTemplate;
        _templates = new CaptionTemplateRegistry([defaultTemplate, .. CreateObjectTemplates(objectTemplates)]);
        Codecs = CreateCodecs();
        Serializer = new CaptionDocumentSerializer(Codecs);
    }

    public ICaptionCodecProvider Codecs { get; }

    public ICaptionTemplateProvider Templates => _templates;

    public CaptionDocumentSerializer Serializer { get; }

    /// <summary>
    /// Creates a dynamically composed catalog using a host-supplied factory for its default template.
    /// </summary>
    internal static CaptionCatalog ComposeWithDefaultElementFactory(
        string defaultTemplateName,
        IEnumerable<ObjectTemplateItem> objectTemplates,
        ICaptionElementFactory defaultElementFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTemplateName);
        ArgumentNullException.ThrowIfNull(objectTemplates);
        ArgumentNullException.ThrowIfNull(defaultElementFactory);

        return new CaptionCatalog(
            CaptionTemplateDefaults.CreateDefaultText(defaultTemplateName, defaultElementFactory),
            objectTemplates);
    }

    /// <summary>
    /// Refreshes host-owned object templates.
    /// </summary>
    public void RefreshObjectTemplates(IEnumerable<ObjectTemplateItem> objectTemplates)
    {
        ArgumentNullException.ThrowIfNull(objectTemplates);
        _templates.Replace([_defaultTemplate, .. CreateObjectTemplates(objectTemplates)]);
    }

    private static CaptionCodecRegistry CreateCodecs()
    {
        var srt = new SrtCaptionCodec();
        var webVtt = new WebVttCaptionCodec();
        var ass = new AssCaptionCodec();
        return new CaptionCodecRegistry(
            [
                new CaptionCodecDescriptor(CaptionFormats.Srt, [".srt"]),
                new CaptionCodecDescriptor(CaptionFormats.WebVtt, [".vtt"]),
                new CaptionCodecDescriptor(CaptionFormats.Ass, [".ass", ".ssa"]),
            ],
            new Dictionary<CaptionFormatId, ICaptionDecoder>
            {
                [CaptionFormats.Srt] = srt,
                [CaptionFormats.WebVtt] = webVtt,
                [CaptionFormats.Ass] = ass,
            },
            new Dictionary<CaptionFormatId, ICaptionEncoder>
            {
                [CaptionFormats.Srt] = srt,
                [CaptionFormats.WebVtt] = webVtt,
                [CaptionFormats.Ass] = ass,
            });
    }

    private static CaptionTemplate[] CreateObjectTemplates(IEnumerable<ObjectTemplateItem> objectTemplates)
    {
        return objectTemplates
            .Select(TextBlockCaptionTemplateAdapter.TryCreate)
            .OfType<CaptionTemplate>()
            .OrderBy(item => item.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Descriptor.Id.Value, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new CaptionTemplate(
                new CaptionTemplateDescriptor(
                    item.Descriptor.Id,
                    item.Descriptor.ProviderId,
                    item.Descriptor.Name,
                    index),
                item.ElementFactory,
                item.PlacementPolicy))
            .ToArray();
    }
}

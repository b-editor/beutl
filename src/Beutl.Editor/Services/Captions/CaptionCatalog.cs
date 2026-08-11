using System.Collections.Specialized;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

public enum CaptionCatalogContributionKind
{
    Codec,
    Template,
}

public sealed record CaptionCatalogExtensionFailure(
    CaptionCatalogContributionKind Kind,
    string ExtensionName,
    Exception Exception);

/// <summary>
/// Provides one reusable, dynamically updated view of the caption codecs and templates available
/// to the editor. Package-owned implementations remain only in lease-backed registry states.
/// </summary>
public sealed class CaptionCatalog : IDisposable
{
    private readonly object _gate = new();
    private readonly IExtensionProvider? _extensionProvider;
    private readonly CaptionCodecRegistration[] _hostCodecRegistrations;
    private readonly CaptionTemplateRegistration? _defaultTemplateRegistration;
    private readonly Action<CaptionCatalogExtensionFailure>? _reportFailure;
    private CaptionTemplateRegistration[] _hostTemplateRegistrations;
    private bool _disposed;

    public CaptionCatalog(
        CaptionCodecRegistry codecs,
        CaptionTemplateRegistry templates)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        ArgumentNullException.ThrowIfNull(templates);

        Codecs = codecs;
        Templates = templates;
        Serializer = new CaptionDocumentSerializer(codecs);
        _hostCodecRegistrations = [];
        _hostTemplateRegistrations = [];
    }

    private CaptionCatalog(
        IExtensionProvider extensionProvider,
        CaptionCodecRegistration[] hostCodecRegistrations,
        CaptionTemplateRegistration defaultTemplateRegistration,
        CaptionTemplateRegistration[] hostTemplateRegistrations,
        Action<CaptionCatalogExtensionFailure>? reportFailure)
        : this(new CaptionCodecRegistry(), new CaptionTemplateRegistry())
    {
        _extensionProvider = extensionProvider;
        _hostCodecRegistrations = hostCodecRegistrations;
        _defaultTemplateRegistration = defaultTemplateRegistration;
        _hostTemplateRegistrations = hostTemplateRegistrations;
        _reportFailure = reportFailure;

        extensionProvider.AllExtensions.CollectionChanged += OnExtensionsChanged;
        try
        {
            Rebuild();
        }
        catch
        {
            extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
            throw;
        }
    }

    public CaptionCodecRegistry Codecs { get; }

    public CaptionTemplateRegistry Templates { get; }

    public CaptionDocumentSerializer Serializer { get; }

    public static CaptionCatalog CreateDefault(string defaultTemplateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTemplateName);
        var templates = new CaptionTemplateRegistry(
        [
            CaptionTemplateDefaults.CreateDefaultText(defaultTemplateName),
        ]);
        return new CaptionCatalog(new CaptionCodecRegistry(CreateDefaultCodecRegistrations()), templates);
    }

    public static CaptionCatalog Compose(
        string defaultTemplateName,
        IEnumerable<ObjectTemplateItem> objectTemplates,
        IExtensionProvider extensionProvider,
        Action<CaptionCatalogExtensionFailure>? reportFailure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTemplateName);
        ArgumentNullException.ThrowIfNull(objectTemplates);
        ArgumentNullException.ThrowIfNull(extensionProvider);

        var defaultTemplateRegistration = new CaptionTemplateRegistration(
            CaptionTemplateDefaults.CreateDefaultText(defaultTemplateName));
        CaptionTemplateRegistration[] hostTemplates =
        [
            defaultTemplateRegistration,
            .. CreateObjectTemplateRegistrations(objectTemplates),
        ];
        return new CaptionCatalog(
            extensionProvider,
            CreateDefaultCodecRegistrations(),
            defaultTemplateRegistration,
            hostTemplates,
            reportFailure);
    }

    /// <summary>
    /// Refreshes host-owned object templates while preserving all current extension registrations.
    /// </summary>
    public void RefreshObjectTemplates(IEnumerable<ObjectTemplateItem> objectTemplates)
    {
        ArgumentNullException.ThrowIfNull(objectTemplates);
        CaptionTemplateRegistration[] registrations =
            CreateObjectTemplateRegistrations(objectTemplates);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_extensionProvider is null || _defaultTemplateRegistration is null)
            {
                throw new InvalidOperationException(
                    "Only a dynamically composed caption catalog can refresh object templates.");
            }

            _hostTemplateRegistrations = [_defaultTemplateRegistration, .. registrations];
            RebuildCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_extensionProvider is not null)
            {
                _extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                Codecs.Replace(_hostCodecRegistrations);
                Templates.Replace(_hostTemplateRegistrations);
            }
        }
    }

    private void OnExtensionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Rebuild();

    private void Rebuild()
    {
        lock (_gate)
        {
            if (_disposed || _extensionProvider is null)
                return;

            RebuildCore();
        }
    }

    private void RebuildCore()
    {
        IExtensionProvider extensionProvider = _extensionProvider
            ?? throw new InvalidOperationException("The caption catalog is not dynamically composed.");
        List<CaptionCodecRegistration> codecRegistrations = [.. _hostCodecRegistrations];
        foreach (CaptionCodecExtension extension
                 in extensionProvider.GetExtensions<CaptionCodecExtension>())
        {
            TryAddCodecExtension(extension, codecRegistrations);
        }

        List<CaptionTemplateRegistration> templateRegistrations = [.. _hostTemplateRegistrations];
        foreach (CaptionTemplateExtension extension
                 in extensionProvider.GetExtensions<CaptionTemplateExtension>())
        {
            TryAddTemplateExtension(extension, templateRegistrations);
        }

        // Replacing a state first prevents new calls from resolving a removed package. Each
        // replacement then waits for calls holding the retired state before this collection
        // notification returns to PackageManager and Extension.Unload() can run.
        Codecs.Replace(codecRegistrations);
        Templates.Replace(templateRegistrations);
    }

    private void TryAddCodecExtension(
        CaptionCodecExtension extension,
        List<CaptionCodecRegistration> accepted)
    {
        try
        {
            CaptionCodecRegistration[] registrations = ValidateRegistrations(
                extension.Registrations,
                "A caption codec extension returned a null registration collection.",
                "A caption codec extension returned a null registration.");
            var candidate = new CaptionCodecRegistry(accepted.Concat(registrations));
            candidate.Replace([]);
            accepted.AddRange(registrations);
        }
        catch (Exception ex)
        {
            ReportFailure(CaptionCatalogContributionKind.Codec, extension, ex);
        }
    }

    private void TryAddTemplateExtension(
        CaptionTemplateExtension extension,
        List<CaptionTemplateRegistration> accepted)
    {
        try
        {
            CaptionTemplateRegistration[] registrations = ValidateRegistrations(
                extension.Registrations,
                "A caption template extension returned a null registration collection.",
                "A caption template extension returned a null registration.");
            var candidate = new CaptionTemplateRegistry();
            candidate.Replace(accepted.Concat(registrations));
            candidate.Replace([]);
            accepted.AddRange(registrations);
        }
        catch (Exception ex)
        {
            ReportFailure(CaptionCatalogContributionKind.Template, extension, ex);
        }
    }

    private void ReportFailure(
        CaptionCatalogContributionKind kind,
        Extension extension,
        Exception exception)
    {
        if (_reportFailure is null)
            return;

        string extensionName;
        try
        {
            extensionName = extension.Name;
        }
        catch
        {
            extensionName = extension.GetType().FullName ?? "Unknown extension";
        }

        try
        {
            _reportFailure(new CaptionCatalogExtensionFailure(kind, extensionName, exception));
        }
        catch
        {
            // Diagnostics must never interrupt registry removal before Extension.Unload().
        }
    }

    private static TRegistration[] ValidateRegistrations<TRegistration>(
        IReadOnlyCollection<TRegistration>? registrations,
        string nullCollectionMessage,
        string nullRegistrationMessage)
        where TRegistration : class
    {
        if (registrations is null)
            throw new InvalidOperationException(nullCollectionMessage);

        TRegistration[] result = registrations.ToArray();
        if (result.Any(registration => registration is null))
            throw new InvalidOperationException(nullRegistrationMessage);

        return result;
    }

    private static CaptionCodecRegistration[] CreateDefaultCodecRegistrations()
    {
        var srt = new SrtCaptionCodec();
        var webVtt = new WebVttCaptionCodec();
        var ass = new AssCaptionCodec();
        return
        [
            new CaptionCodecRegistration(new CaptionCodecContribution(
                CaptionFormats.Srt,
                new CaptionCodecDescriptor(CaptionFormats.Srt, [".srt"]),
                srt,
                srt)),
            new CaptionCodecRegistration(new CaptionCodecContribution(
                CaptionFormats.WebVtt,
                new CaptionCodecDescriptor(CaptionFormats.WebVtt, [".vtt"]),
                webVtt,
                webVtt)),
            new CaptionCodecRegistration(new CaptionCodecContribution(
                CaptionFormats.Ass,
                new CaptionCodecDescriptor(CaptionFormats.Ass, [".ass", ".ssa"]),
                ass,
                ass)),
        ];
    }

    private static CaptionTemplateRegistration[] CreateObjectTemplateRegistrations(
        IEnumerable<ObjectTemplateItem> objectTemplates)
    {
        return objectTemplates
            .Select(TextBlockCaptionTemplateAdapter.TryCreate)
            .OfType<CaptionTemplateContribution>()
            .OrderBy(contribution => contribution.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(contribution => contribution.Id.Value, StringComparer.OrdinalIgnoreCase)
            .Select((contribution, index) => new CaptionTemplateRegistration(
                new CaptionTemplateContribution(
                    contribution.Id,
                    contribution.ProviderId,
                    contribution.Name,
                    contribution.ElementFactory,
                    contribution.PlacementPolicy,
                    index)))
            .ToArray();
    }
}

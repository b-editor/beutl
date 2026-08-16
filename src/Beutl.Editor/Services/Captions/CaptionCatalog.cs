using System.Collections.Specialized;
using Beutl.Api.Services;
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
public sealed class CaptionCatalog : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IExtensionProvider? _extensionProvider;
    private readonly CaptionCodecRegistration[] _hostCodecRegistrations;
    private readonly CaptionTemplateRegistration? _defaultTemplateRegistration;
    private readonly Action<CaptionCatalogExtensionFailure>? _reportFailure;
    private readonly HashSet<CaptionCodecExtension> _activeCodecExtensions =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CaptionTemplateExtension> _activeTemplateExtensions =
        new(ReferenceEqualityComparer.Instance);
    private CaptionTemplateRegistration[] _hostTemplateRegistrations;
    private Task? _disposeTask;
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

    public ValueTask DisposeAsync()
    {
        if (_extensionProvider is IExtensionRegistry registry)
        {
            ValueTask result = default;
            registry.SynchronizeMutation(() => result = StartDispose());
            return result;
        }

        return StartDispose();
    }

    private ValueTask StartDispose()
    {
        lock (_gate)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private Task DisposeCoreAsync()
    {
        CaptionCodecExtension[] codecExtensions;
        CaptionTemplateExtension[] templateExtensions;
        ValueTask codecDrain;
        ValueTask templateDrain;
        lock (_gate)
        {
            if (_disposed)
                return Task.CompletedTask;

            _disposed = true;
            if (_extensionProvider is not null)
            {
                _extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                codecExtensions = _activeCodecExtensions.ToArray();
                templateExtensions = _activeTemplateExtensions.ToArray();
                _activeCodecExtensions.Clear();
                _activeTemplateExtensions.Clear();
                CaptionRegistryDrain<Extension> retiredCodecs = Codecs.ReplaceOwned(
                    _hostCodecRegistrations,
                    new Dictionary<CaptionFormatId, IReadOnlyCollection<Extension>>());
                CaptionRegistryDrain<Extension> retiredTemplates = Templates.ReplaceOwned(
                    _hostTemplateRegistrations,
                    new Dictionary<CaptionTemplateId, Extension>());
                codecDrain = retiredCodecs.All;
                templateDrain = retiredTemplates.All;
                foreach (CaptionCodecExtension extension in codecExtensions)
                {
                    ExtensionRegistrationLifetimes.Retire(
                        extension,
                        () => new ValueTask(retiredCodecs.DrainOwnerAsync(extension)));
                }
                foreach (CaptionTemplateExtension extension in templateExtensions)
                {
                    ExtensionRegistrationLifetimes.Retire(
                        extension,
                        () => new ValueTask(retiredTemplates.DrainOwnerAsync(extension)));
                }
            }
            else
            {
                codecExtensions = [];
                templateExtensions = [];
                codecDrain = ValueTask.CompletedTask;
                templateDrain = ValueTask.CompletedTask;
            }
        }

        return Combine(codecDrain, templateDrain).AsTask();
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

    private ValueTask RebuildCore()
    {
        IExtensionProvider extensionProvider = _extensionProvider
            ?? throw new InvalidOperationException("The caption catalog is not dynamically composed.");
        CaptionCodecExtension[] previousCodecExtensions = _activeCodecExtensions.ToArray();
        CaptionTemplateExtension[] previousTemplateExtensions = _activeTemplateExtensions.ToArray();
        List<CaptionCodecRegistration> codecRegistrations = [.. _hostCodecRegistrations];
        var codecOwners = new Dictionary<CaptionFormatId, HashSet<Extension>>();
        CaptionCodecExtension[] codecExtensions =
            extensionProvider.GetExtensions<CaptionCodecExtension>();
        foreach (CaptionCodecExtension extension in codecExtensions)
        {
            TryAddCodecExtension(extension, codecRegistrations, codecOwners);
        }

        List<CaptionTemplateRegistration> templateRegistrations = [.. _hostTemplateRegistrations];
        var templateOwners = new Dictionary<CaptionTemplateId, Extension>();
        CaptionTemplateExtension[] templateExtensions =
            extensionProvider.GetExtensions<CaptionTemplateExtension>();
        foreach (CaptionTemplateExtension extension in templateExtensions)
        {
            TryAddTemplateExtension(extension, templateRegistrations, templateOwners);
        }

        _activeCodecExtensions.Clear();
        _activeCodecExtensions.UnionWith(codecExtensions);
        _activeTemplateExtensions.Clear();
        _activeTemplateExtensions.UnionWith(templateExtensions);

        // Each replacement synchronously publishes a state that excludes removed packages. The
        // returned tasks drain calls that still hold the retired package-owned state.
        CaptionRegistryDrain<Extension> retiredCodecs =
            Codecs.ReplaceOwned(
                codecRegistrations,
                codecOwners.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyCollection<Extension>)pair.Value.ToArray()));
        CaptionRegistryDrain<Extension> retiredTemplates =
            Templates.ReplaceOwned(templateRegistrations, templateOwners);
        ValueTask codecDrain = retiredCodecs.All;
        ValueTask templateDrain = retiredTemplates.All;
        // Track every replaced category state against the extensions that could be retained by
        // that state. This also covers an older state retired by a host-template refresh long
        // before its package is removed.
        foreach (CaptionCodecExtension extension in previousCodecExtensions)
        {
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => new ValueTask(retiredCodecs.DrainOwnerAsync(extension)));
        }

        foreach (CaptionTemplateExtension extension in previousTemplateExtensions)
        {
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => new ValueTask(retiredTemplates.DrainOwnerAsync(extension)));
        }

        return Combine(codecDrain, templateDrain);
    }

    private static ValueTask Combine(ValueTask first, ValueTask second)
    {
        if (first.IsCompletedSuccessfully && second.IsCompletedSuccessfully)
            return ValueTask.CompletedTask;

        return new ValueTask(Task.WhenAll(first.AsTask(), second.AsTask()));
    }

    private void TryAddCodecExtension(
        CaptionCodecExtension extension,
        List<CaptionCodecRegistration> accepted,
        Dictionary<CaptionFormatId, HashSet<Extension>> owners)
    {
        try
        {
            CaptionCodecRegistration[] registrations = ValidateRegistrations(
                extension.Registrations,
                "A caption codec extension returned a null registration collection.",
                "A caption codec extension returned a null registration.");
            var candidate = new CaptionCodecRegistry(accepted.Concat(registrations));
            accepted.AddRange(registrations);
            foreach (CaptionCodecRegistration registration in registrations)
            {
                CaptionFormatId format = registration.Contribution.Format;
                if (registration.Mode == CaptionCodecRegistrationMode.Replace)
                    owners.Remove(format);
                if (!owners.TryGetValue(format, out HashSet<Extension>? formatOwners))
                {
                    formatOwners = new HashSet<Extension>(ReferenceEqualityComparer.Instance);
                    owners.Add(format, formatOwners);
                }
                formatOwners.Add(extension);
            }
        }
        catch (Exception ex)
        {
            ReportFailure(CaptionCatalogContributionKind.Codec, extension, ex);
        }
    }

    private void TryAddTemplateExtension(
        CaptionTemplateExtension extension,
        List<CaptionTemplateRegistration> accepted,
        Dictionary<CaptionTemplateId, Extension> owners)
    {
        try
        {
            CaptionTemplateRegistration[] registrations = ValidateRegistrations(
                extension.Registrations,
                "A caption template extension returned a null registration collection.",
                "A caption template extension returned a null registration.");
            var candidate = new CaptionTemplateRegistry();
            ValueTask validation = candidate.ReplaceAsync(accepted.Concat(registrations));
            if (!validation.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "Caption template registration validation unexpectedly required an asynchronous drain.");
            }
            accepted.AddRange(registrations);
            foreach (CaptionTemplateRegistration registration in registrations)
                owners[registration.Contribution.Id] = extension;
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

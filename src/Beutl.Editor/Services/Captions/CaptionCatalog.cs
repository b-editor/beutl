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
        => ComposeWithDefaultElementFactory(
            defaultTemplateName,
            objectTemplates,
            extensionProvider,
            DefaultTextCaptionElementFactory.Instance,
            reportFailure);

    /// <summary>
    /// Creates a dynamically composed catalog using a host-supplied factory for its default template.
    /// </summary>
    public static CaptionCatalog ComposeWithDefaultElementFactory(
        string defaultTemplateName,
        IEnumerable<ObjectTemplateItem> objectTemplates,
        IExtensionProvider extensionProvider,
        ICaptionElementFactory defaultElementFactory,
        Action<CaptionCatalogExtensionFailure>? reportFailure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTemplateName);
        ArgumentNullException.ThrowIfNull(objectTemplates);
        ArgumentNullException.ThrowIfNull(extensionProvider);
        ArgumentNullException.ThrowIfNull(defaultElementFactory);

        var defaultTemplateRegistration = new CaptionTemplateRegistration(
            CaptionTemplateDefaults.CreateDefaultText(defaultTemplateName, defaultElementFactory));
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
        var codecCandidates = new List<(
            CaptionCodecExtension Extension,
            CaptionCodecRegistration[] Registrations)>();
        foreach (CaptionCodecExtension extension in codecExtensions)
        {
            try
            {
                CaptionCodecRegistration[] registrations = ValidateRegistrations(
                        extension.Registrations,
                        "A caption codec extension returned a null registration collection.",
                        "A caption codec extension returned a null registration.")
                    .OrderBy(registration =>
                        registration.Mode == CaptionCodecRegistrationMode.Add ? 0 : 1)
                    .ToArray();
                codecCandidates.Add((extension, registrations));
            }
            catch (Exception ex)
            {
                ReportFailure(CaptionCatalogContributionKind.Codec, extension, ex);
            }
        }
        ComposeCodecExtensions(codecCandidates, codecRegistrations, codecOwners);

        List<CaptionTemplateRegistration> templateRegistrations = [.. _hostTemplateRegistrations];
        var templateOwners = new Dictionary<CaptionTemplateId, Extension>();
        CaptionTemplateExtension[] templateExtensions =
            extensionProvider.GetExtensions<CaptionTemplateExtension>();
        var templateCandidates = new List<(
            CaptionTemplateExtension Extension,
            CaptionTemplateRegistration[] Registrations)>();
        foreach (CaptionTemplateExtension extension in templateExtensions)
        {
            try
            {
                CaptionTemplateRegistration[] registrations = ValidateRegistrations(
                        extension.Registrations,
                        "A caption template extension returned a null registration collection.",
                        "A caption template extension returned a null registration.")
                    .OrderBy(registration =>
                        registration.Mode == CaptionTemplateRegistrationMode.Add ? 0 : 1)
                    .ToArray();
                templateCandidates.Add((extension, registrations));
            }
            catch (Exception ex)
            {
                ReportFailure(CaptionCatalogContributionKind.Template, extension, ex);
            }
        }
        ComposeTemplateExtensions(templateCandidates, templateRegistrations, templateOwners);

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

    private void ComposeCodecExtensions(
        List<(CaptionCodecExtension Extension, CaptionCodecRegistration[] Registrations)> candidates,
        List<CaptionCodecRegistration> accepted,
        Dictionary<CaptionFormatId, HashSet<Extension>> owners)
    {
        var failures = new Dictionary<CaptionCodecExtension, Exception>(
            ReferenceEqualityComparer.Instance);
        CaptionCodecRegistration[] hostRegistrations = accepted.ToArray();
        while (true)
        {
            var working = new List<CaptionCodecRegistration>(hostRegistrations);
            var newFailures = new Dictionary<
                CaptionCodecExtension,
                (Exception Exception, int Phase)>(
                ReferenceEqualityComparer.Instance);
            for (int phaseIndex = 0; phaseIndex < 2; phaseIndex++)
            {
                foreach ((CaptionCodecExtension extension, CaptionCodecRegistration[] registrations)
                         in candidates)
                {
                    if (failures.ContainsKey(extension) || newFailures.ContainsKey(extension))
                        continue;
                    CaptionCodecRegistration[] phase = registrations
                        .Where(registration => phaseIndex == 0
                            ? registration.Mode == CaptionCodecRegistrationMode.Add
                            : registration.Mode != CaptionCodecRegistrationMode.Add)
                        .ToArray();
                    if (phase.Length == 0)
                        continue;
                    try
                    {
                        _ = new CaptionCodecRegistry(working.Concat(phase));
                        working.AddRange(phase);
                    }
                    catch (Exception ex)
                    {
                        newFailures.Add(extension, (ex, phaseIndex));
                    }
                }
            }

            if (newFailures.Count > 0)
            {
                int latestFailedPhase = newFailures.Values.Max(failure => failure.Phase);
                KeyValuePair<CaptionCodecExtension, (Exception Exception, int Phase)> rejected =
                    newFailures.First(failure => failure.Value.Phase == latestFailedPhase);
                failures.TryAdd(rejected.Key, rejected.Value.Exception);
                continue;
            }

            accepted.Clear();
            accepted.AddRange(working);
            owners.Clear();
            for (int phaseIndex = 0; phaseIndex < 2; phaseIndex++)
            {
                foreach ((CaptionCodecExtension extension, CaptionCodecRegistration[] registrations)
                         in candidates.Where(candidate => !failures.ContainsKey(candidate.Extension)))
                {
                    foreach (CaptionCodecRegistration registration in registrations
                                 .Where(registration => phaseIndex == 0
                                     ? registration.Mode == CaptionCodecRegistrationMode.Add
                                     : registration.Mode != CaptionCodecRegistrationMode.Add))
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
            }
            break;
        }

        foreach ((CaptionCodecExtension extension, Exception failure) in failures)
        {
            ReportFailure(CaptionCatalogContributionKind.Codec, extension, failure);
        }
    }

    private void ComposeTemplateExtensions(
        List<(CaptionTemplateExtension Extension, CaptionTemplateRegistration[] Registrations)> candidates,
        List<CaptionTemplateRegistration> accepted,
        Dictionary<CaptionTemplateId, Extension> owners)
    {
        var failures = new Dictionary<CaptionTemplateExtension, Exception>(
            ReferenceEqualityComparer.Instance);
        CaptionTemplateRegistration[] hostRegistrations = accepted.ToArray();
        while (true)
        {
            var working = new List<CaptionTemplateRegistration>(hostRegistrations);
            var newFailures = new Dictionary<
                CaptionTemplateExtension,
                (Exception Exception, int Phase)>(
                ReferenceEqualityComparer.Instance);
            for (int phaseIndex = 0; phaseIndex < 2; phaseIndex++)
            {
                foreach ((CaptionTemplateExtension extension, CaptionTemplateRegistration[] registrations)
                         in candidates)
                {
                    if (failures.ContainsKey(extension) || newFailures.ContainsKey(extension))
                        continue;
                    CaptionTemplateRegistration[] phase = registrations
                        .Where(registration => phaseIndex == 0
                            ? registration.Mode == CaptionTemplateRegistrationMode.Add
                            : registration.Mode != CaptionTemplateRegistrationMode.Add)
                        .ToArray();
                    if (phase.Length == 0)
                        continue;
                    try
                    {
                        var candidate = new CaptionTemplateRegistry();
                        ValueTask validation = candidate.ReplaceAsync(working.Concat(phase));
                        if (!validation.IsCompletedSuccessfully)
                        {
                            throw new InvalidOperationException(
                                "Caption template registration validation unexpectedly required an asynchronous drain.");
                        }
                        working.AddRange(phase);
                    }
                    catch (Exception ex)
                    {
                        newFailures.Add(extension, (ex, phaseIndex));
                    }
                }
            }

            if (newFailures.Count > 0)
            {
                int latestFailedPhase = newFailures.Values.Max(failure => failure.Phase);
                KeyValuePair<CaptionTemplateExtension, (Exception Exception, int Phase)> rejected =
                    newFailures.First(failure => failure.Value.Phase == latestFailedPhase);
                failures.TryAdd(rejected.Key, rejected.Value.Exception);
                continue;
            }

            accepted.Clear();
            accepted.AddRange(working);
            owners.Clear();
            for (int phaseIndex = 0; phaseIndex < 2; phaseIndex++)
            {
                foreach ((CaptionTemplateExtension extension, CaptionTemplateRegistration[] registrations)
                         in candidates.Where(candidate => !failures.ContainsKey(candidate.Extension)))
                {
                    foreach (CaptionTemplateRegistration registration in registrations
                                 .Where(registration => phaseIndex == 0
                                     ? registration.Mode == CaptionTemplateRegistrationMode.Add
                                     : registration.Mode != CaptionTemplateRegistrationMode.Add))
                    {
                        owners[registration.Contribution.Id] = extension;
                    }
                }
            }
            break;
        }

        foreach ((CaptionTemplateExtension extension, Exception failure) in failures)
        {
            ReportFailure(CaptionCatalogContributionKind.Template, extension, failure);
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

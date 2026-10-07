using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

public enum CaptionCatalogContributionKind
{
    CodecDescriptor,
    Decoder,
    Encoder,
    TemplateDescriptor,
    ElementFactory,
    Placement,
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
    private readonly CaptionCodecRegistry _codecs;
    private readonly CaptionTemplateRegistry _templates;
    private readonly bool _ownsRegistries;
    private readonly IExtensionRegistry? _extensionProvider;
    private readonly CaptionCodecDescriptorRegistration[] _hostCodecDescriptors;
    private readonly CaptionDecoderRegistration[] _hostCodecDecoders;
    private readonly CaptionEncoderRegistration[] _hostCodecEncoders;
    private readonly CaptionTemplateRegistrationSet? _defaultTemplateRegistrations;
    private readonly Action<CaptionCatalogExtensionFailure>? _reportFailure;
    private readonly HashSet<CaptionCodecDescriptorExtension> _activeCodecDescriptorExtensions =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CaptionDecoderExtension> _activeDecoderExtensions =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CaptionEncoderExtension> _activeEncoderExtensions =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CaptionTemplateDescriptorExtension> _activeDescriptorExtensions =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CaptionElementFactoryExtension> _activeFactoryExtensions =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CaptionPlacementPolicyExtension> _activePlacementExtensions =
        new(ReferenceEqualityComparer.Instance);
    private CaptionTemplateDescriptorRegistration[] _hostTemplateDescriptors;
    private CaptionElementFactoryRegistration[] _hostTemplateFactories;
    private CaptionPlacementPolicyRegistration[] _hostTemplatePlacements;
    private Task? _disposeTask;
    private bool _disposed;

    public CaptionCatalog(
        CaptionCodecRegistry codecs,
        CaptionTemplateRegistry templates)
        : this(codecs, templates, ownsRegistries: false)
    {
    }

    private CaptionCatalog(
        CaptionCodecRegistry codecs,
        CaptionTemplateRegistry templates,
        bool ownsRegistries)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        ArgumentNullException.ThrowIfNull(templates);

        _codecs = codecs;
        _templates = templates;
        _ownsRegistries = ownsRegistries;
        Codecs = new CaptionCodecProviderView(codecs);
        Templates = new CaptionTemplateProviderView(templates);
        Serializer = new CaptionDocumentSerializer(Codecs);
        _hostCodecDescriptors = [];
        _hostCodecDecoders = [];
        _hostCodecEncoders = [];
        _hostTemplateDescriptors = [];
        _hostTemplateFactories = [];
        _hostTemplatePlacements = [];
    }

    private CaptionCatalog(
        IExtensionRegistry extensionProvider,
        CaptionCodecDescriptorRegistration[] hostCodecDescriptors,
        CaptionDecoderRegistration[] hostCaptionDecoders,
        CaptionEncoderRegistration[] hostCaptionEncoders,
        CaptionTemplateRegistrationSet defaultTemplateRegistrations,
        CaptionTemplateDescriptorRegistration[] hostTemplateDescriptors,
        CaptionElementFactoryRegistration[] hostTemplateFactories,
        CaptionPlacementPolicyRegistration[] hostTemplatePlacements,
        Action<CaptionCatalogExtensionFailure>? reportFailure)
        : this(new CaptionCodecRegistry(), new CaptionTemplateRegistry(), ownsRegistries: true)
    {
        _extensionProvider = extensionProvider;
        _hostCodecDescriptors = hostCodecDescriptors;
        _hostCodecDecoders = hostCaptionDecoders;
        _hostCodecEncoders = hostCaptionEncoders;
        _defaultTemplateRegistrations = defaultTemplateRegistrations;
        _hostTemplateDescriptors = hostTemplateDescriptors;
        _hostTemplateFactories = hostTemplateFactories;
        _hostTemplatePlacements = hostTemplatePlacements;
        _reportFailure = reportFailure;

        extensionProvider.SynchronizeMutation(() =>
        {
            lock (_gate)
            {
                extensionProvider.AllExtensions.CollectionChanged += OnExtensionsChanged;
                extensionProvider.ExtensionsChanged += OnExtensionsCommitted;
                try
                {
                    RebuildCore();
                }
                catch
                {
                    extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                    extensionProvider.ExtensionsChanged -= OnExtensionsCommitted;
                    throw;
                }
            }
        });
    }

    public ICaptionCodecProvider Codecs { get; }

    public ICaptionTemplateProvider Templates { get; }

    public CaptionDocumentSerializer Serializer { get; }

    public static CaptionCatalog CreateDefault(string defaultTemplateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTemplateName);
        CaptionTemplateRegistrationSet template =
            CaptionTemplateDefaults.CreateDefaultText(defaultTemplateName);
        var templates = new CaptionTemplateRegistry(
            [template.DescriptorRegistration],
            [template.ElementFactoryRegistration],
            [template.PlacementPolicyRegistration]);
        BuiltInCaptionCodecRegistrations codecs = CreateDefaultCodecRegistrations();
        return new CaptionCatalog(
            new CaptionCodecRegistry(codecs.Descriptors, codecs.Decoders, codecs.Encoders),
            templates,
            ownsRegistries: true);
    }

    internal static CaptionCatalog Compose(
        string defaultTemplateName,
        IEnumerable<ObjectTemplateItem> objectTemplates,
        IExtensionRegistry extensionProvider,
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
    internal static CaptionCatalog ComposeWithDefaultElementFactory(
        string defaultTemplateName,
        IEnumerable<ObjectTemplateItem> objectTemplates,
        IExtensionRegistry extensionProvider,
        ICaptionElementFactory defaultElementFactory,
        Action<CaptionCatalogExtensionFailure>? reportFailure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTemplateName);
        ArgumentNullException.ThrowIfNull(objectTemplates);
        ArgumentNullException.ThrowIfNull(extensionProvider);
        ArgumentNullException.ThrowIfNull(defaultElementFactory);

        CaptionTemplateRegistrationSet defaultTemplate =
            CaptionTemplateDefaults.CreateDefaultText(defaultTemplateName, defaultElementFactory);
        CaptionTemplateRegistrationSet[] objectTemplateRegistrations =
            CreateObjectTemplateRegistrations(objectTemplates);
        BuiltInCaptionCodecRegistrations codecs = CreateDefaultCodecRegistrations();
        (CaptionTemplateDescriptorRegistration[] descriptors,
            CaptionElementFactoryRegistration[] factories,
            CaptionPlacementPolicyRegistration[] placements) =
            PrependDefaultTemplate(defaultTemplate, objectTemplateRegistrations);
        return new CaptionCatalog(
            extensionProvider,
            codecs.Descriptors,
            codecs.Decoders,
            codecs.Encoders,
            defaultTemplate,
            descriptors,
            factories,
            placements,
            reportFailure);
    }

    /// <summary>
    /// Refreshes host-owned object templates while preserving all current extension registrations.
    /// </summary>
    public void RefreshObjectTemplates(IEnumerable<ObjectTemplateItem> objectTemplates)
    {
        ArgumentNullException.ThrowIfNull(objectTemplates);
        CaptionTemplateRegistrationSet[] registrations =
            CreateObjectTemplateRegistrations(objectTemplates);
        IExtensionRegistry extensionProvider = _extensionProvider
            ?? throw new InvalidOperationException(
                "Only a dynamically composed caption catalog can refresh object templates.");
        extensionProvider.SynchronizeMutation(() =>
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_defaultTemplateRegistrations is null)
                {
                    throw new InvalidOperationException(
                        "Only a dynamically composed caption catalog can refresh object templates.");
                }

                (_hostTemplateDescriptors, _hostTemplateFactories, _hostTemplatePlacements) =
                    PrependDefaultTemplate(_defaultTemplateRegistrations, registrations);
                RebuildCore();
            }
        });
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
                _extensionProvider.ExtensionsChanged -= OnExtensionsCommitted;
                Extension[] codecExtensions = SnapshotCodecExtensions();
                Extension[] templateExtensions = SnapshotTemplateExtensions();
                ClearActiveExtensions();
                CaptionRegistryDrain<Extension> retiredCodecs = _codecs.ReplaceOwned(
                    _hostCodecDescriptors,
                    _hostCodecDecoders,
                    _hostCodecEncoders,
                    new Dictionary<CaptionFormatId, Extension>(),
                    new Dictionary<CaptionFormatId, Extension>());
                CaptionRegistryDrain<Extension> retiredTemplates = _templates.ReplaceOwned(
                    _hostTemplateDescriptors,
                    _hostTemplateFactories,
                    _hostTemplatePlacements,
                    new Dictionary<CaptionTemplateId, Extension>(),
                    new Dictionary<CaptionTemplateId, Extension>(),
                    new Dictionary<CaptionTemplateId, Extension>());
                codecDrain = Combine(retiredCodecs.All, _codecs.DisposeAsync());
                templateDrain = Combine(retiredTemplates.All, _templates.DisposeAsync());
                RetireExtensions(codecExtensions, retiredCodecs);
                RetireExtensions(templateExtensions, retiredTemplates);
            }
            else
            {
                codecDrain = _ownsRegistries ? _codecs.DisposeAsync() : ValueTask.CompletedTask;
                templateDrain = _ownsRegistries ? _templates.DisposeAsync() : ValueTask.CompletedTask;
            }
        }

        return Combine(codecDrain, templateDrain).AsTask();
    }

    private void OnExtensionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            Rebuild();
    }

    private void OnExtensionsCommitted(object? sender, EventArgs e) => Rebuild();

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
        IExtensionRegistry extensionProvider = _extensionProvider
            ?? throw new InvalidOperationException("The caption catalog is not dynamically composed.");
        Extension[] previousCodecExtensions = SnapshotCodecExtensions();
        Extension[] previousTemplateExtensions = SnapshotTemplateExtensions();
        List<CaptionCodecDescriptorRegistration> codecDescriptors =
            [.. _hostCodecDescriptors];
        List<CaptionDecoderRegistration> captionDecoders = [.. _hostCodecDecoders];
        List<CaptionEncoderRegistration> captionEncoders = [.. _hostCodecEncoders];
        var decoderOwners = new Dictionary<CaptionFormatId, Extension>();
        var encoderOwners = new Dictionary<CaptionFormatId, Extension>();

        CaptionCodecDescriptorExtension[] codecDescriptorExtensions = ComposeCategory(
            extensionProvider, s_codecDescriptorCategory, codecDescriptors, owners: null);
        CaptionDecoderExtension[] decoderExtensions = ComposeCategory(
            extensionProvider, s_decoderCategory, captionDecoders, decoderOwners);
        CaptionEncoderExtension[] encoderExtensions = ComposeCategory(
            extensionProvider, s_encoderCategory, captionEncoders, encoderOwners);

        List<CaptionTemplateDescriptorRegistration> descriptorRegistrations =
            [.. _hostTemplateDescriptors];
        List<CaptionElementFactoryRegistration> factoryRegistrations =
            [.. _hostTemplateFactories];
        List<CaptionPlacementPolicyRegistration> placementRegistrations =
            [.. _hostTemplatePlacements];
        var descriptorOwners = new Dictionary<CaptionTemplateId, Extension>();
        var factoryOwners = new Dictionary<CaptionTemplateId, Extension>();
        var placementOwners = new Dictionary<CaptionTemplateId, Extension>();

        CaptionTemplateDescriptorExtension[] descriptorExtensions = ComposeCategory(
            extensionProvider, s_templateDescriptorCategory, descriptorRegistrations, descriptorOwners);
        CaptionElementFactoryExtension[] factoryExtensions = ComposeCategory(
            extensionProvider, s_elementFactoryCategory, factoryRegistrations, factoryOwners);
        CaptionPlacementPolicyExtension[] placementExtensions = ComposeCategory(
            extensionProvider, s_placementCategory, placementRegistrations, placementOwners);

        ReplaceContents(_activeCodecDescriptorExtensions, codecDescriptorExtensions);
        ReplaceContents(_activeDecoderExtensions, decoderExtensions);
        ReplaceContents(_activeEncoderExtensions, encoderExtensions);
        ReplaceContents(_activeDescriptorExtensions, descriptorExtensions);
        ReplaceContents(_activeFactoryExtensions, factoryExtensions);
        ReplaceContents(_activePlacementExtensions, placementExtensions);

        // Each replacement synchronously publishes a state that excludes removed packages. The
        // returned tasks drain calls that still hold the retired package-owned state.
        CaptionRegistryDrain<Extension> retiredCodecs =
            _codecs.ReplaceOwned(
                codecDescriptors,
                captionDecoders,
                captionEncoders,
                decoderOwners,
                encoderOwners);
        CaptionRegistryDrain<Extension> retiredTemplates =
            _templates.ReplaceOwned(
                descriptorRegistrations,
                factoryRegistrations,
                placementRegistrations,
                descriptorOwners,
                factoryOwners,
                placementOwners);
        ValueTask codecDrain = retiredCodecs.All;
        ValueTask templateDrain = retiredTemplates.All;
        // Track every replaced category state against the extensions that could be retained by
        // that state. This also covers an older state retired by a host-template refresh long
        // before its package is removed.
        RetireExtensions(previousCodecExtensions, retiredCodecs);
        RetireExtensions(previousTemplateExtensions, retiredTemplates);

        return Combine(codecDrain, templateDrain);
    }

    private static readonly ContributionCategory<
        CaptionCodecDescriptorExtension, CaptionCodecDescriptorRegistration, CaptionFormatId> s_codecDescriptorCategory = new(
        CaptionCatalogContributionKind.CodecDescriptor,
        "codec descriptor",
        extension => extension.Registrations,
        static registration => registration.Descriptor.Format,
        static registration => registration.Mode == CaptionCodecRegistrationMode.Add,
        registrations => new CaptionCodecRegistry(registrations, [], []));

    private static readonly ContributionCategory<
        CaptionDecoderExtension, CaptionDecoderRegistration, CaptionFormatId> s_decoderCategory = new(
        CaptionCatalogContributionKind.Decoder,
        "caption decoder",
        extension => extension.Registrations,
        static registration => registration.Format,
        static registration => registration.Mode == CaptionCodecRegistrationMode.Add,
        registrations => new CaptionCodecRegistry([], registrations, []));

    private static readonly ContributionCategory<
        CaptionEncoderExtension, CaptionEncoderRegistration, CaptionFormatId> s_encoderCategory = new(
        CaptionCatalogContributionKind.Encoder,
        "caption encoder",
        extension => extension.Registrations,
        static registration => registration.Format,
        static registration => registration.Mode == CaptionCodecRegistrationMode.Add,
        registrations => new CaptionCodecRegistry([], [], registrations));

    private static readonly ContributionCategory<
        CaptionTemplateDescriptorExtension, CaptionTemplateDescriptorRegistration, CaptionTemplateId> s_templateDescriptorCategory = new(
        CaptionCatalogContributionKind.TemplateDescriptor,
        "descriptor",
        extension => extension.Registrations,
        static registration => registration.Descriptor.Id,
        static registration => registration.Mode == CaptionTemplateRegistrationMode.Add,
        registrations => new CaptionTemplateRegistry(registrations, [], []));

    private static readonly ContributionCategory<
        CaptionElementFactoryExtension, CaptionElementFactoryRegistration, CaptionTemplateId> s_elementFactoryCategory = new(
        CaptionCatalogContributionKind.ElementFactory,
        "element-factory",
        extension => extension.Registrations,
        static registration => registration.TemplateId,
        static registration => registration.Mode == CaptionTemplateRegistrationMode.Add,
        registrations => new CaptionTemplateRegistry([], registrations, []));

    private static readonly ContributionCategory<
        CaptionPlacementPolicyExtension, CaptionPlacementPolicyRegistration, CaptionTemplateId> s_placementCategory = new(
        CaptionCatalogContributionKind.Placement,
        "placement",
        extension => extension.Registrations,
        static registration => registration.TemplateId,
        static registration => registration.Mode == CaptionTemplateRegistrationMode.Add,
        registrations => new CaptionTemplateRegistry([], [], registrations));

    private sealed record ContributionCategory<TExtension, TRegistration, TId>(
        CaptionCatalogContributionKind Kind,
        string CapabilityName,
        Func<TExtension, IReadOnlyCollection<TRegistration>?> GetRegistrations,
        Func<TRegistration, TId> GetId,
        Func<TRegistration, bool> IsAdd,
        Func<IEnumerable<TRegistration>, object> Validate)
        where TExtension : Extension
        where TRegistration : class
        where TId : notnull;

    private TExtension[] ComposeCategory<TExtension, TRegistration, TId>(
        IExtensionRegistry extensionProvider,
        ContributionCategory<TExtension, TRegistration, TId> category,
        List<TRegistration> accepted,
        Dictionary<TId, Extension>? owners)
        where TExtension : Extension
        where TRegistration : class
        where TId : notnull
    {
        TExtension[] extensions = extensionProvider.GetExtensions<TExtension>();
        ComposeSlotExtensions(
            CreateCandidates(extensions, category.GetRegistrations, category.Kind, category.CapabilityName),
            accepted,
            owners,
            category.GetId,
            category.IsAdd,
            category.Kind,
            category.Validate);
        return extensions;
    }

    private Extension[] SnapshotCodecExtensions()
        => [.. _activeCodecDescriptorExtensions, .. _activeDecoderExtensions, .. _activeEncoderExtensions];

    private Extension[] SnapshotTemplateExtensions()
        => [.. _activeDescriptorExtensions, .. _activeFactoryExtensions, .. _activePlacementExtensions];

    private void ClearActiveExtensions()
    {
        _activeCodecDescriptorExtensions.Clear();
        _activeDecoderExtensions.Clear();
        _activeEncoderExtensions.Clear();
        _activeDescriptorExtensions.Clear();
        _activeFactoryExtensions.Clear();
        _activePlacementExtensions.Clear();
    }

    private static void ReplaceContents<T>(HashSet<T> set, T[] items)
    {
        set.Clear();
        set.UnionWith(items);
    }

    private static void RetireExtensions(IEnumerable<Extension> extensions, CaptionRegistryDrain<Extension> drain)
    {
        foreach (Extension extension in extensions)
        {
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => new ValueTask(drain.DrainOwnerAsync(extension)));
        }
    }

    private static (CaptionTemplateDescriptorRegistration[] Descriptors,
        CaptionElementFactoryRegistration[] Factories,
        CaptionPlacementPolicyRegistration[] Placements) PrependDefaultTemplate(
        CaptionTemplateRegistrationSet defaultTemplate,
        CaptionTemplateRegistrationSet[] registrations)
        => ([defaultTemplate.DescriptorRegistration, .. registrations.Select(item => item.DescriptorRegistration)],
            [defaultTemplate.ElementFactoryRegistration, .. registrations.Select(item => item.ElementFactoryRegistration)],
            [defaultTemplate.PlacementPolicyRegistration, .. registrations.Select(item => item.PlacementPolicyRegistration)]);

    private static ValueTask Combine(ValueTask first, ValueTask second)
    {
        if (first.IsCompletedSuccessfully && second.IsCompletedSuccessfully)
            return ValueTask.CompletedTask;

        return new ValueTask(Task.WhenAll(first.AsTask(), second.AsTask()));
    }

    private sealed class CaptionCodecProviderView(CaptionCodecRegistry registry) : ICaptionCodecProvider
    {
        public Beutl.Collections.ICoreReadOnlyList<CaptionCodecInfo> Codecs => registry.Codecs;

        public bool TryGet(
            CaptionFormatId format,
            [NotNullWhen(true)] out CaptionCodecInfo? codec)
            => registry.TryGet(format, out codec);

        public CaptionCodecInfo GetRequired(CaptionFormatId format) => registry.GetRequired(format);

        public bool TryGetByFileExtension(
            string extension,
            [NotNullWhen(true)] out CaptionCodecInfo? codec)
            => registry.TryGetByFileExtension(extension, out codec);

        public bool TryGetByFileName(
            string fileName,
            [NotNullWhen(true)] out CaptionCodecInfo? codec)
            => registry.TryGetByFileName(fileName, out codec);

        public CaptionImportResult Decode(CaptionFormatId format, string content)
            => registry.Decode(format, content);

        public string Encode(CaptionFormatId format, CaptionDocument document)
            => registry.Encode(format, document);
    }

    private sealed class CaptionTemplateProviderView(CaptionTemplateRegistry registry) : ICaptionTemplateProvider
    {
        public Beutl.Collections.ICoreReadOnlyList<CaptionTemplateDescriptor> Templates
            => registry.Templates;

        public bool TryGet(
            CaptionTemplateId id,
            [NotNullWhen(true)] out CaptionTemplateDescriptor? template)
            => registry.TryGet(id, out template);

        public CaptionTemplateDescriptor GetRequired(CaptionTemplateId id) => registry.GetRequired(id);

        public ICaptionTemplateLease Acquire(CaptionTemplateId id) => registry.Acquire(id);
    }

    private List<(TExtension Extension, TRegistration[] Registrations)> CreateCandidates<
        TExtension,
        TRegistration>(
        IEnumerable<TExtension> extensions,
        Func<TExtension, IReadOnlyCollection<TRegistration>?> getRegistrations,
        CaptionCatalogContributionKind kind,
        string capabilityName)
        where TExtension : Extension
        where TRegistration : class
    {
        var result = new List<(TExtension Extension, TRegistration[] Registrations)>();
        foreach (TExtension extension in extensions)
        {
            try
            {
                TRegistration[] registrations = ValidateRegistrations(
                    getRegistrations(extension),
                    $"A caption {capabilityName} extension returned a null registration collection.",
                    $"A caption {capabilityName} extension returned a null registration.");
                result.Add((extension, registrations));
            }
            catch (Exception ex)
            {
                ReportFailure(kind, extension, ex);
            }
        }

        return result;
    }

    private void ComposeSlotExtensions<TExtension, TRegistration, TId>(
        List<(TExtension Extension, TRegistration[] Registrations)> candidates,
        List<TRegistration> accepted,
        Dictionary<TId, Extension>? owners,
        Func<TRegistration, TId> getId,
        Func<TRegistration, bool> isAdd,
        CaptionCatalogContributionKind kind,
        Func<IEnumerable<TRegistration>, object> validate)
        where TExtension : Extension
        where TRegistration : class
        where TId : notnull
    {
        var failures = new Dictionary<TExtension, Exception>(ReferenceEqualityComparer.Instance);
        TRegistration[] hostRegistrations = accepted.ToArray();
        while (true)
        {
            var working = new List<TRegistration>(hostRegistrations);
            var newFailures = new Dictionary<TExtension, (Exception Exception, int Phase)>(
                ReferenceEqualityComparer.Instance);
            for (int phaseIndex = 0; phaseIndex < 2; phaseIndex++)
            {
                foreach ((TExtension extension, TRegistration[] registrations) in candidates)
                {
                    if (failures.ContainsKey(extension) || newFailures.ContainsKey(extension))
                        continue;
                    TRegistration[] phase = registrations
                        .Where(registration => IsInPhase(registration, phaseIndex, isAdd))
                        .ToArray();
                    if (phase.Length == 0)
                        continue;
                    try
                    {
                        _ = validate(working.Concat(phase));
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
                KeyValuePair<TExtension, (Exception Exception, int Phase)> rejected =
                    newFailures.First(failure => failure.Value.Phase == latestFailedPhase);
                failures.TryAdd(rejected.Key, rejected.Value.Exception);
                continue;
            }

            accepted.Clear();
            accepted.AddRange(working);
            if (owners is null)
                break;

            owners.Clear();
            for (int phaseIndex = 0; phaseIndex < 2; phaseIndex++)
            {
                foreach ((TExtension extension, TRegistration[] registrations)
                         in candidates.Where(candidate => !failures.ContainsKey(candidate.Extension)))
                {
                    foreach (TRegistration registration in registrations.Where(registration =>
                                 IsInPhase(registration, phaseIndex, isAdd)))
                    {
                        owners[getId(registration)] = extension;
                    }
                }
            }
            break;
        }

        foreach ((TExtension extension, Exception failure) in failures)
        {
            ReportFailure(kind, extension, failure);
        }

        static bool IsInPhase(TRegistration registration, int phaseIndex, Func<TRegistration, bool> isAdd)
            => phaseIndex == 0 ? isAdd(registration) : !isAdd(registration);
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

    private static BuiltInCaptionCodecRegistrations CreateDefaultCodecRegistrations()
    {
        var srt = new SrtCaptionCodec();
        var webVtt = new WebVttCaptionCodec();
        var ass = new AssCaptionCodec();
        return new BuiltInCaptionCodecRegistrations(
            Descriptors:
            [
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(CaptionFormats.Srt, [".srt"])),
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(CaptionFormats.WebVtt, [".vtt"])),
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(CaptionFormats.Ass, [".ass", ".ssa"])),
            ],
            Decoders:
            [
                new CaptionDecoderRegistration(CaptionFormats.Srt, srt),
                new CaptionDecoderRegistration(CaptionFormats.WebVtt, webVtt),
                new CaptionDecoderRegistration(CaptionFormats.Ass, ass),
            ],
            Encoders:
            [
                new CaptionEncoderRegistration(CaptionFormats.Srt, srt),
                new CaptionEncoderRegistration(CaptionFormats.WebVtt, webVtt),
                new CaptionEncoderRegistration(CaptionFormats.Ass, ass),
            ]);
    }

    private sealed record BuiltInCaptionCodecRegistrations(
        CaptionCodecDescriptorRegistration[] Descriptors,
        CaptionDecoderRegistration[] Decoders,
        CaptionEncoderRegistration[] Encoders);

    private static CaptionTemplateRegistrationSet[] CreateObjectTemplateRegistrations(
        IEnumerable<ObjectTemplateItem> objectTemplates)
    {
        return objectTemplates
            .Select(TextBlockCaptionTemplateAdapter.TryCreate)
            .OfType<CaptionTemplateRegistrationSet>()
            .OrderBy(item => item.DescriptorRegistration.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DescriptorRegistration.Descriptor.Id.Value, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) =>
            {
                CaptionTemplateDescriptor descriptor = item.DescriptorRegistration.Descriptor;
                return new CaptionTemplateRegistrationSet(
                    new CaptionTemplateDescriptorRegistration(new CaptionTemplateDescriptor(
                        descriptor.Id,
                        descriptor.ProviderId,
                        descriptor.Name,
                        index)),
                    item.ElementFactoryRegistration,
                    item.PlacementPolicyRegistration);
            })
            .ToArray();
    }
}

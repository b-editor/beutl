using System.Diagnostics.CodeAnalysis;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Describes one currently available caption format without retaining its codec implementation.
/// </summary>
public sealed class CaptionCodecInfo
{
    internal CaptionCodecInfo(CaptionCodecContribution contribution)
    {
        Format = contribution.Format;
        FileExtensions = Array.AsReadOnly(contribution.HasDescriptor
            ? contribution.Descriptor.FileExtensions.Select(NormalizeExtension).ToArray()
            : []);
        CanDecode = contribution.Decoder is not null;
        CanEncode = contribution.Encoder is not null;
        Order = contribution.Order;
    }

    public CaptionFormatId Format { get; }

    public IReadOnlyList<string> FileExtensions { get; }

    public bool CanDecode { get; }

    public bool CanEncode { get; }

    public int Order { get; }

    private static string NormalizeExtension(string extension)
    {
        string normalized = extension.Trim();
        return normalized[0] == '.' ? normalized : '.' + normalized;
    }
}

/// <summary>
/// Resolves independent caption capabilities by format identifier or file extension. Codec
/// implementations are invoked through short-lived leases so replacing the registry waits for
/// in-flight calls before releasing package-owned instances.
/// </summary>
public sealed class CaptionCodecRegistry
{
    private readonly object _mutationGate = new();
    private readonly object _stateGate = new();
    private State _state = State.Create([]);

    public CaptionCodecRegistry()
    {
    }

    public CaptionCodecRegistry(IEnumerable<CaptionCodecRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _state = State.Create(registrations);
    }

    public IReadOnlyList<CaptionCodecInfo> Codecs
    {
        get
        {
            lock (_stateGate)
                return _state.Codecs;
        }
    }

    public ValueTask RegisterAsync(CaptionCodecRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_mutationGate)
        {
            IReadOnlyDictionary<CaptionFormatId, IReadOnlyCollection<Extension>> owners;
            CaptionCodecRegistration[] registrations;
            lock (_stateGate)
            {
                registrations =
                [
                    .. _state.Contributions.Select(contribution =>
                        new CaptionCodecRegistration(contribution)),
                    registration,
                ];
                owners = _state.Owners;
            }

            return SwapState(State.Create(registrations, owners)).All;
        }
    }

    /// <summary>
    /// Atomically replaces every registration and drains calls using the previous state.
    /// </summary>
    public ValueTask ReplaceAsync(IEnumerable<CaptionCodecRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        CaptionCodecRegistration[] snapshot = registrations.ToArray();
        lock (_mutationGate)
        {
            IReadOnlyDictionary<CaptionFormatId, IReadOnlyCollection<Extension>> owners;
            lock (_stateGate)
                owners = _state.Owners;

            return SwapState(State.Create(snapshot, owners)).All;
        }
    }

    internal CaptionRegistryDrain<Extension> ReplaceOwned(
        IEnumerable<CaptionCodecRegistration> registrations,
        IReadOnlyDictionary<CaptionFormatId, IReadOnlyCollection<Extension>> owners)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(owners);
        CaptionCodecRegistration[] snapshot = registrations.ToArray();
        lock (_mutationGate)
        {
            return SwapState(State.Create(snapshot, owners));
        }
    }

    public bool TryGet(
        CaptionFormatId format,
        [NotNullWhen(true)] out CaptionCodecInfo? codec)
    {
        lock (_stateGate)
            return _state.CodecsByFormat.TryGetValue(format, out codec);
    }

    public CaptionCodecInfo GetRequired(CaptionFormatId format)
    {
        if (TryGet(format, out CaptionCodecInfo? codec))
            return codec;

        throw new KeyNotFoundException($"No caption codec is registered for format '{format}'.");
    }

    public bool TryGetByFileExtension(
        string extension,
        [NotNullWhen(true)] out CaptionCodecInfo? codec)
    {
        string normalized = NormalizeExtension(extension);
        lock (_stateGate)
        {
            if (_state.FormatsByExtension.TryGetValue(normalized, out CaptionFormatId format))
                return _state.CodecsByFormat.TryGetValue(format, out codec);
        }

        codec = null;
        return false;
    }

    public bool TryGetByFileName(
        string fileName,
        [NotNullWhen(true)] out CaptionCodecInfo? codec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        string extension = Path.GetExtension(fileName);
        if (extension.Length > 0)
            return TryGetByFileExtension(extension, out codec);

        codec = null;
        return false;
    }

    public CaptionImportResult Decode(CaptionFormatId format, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using StateLease lease = AcquireState(format);
        CaptionCodecContribution contribution = lease.Contribution;
        ICaptionDecoder decoder = contribution.Decoder
            ?? throw new NotSupportedException($"Caption format '{format}' does not support decoding.");
        return decoder.Decode(content)
               ?? throw new InvalidOperationException(
                   $"Caption decoder '{format}' returned a null import result.");
    }

    public string Encode(CaptionFormatId format, CaptionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using StateLease lease = AcquireState(format);
        CaptionCodecContribution contribution = lease.Contribution;
        ICaptionEncoder encoder = contribution.Encoder
            ?? throw new NotSupportedException($"Caption format '{format}' does not support encoding.");
        return encoder.Encode(document)
               ?? throw new InvalidOperationException(
                   $"Caption encoder '{format}' returned null export content.");
    }

    private StateLease AcquireState(CaptionFormatId format)
    {
        lock (_stateGate)
        {
            State state = _state;
            CaptionCodecContribution contribution = state.GetRequired(format);
            return new StateLease(
                contribution,
                state.AcquireLease(state.GetOwners(contribution)));
        }
    }

    private CaptionRegistryDrain<Extension> SwapState(State next)
    {
        State previous;
        lock (_stateGate)
        {
            previous = _state;
            _state = next;
        }

        return new CaptionRegistryDrain<Extension>(
            previous.RetireAsync(),
            previous.DrainOwnerAsync);
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.Trim();
        if (normalized[0] != '.')
            normalized = '.' + normalized;

        if (normalized.Length == 1
            || normalized.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) >= 0)
        {
            throw new ArgumentException($"'{extension}' is not a valid file extension.", nameof(extension));
        }

        return normalized;
    }

    private sealed class State : CaptionRegistryLeaseState<Extension>
    {
        private State(
            Dictionary<CaptionFormatId, CaptionCodecContribution> contributions,
            Dictionary<string, CaptionFormatId> formatsByExtension,
            IReadOnlyDictionary<CaptionFormatId, IReadOnlyCollection<Extension>> owners)
        {
            ContributionsByFormat = contributions;
            Contributions = contributions.Values.ToArray();
            FormatsByExtension = formatsByExtension;
            CaptionCodecInfo[] codecs = contributions.Values
                .Select(contribution => new CaptionCodecInfo(contribution))
                .OrderBy(codec => codec.Order)
                .ThenBy(codec => codec.Format.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Codecs = Array.AsReadOnly(codecs);
            CodecsByFormat = codecs.ToDictionary(codec => codec.Format);
            Owners = owners;
        }

        public IReadOnlyList<CaptionCodecContribution> Contributions { get; }

        public Dictionary<CaptionFormatId, CaptionCodecContribution> ContributionsByFormat { get; }

        public Dictionary<CaptionFormatId, CaptionCodecInfo> CodecsByFormat { get; }

        public IReadOnlyList<CaptionCodecInfo> Codecs { get; }

        public Dictionary<string, CaptionFormatId> FormatsByExtension { get; }

        public IReadOnlyDictionary<CaptionFormatId, IReadOnlyCollection<Extension>> Owners { get; }

        public IReadOnlyCollection<Extension> GetOwners(CaptionCodecContribution contribution)
            => Owners.TryGetValue(
                    contribution.Format,
                    out IReadOnlyCollection<Extension>? owners)
                ? owners
                : [];

        public CaptionCodecContribution GetRequired(CaptionFormatId format)
        {
            if (ContributionsByFormat.TryGetValue(format, out CaptionCodecContribution? contribution))
                return contribution;

            throw new KeyNotFoundException(
                $"No caption contribution is registered for format '{format}'.");
        }

        public static State Create(
            IEnumerable<CaptionCodecRegistration> registrations,
            IReadOnlyDictionary<CaptionFormatId, IReadOnlyCollection<Extension>>? owners = null)
        {
            var contributions = new Dictionary<CaptionFormatId, CaptionCodecContribution>();
            var formatsByExtension = new Dictionary<string, CaptionFormatId>(StringComparer.OrdinalIgnoreCase);
            foreach (CaptionCodecRegistration registration in registrations)
            {
                Apply(registration, contributions, formatsByExtension);
            }

            return new State(
                contributions,
                formatsByExtension,
                owners ?? new Dictionary<CaptionFormatId, IReadOnlyCollection<Extension>>());
        }

        private static void Apply(
            CaptionCodecRegistration registration,
            Dictionary<CaptionFormatId, CaptionCodecContribution> contributions,
            Dictionary<string, CaptionFormatId> formatsByExtension)
        {
            ArgumentNullException.ThrowIfNull(registration);
            CaptionCodecContribution contribution = registration.Contribution;
            ArgumentNullException.ThrowIfNull(contribution);
            bool exists = contributions.TryGetValue(
                contribution.Format,
                out CaptionCodecContribution? existing);

            CaptionCodecContribution candidate = registration.Mode switch
            {
                CaptionCodecRegistrationMode.Add when exists => throw new ArgumentException(
                    $"Caption format '{contribution.Format}' is already registered. "
                    + "Use Merge or Replace explicitly.",
                    nameof(registration)),
                CaptionCodecRegistrationMode.Add => contribution,
                CaptionCodecRegistrationMode.Merge when !exists => throw new ArgumentException(
                    $"Caption format '{contribution.Format}' cannot be merged because it is not registered.",
                    nameof(registration)),
                CaptionCodecRegistrationMode.Merge => Merge(existing!, contribution, registration),
                CaptionCodecRegistrationMode.Replace when !exists => throw new ArgumentException(
                    $"Caption format '{contribution.Format}' cannot be replaced because it is not registered.",
                    nameof(registration)),
                CaptionCodecRegistrationMode.Replace => contribution,
                _ => throw new ArgumentOutOfRangeException(nameof(registration)),
            };

            string[] normalizedExtensions = candidate.HasDescriptor
                ? candidate.Descriptor.FileExtensions.Select(NormalizeExtension).ToArray()
                : [];
            if (normalizedExtensions.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != normalizedExtensions.Length)
            {
                throw new ArgumentException(
                    $"Caption format '{contribution.Format}' declares a file extension more than once.",
                    nameof(registration));
            }

            foreach (string extension in normalizedExtensions)
            {
                if (formatsByExtension.TryGetValue(extension, out CaptionFormatId existingFormat)
                    && existingFormat != contribution.Format)
                {
                    throw new ArgumentException(
                        $"File extension '{extension}' is already registered for caption format "
                        + $"'{existingFormat}'.",
                        nameof(registration));
                }
            }

            if (existing?.HasDescriptor == true)
            {
                foreach (string extension in existing.Descriptor.FileExtensions)
                {
                    formatsByExtension.Remove(NormalizeExtension(extension));
                }
            }

            contributions[contribution.Format] = candidate;
            foreach (string extension in normalizedExtensions)
            {
                formatsByExtension[extension] = contribution.Format;
            }
        }

        private static CaptionCodecContribution Merge(
            CaptionCodecContribution existing,
            CaptionCodecContribution contribution,
            CaptionCodecRegistration registration)
        {
            if (existing.HasDescriptor && contribution.HasDescriptor)
            {
                throw new ArgumentException(
                    $"A descriptor for caption format '{contribution.Format}' is already registered.",
                    nameof(registration));
            }
            if (existing.Decoder is not null && contribution.Decoder is not null)
            {
                throw new ArgumentException(
                    $"A decoder for caption format '{contribution.Format}' is already registered.",
                    nameof(registration));
            }
            if (existing.Encoder is not null && contribution.Encoder is not null)
            {
                throw new ArgumentException(
                    $"An encoder for caption format '{contribution.Format}' is already registered.",
                    nameof(registration));
            }

            return new CaptionCodecContribution(
                contribution.Format,
                contribution.HasDescriptor
                    ? contribution.Descriptor
                    : existing.HasDescriptor ? existing.Descriptor : null,
                contribution.Decoder ?? existing.Decoder,
                contribution.Encoder ?? existing.Encoder,
                existing.Order);
        }
    }

    private sealed class StateLease(
        CaptionCodecContribution contribution,
        IDisposable lifetime) : IDisposable
    {
        private CaptionCodecContribution? _contribution = contribution;
        private IDisposable? _lifetime = lifetime;

        public CaptionCodecContribution Contribution
            => _contribution ?? throw new ObjectDisposedException(nameof(StateLease));

        public void Dispose()
        {
            _contribution = null;
            Interlocked.Exchange(ref _lifetime, null)?.Dispose();
        }
    }

}

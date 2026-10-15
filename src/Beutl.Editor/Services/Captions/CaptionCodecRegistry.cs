using System.Diagnostics.CodeAnalysis;
using Beutl.Collections;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Describes one caption format whose descriptor is currently registered. Decoder-only and
/// encoder-only formats remain available by exact identifier but do not appear in this metadata
/// view or file-extension lookup.
/// </summary>
public sealed class CaptionCodecInfo
{
    // The registry has already normalized and checked the descriptor's extensions.
    internal CaptionCodecInfo(
        CaptionCodecDescriptor descriptor,
        string[] normalizedExtensions,
        bool canDecode,
        bool canEncode)
    {
        Format = descriptor.Format;
        FileExtensions = Array.AsReadOnly(normalizedExtensions);
        CanDecode = canDecode;
        CanEncode = canEncode;
        Order = descriptor.Order;
    }

    public CaptionCodecInfo(
        CaptionFormatId format,
        IEnumerable<string> fileExtensions,
        bool canDecode,
        bool canEncode,
        int order = 0)
    {
        if (format.Value.Length == 0)
            throw new ArgumentException("A caption format identifier is required.", nameof(format));
        ArgumentNullException.ThrowIfNull(fileExtensions);
        string[] normalizedExtensions = fileExtensions.Select(NormalizeExtension).ToArray();
        if (normalizedExtensions.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != normalizedExtensions.Length)
        {
            throw new ArgumentException(
                "A caption codec cannot describe the same file extension more than once.",
                nameof(fileExtensions));
        }

        Format = format;
        FileExtensions = Array.AsReadOnly(normalizedExtensions);
        CanDecode = canDecode;
        CanEncode = canEncode;
        Order = order;
    }

    public CaptionFormatId Format { get; }

    public IReadOnlyList<string> FileExtensions { get; }

    public bool CanDecode { get; }

    public bool CanEncode { get; }

    public int Order { get; }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.Trim();
        return normalized[0] == '.' ? normalized : '.' + normalized;
    }
}

/// <summary>
/// Resolves caption capabilities by exact format identifier. Only formats with registered
/// descriptors appear in <see cref="Codecs"/> and file-extension lookup.
/// </summary>
public interface ICaptionCodecProvider
{
    ICoreReadOnlyList<CaptionCodecInfo> Codecs { get; }

    bool TryGet(
        CaptionFormatId format,
        [NotNullWhen(true)] out CaptionCodecInfo? codec);

    CaptionCodecInfo GetRequired(CaptionFormatId format);

    bool TryGetByFileExtension(
        string extension,
        [NotNullWhen(true)] out CaptionCodecInfo? codec);

    bool TryGetByFileName(
        string fileName,
        [NotNullWhen(true)] out CaptionCodecInfo? codec);

    CaptionImportResult Decode(CaptionFormatId format, string content);

    string Encode(CaptionFormatId format, CaptionDocument document);
}

/// <summary>Composes descriptor, decoder, and encoder slots independently by format.</summary>
public sealed class CaptionCodecRegistry : ICaptionCodecProvider, IAsyncDisposable
{
    private readonly CoreList<CaptionCodecInfo> _codecs = [];
    private readonly CaptionRegistryStates<State> _states;

    public CaptionCodecRegistry()
    {
        _states = CreateStates(State.Create([], [], []));
    }

    public CaptionCodecRegistry(
        IEnumerable<CaptionCodecDescriptorRegistration> descriptors,
        IEnumerable<CaptionDecoderRegistration> decoders,
        IEnumerable<CaptionEncoderRegistration> encoders)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(decoders);
        ArgumentNullException.ThrowIfNull(encoders);
        State state = State.Create(descriptors, decoders, encoders);
        _codecs.Replace(state.Codecs);
        _states = CreateStates(state);
    }

    public ICoreReadOnlyList<CaptionCodecInfo> Codecs => _codecs;

    internal CaptionRegistryDrain ReplaceOwned(
        IEnumerable<CaptionCodecDescriptorRegistration> descriptors,
        IEnumerable<CaptionDecoderRegistration> decoders,
        IEnumerable<CaptionEncoderRegistration> encoders,
        IReadOnlyDictionary<CaptionFormatId, Extension> decoderOwners,
        IReadOnlyDictionary<CaptionFormatId, Extension> encoderOwners)
        => _states.Replace(() => State.Create(
            descriptors,
            decoders,
            encoders,
            ToObjectOwners(decoderOwners),
            ToObjectOwners(encoderOwners)));

    public bool TryGet(
        CaptionFormatId format,
        [NotNullWhen(true)] out CaptionCodecInfo? codec)
    {
        lock (_states.StateGate)
            return _states.Current.CodecsByFormat.TryGetValue(format, out codec);
    }

    public CaptionCodecInfo GetRequired(CaptionFormatId format)
    {
        if (TryGet(format, out CaptionCodecInfo? codec))
            return codec;
        throw new KeyNotFoundException(
            $"No caption codec descriptor is registered for format '{format}'.");
    }

    public bool TryGetByFileExtension(
        string extension,
        [NotNullWhen(true)] out CaptionCodecInfo? codec)
    {
        string normalized = NormalizeExtension(extension);
        lock (_states.StateGate)
        {
            if (_states.Current.FormatsByExtension.TryGetValue(normalized, out CaptionFormatId format))
                return _states.Current.CodecsByFormat.TryGetValue(format, out codec);
        }

        codec = null;
        return false;
    }

    public bool TryGetByFileName(
        string fileName,
        [NotNullWhen(true)] out CaptionCodecInfo? codec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        lock (_states.StateGate)
        {
            KeyValuePair<string, CaptionFormatId>? bestMatch = null;
            foreach (KeyValuePair<string, CaptionFormatId> candidate in _states.Current.FormatsByExtension)
            {
                if (fileName.EndsWith(candidate.Key, StringComparison.OrdinalIgnoreCase)
                    && (bestMatch is null || candidate.Key.Length > bestMatch.Value.Key.Length))
                {
                    bestMatch = candidate;
                }
            }

            if (bestMatch is { } match)
                return _states.Current.CodecsByFormat.TryGetValue(match.Value, out codec);
        }

        codec = null;
        return false;
    }

    public CaptionImportResult Decode(CaptionFormatId format, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using CapabilityLease<ICaptionDecoder> lease = AcquireDecoder(format);
        return lease.Capability.Decode(content)
               ?? throw new InvalidOperationException(
                   $"Caption decoder '{format}' returned a null import result.");
    }

    public string Encode(CaptionFormatId format, CaptionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using CapabilityLease<ICaptionEncoder> lease = AcquireEncoder(format);
        return lease.Capability.Encode(document)
               ?? throw new InvalidOperationException(
                   $"Caption encoder '{format}' returned null export content.");
    }

    public ValueTask DisposeAsync() => _states.DisposeAsync();

    private CaptionRegistryStates<State> CreateStates(State initial)
        => new(this, initial, static () => State.Create([], [], []), PublishMetadata);

    private CapabilityLease<ICaptionDecoder> AcquireDecoder(CaptionFormatId format)
    {
        lock (_states.StateGate)
        {
            State state = _states.Current;
            if (!state.Decoders.TryGetValue(format, out ICaptionDecoder? decoder))
            {
                throw new NotSupportedException(
                    $"Caption format '{format}' does not support decoding.");
            }

            return new CapabilityLease<ICaptionDecoder>(
                decoder,
                state.AcquireLease(state.GetDecoderOwners(format)));
        }
    }

    private CapabilityLease<ICaptionEncoder> AcquireEncoder(CaptionFormatId format)
    {
        lock (_states.StateGate)
        {
            State state = _states.Current;
            if (!state.Encoders.TryGetValue(format, out ICaptionEncoder? encoder))
            {
                throw new NotSupportedException(
                    $"Caption format '{format}' does not support encoding.");
            }

            return new CapabilityLease<ICaptionEncoder>(
                encoder,
                state.AcquireLease(state.GetEncoderOwners(format)));
        }
    }

    private void PublishMetadata(State state)
    {
        if (!HaveSameMetadata(_codecs, state.Codecs))
            _codecs.Replace(state.Codecs);
    }

    private static bool HaveSameMetadata(
        IReadOnlyList<CaptionCodecInfo> current,
        IReadOnlyList<CaptionCodecInfo> next)
    {
        if (current.Count != next.Count)
            return false;

        for (int index = 0; index < current.Count; index++)
        {
            CaptionCodecInfo left = current[index];
            CaptionCodecInfo right = next[index];
            if (left.Format != right.Format
                || left.CanDecode != right.CanDecode
                || left.CanEncode != right.CanEncode
                || left.Order != right.Order
                || !left.FileExtensions.SequenceEqual(
                    right.FileExtensions,
                    StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyDictionary<CaptionFormatId, object> ToObjectOwners(
        IReadOnlyDictionary<CaptionFormatId, Extension> owners)
        => owners.ToDictionary(pair => pair.Key, pair => (object)pair.Value);

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

    private sealed class State : CaptionRegistryLeaseState
    {
        private State(
            Dictionary<CaptionFormatId, CaptionCodecDescriptor> descriptors,
            Dictionary<CaptionFormatId, string[]> extensionsByFormat,
            Dictionary<CaptionFormatId, ICaptionDecoder> decoders,
            Dictionary<CaptionFormatId, ICaptionEncoder> encoders,
            Dictionary<string, CaptionFormatId> formatsByExtension,
            IReadOnlyDictionary<CaptionFormatId, object> decoderOwners,
            IReadOnlyDictionary<CaptionFormatId, object> encoderOwners)
        {
            Decoders = decoders;
            Encoders = encoders;
            FormatsByExtension = formatsByExtension;
            DecoderOwners = decoderOwners;
            EncoderOwners = encoderOwners;
            CaptionCodecInfo[] codecs = descriptors.Values
                .Select(descriptor => new CaptionCodecInfo(
                    descriptor,
                    extensionsByFormat[descriptor.Format],
                    decoders.ContainsKey(descriptor.Format),
                    encoders.ContainsKey(descriptor.Format)))
                .OrderBy(codec => codec.Order)
                .ThenBy(codec => codec.Format.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Codecs = codecs;
            CodecsByFormat = codecs.ToDictionary(codec => codec.Format);
        }

        public Dictionary<CaptionFormatId, ICaptionDecoder> Decoders { get; private set; }

        public Dictionary<CaptionFormatId, ICaptionEncoder> Encoders { get; private set; }

        public CaptionCodecInfo[] Codecs { get; private set; }

        public Dictionary<CaptionFormatId, CaptionCodecInfo> CodecsByFormat { get; private set; }

        public Dictionary<string, CaptionFormatId> FormatsByExtension { get; private set; }

        public IReadOnlyDictionary<CaptionFormatId, object> DecoderOwners { get; private set; }

        public IReadOnlyDictionary<CaptionFormatId, object> EncoderOwners { get; private set; }

        public IReadOnlyCollection<object> GetDecoderOwners(CaptionFormatId format)
            => DecoderOwners.TryGetValue(format, out object? owner) ? [owner] : [];

        public IReadOnlyCollection<object> GetEncoderOwners(CaptionFormatId format)
            => EncoderOwners.TryGetValue(format, out object? owner) ? [owner] : [];

        protected override void ReleasePayload()
        {
            Decoders = [];
            Encoders = [];
            Codecs = [];
            CodecsByFormat = [];
            FormatsByExtension = new Dictionary<string, CaptionFormatId>(
                StringComparer.OrdinalIgnoreCase);
            DecoderOwners = new Dictionary<CaptionFormatId, object>();
            EncoderOwners = new Dictionary<CaptionFormatId, object>();
        }

        public static State Create(
            IEnumerable<CaptionCodecDescriptorRegistration> descriptorRegistrations,
            IEnumerable<CaptionDecoderRegistration> decoderRegistrations,
            IEnumerable<CaptionEncoderRegistration> encoderRegistrations,
            IReadOnlyDictionary<CaptionFormatId, object>? decoderOwners = null,
            IReadOnlyDictionary<CaptionFormatId, object>? encoderOwners = null)
        {
            var descriptors = new Dictionary<CaptionFormatId, CaptionCodecDescriptor>();
            var decoders = new Dictionary<CaptionFormatId, ICaptionDecoder>();
            var encoders = new Dictionary<CaptionFormatId, ICaptionEncoder>();
            foreach (CaptionCodecDescriptorRegistration registration in descriptorRegistrations)
            {
                ApplyRegistration(
                    descriptors,
                    registration.Descriptor.Format,
                    registration.Descriptor,
                    registration.Mode == CaptionCodecRegistrationMode.Replace,
                    "Caption format",
                    "descriptor");
            }
            foreach (CaptionDecoderRegistration registration in decoderRegistrations)
            {
                ApplyRegistration(
                    decoders,
                    registration.Format,
                    registration.Decoder,
                    registration.Mode == CaptionCodecRegistrationMode.Replace,
                    "Caption format",
                    "decoder");
            }
            foreach (CaptionEncoderRegistration registration in encoderRegistrations)
            {
                ApplyRegistration(
                    encoders,
                    registration.Format,
                    registration.Encoder,
                    registration.Mode == CaptionCodecRegistrationMode.Replace,
                    "Caption format",
                    "encoder");
            }

            var formatsByExtension = new Dictionary<string, CaptionFormatId>(
                StringComparer.OrdinalIgnoreCase);
            var extensionsByFormat = new Dictionary<CaptionFormatId, string[]>();
            foreach (CaptionCodecDescriptor descriptor in descriptors.Values)
            {
                var extensions = new List<string>(descriptor.FileExtensions.Count);
                foreach (string extension in descriptor.FileExtensions)
                {
                    string normalized = NormalizeExtension(extension);
                    if (!formatsByExtension.TryAdd(normalized, descriptor.Format))
                    {
                        throw new ArgumentException(
                            $"File extension '{normalized}' is registered more than once.",
                            nameof(descriptorRegistrations));
                    }

                    extensions.Add(normalized);
                }

                extensionsByFormat.Add(descriptor.Format, [.. extensions]);
            }

            return new State(
                descriptors,
                extensionsByFormat,
                decoders,
                encoders,
                formatsByExtension,
                decoderOwners ?? new Dictionary<CaptionFormatId, object>(),
                encoderOwners ?? new Dictionary<CaptionFormatId, object>());
        }
    }

    private sealed class CapabilityLease<TCapability>(
        TCapability capability,
        IDisposable lifetime) : IDisposable
        where TCapability : class
    {
        private TCapability? _capability = capability;
        private IDisposable? _lifetime = lifetime;

        public TCapability Capability
            => _capability ?? throw new ObjectDisposedException(nameof(CapabilityLease<TCapability>));

        public void Dispose()
        {
            _capability = null;
            Interlocked.Exchange(ref _lifetime, null)?.Dispose();
        }
    }
}

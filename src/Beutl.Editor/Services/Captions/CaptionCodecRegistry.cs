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
    internal CaptionCodecInfo(
        CaptionCodecDescriptor descriptor,
        bool canDecode,
        bool canEncode)
        : this(
            descriptor.Format,
            descriptor.FileExtensions,
            canDecode,
            canEncode,
            descriptor.Order)
    {
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

/// <summary>Owns one descriptor registration.</summary>
public interface ICaptionCodecDescriptorRegistration : IAsyncDisposable
{
}

/// <summary>Owns one decoder registration and drains its active decode calls on disposal.</summary>
public interface ICaptionDecoderRegistration : IAsyncDisposable
{
}

/// <summary>Owns one encoder registration and drains its active encode calls on disposal.</summary>
public interface ICaptionEncoderRegistration : IAsyncDisposable
{
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
    private readonly object _mutationGate = new();
    private readonly object _stateGate = new();
    private readonly CoreList<CaptionCodecInfo> _codecs = [];
    private readonly List<DescriptorEntry> _descriptorEntries = [];
    private readonly List<DecoderEntry> _decoderEntries = [];
    private readonly List<EncoderEntry> _encoderEntries = [];
    private readonly HashSet<State> _retiredStates = [];
    private State _state = State.Create([], [], []);
    private Task? _disposeTask;
    private bool _disposed;

    public CaptionCodecRegistry()
    {
    }

    public CaptionCodecRegistry(
        IEnumerable<CaptionCodecDescriptorRegistration> descriptors,
        IEnumerable<CaptionDecoderRegistration> decoders,
        IEnumerable<CaptionEncoderRegistration> encoders)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(decoders);
        ArgumentNullException.ThrowIfNull(encoders);
        _descriptorEntries.AddRange(descriptors.Select(registration =>
            new DescriptorEntry(registration)));
        _decoderEntries.AddRange(decoders.Select(registration => new DecoderEntry(registration)));
        _encoderEntries.AddRange(encoders.Select(registration => new EncoderEntry(registration)));
        _state = State.Create(
            _descriptorEntries.Select(entry => entry.Registration),
            _decoderEntries.Select(entry => entry.Registration),
            _encoderEntries.Select(entry => entry.Registration));
        _codecs.Replace(_state.Codecs);
    }

    public ICoreReadOnlyList<CaptionCodecInfo> Codecs => _codecs;

    public ICaptionCodecDescriptorRegistration Register(
        CaptionCodecDescriptorRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            ValidateRegistrationMode(
                registration.Descriptor.Format,
                registration.Mode,
                _descriptorEntries.Any(entry =>
                    entry.Registration.Descriptor.Format == registration.Descriptor.Format),
                "descriptor");
            var entry = new DescriptorEntry(registration);
            _descriptorEntries.Add(entry);
            try
            {
                SwapState(CreateDirectState());
                return new DescriptorHandle(this, entry);
            }
            catch
            {
                _descriptorEntries.Remove(entry);
                throw;
            }
        }
    }

    public ICaptionDecoderRegistration Register(CaptionDecoderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            ValidateRegistrationMode(
                registration.Format,
                registration.Mode,
                _decoderEntries.Any(entry => entry.Registration.Format == registration.Format),
                "decoder");
            var entry = new DecoderEntry(registration);
            _decoderEntries.Add(entry);
            try
            {
                SwapState(CreateDirectState());
                return new DecoderHandle(this, entry);
            }
            catch
            {
                _decoderEntries.Remove(entry);
                throw;
            }
        }
    }

    public ICaptionEncoderRegistration Register(CaptionEncoderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            ValidateRegistrationMode(
                registration.Format,
                registration.Mode,
                _encoderEntries.Any(entry => entry.Registration.Format == registration.Format),
                "encoder");
            var entry = new EncoderEntry(registration);
            _encoderEntries.Add(entry);
            try
            {
                SwapState(CreateDirectState());
                return new EncoderHandle(this, entry);
            }
            catch
            {
                _encoderEntries.Remove(entry);
                throw;
            }
        }
    }

    internal CaptionRegistryDrain<Extension> ReplaceOwned(
        IEnumerable<CaptionCodecDescriptorRegistration> descriptors,
        IEnumerable<CaptionDecoderRegistration> decoders,
        IEnumerable<CaptionEncoderRegistration> encoders,
        IReadOnlyDictionary<CaptionFormatId, Extension> descriptorOwners,
        IReadOnlyDictionary<CaptionFormatId, Extension> decoderOwners,
        IReadOnlyDictionary<CaptionFormatId, Extension> encoderOwners)
    {
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            CaptionRegistryDrain<object> retired = SwapState(State.Create(
                descriptors,
                decoders,
                encoders,
                ToObjectOwners(descriptorOwners),
                ToObjectOwners(decoderOwners),
                ToObjectOwners(encoderOwners)));
            return new CaptionRegistryDrain<Extension>(
                retired.All.AsTask(),
                owner => retired.DrainOwnerAsync(owner));
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
        throw new KeyNotFoundException(
            $"No caption codec descriptor is registered for format '{format}'.");
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
        lock (_stateGate)
        {
            KeyValuePair<string, CaptionFormatId>? bestMatch = null;
            foreach (KeyValuePair<string, CaptionFormatId> candidate in _state.FormatsByExtension)
            {
                if (fileName.EndsWith(candidate.Key, StringComparison.OrdinalIgnoreCase)
                    && (bestMatch is null || candidate.Key.Length > bestMatch.Value.Key.Length))
                {
                    bestMatch = candidate;
                }
            }

            if (bestMatch is { } match)
                return _state.CodecsByFormat.TryGetValue(match.Value, out codec);
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

    public ValueTask DisposeAsync()
    {
        lock (_mutationGate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private CapabilityLease<ICaptionDecoder> AcquireDecoder(CaptionFormatId format)
    {
        lock (_stateGate)
        {
            State state = _state;
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
        lock (_stateGate)
        {
            State state = _state;
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

    private Task DisposeCoreAsync()
    {
        if (_disposed)
            return Task.CompletedTask;
        _disposed = true;
        _descriptorEntries.Clear();
        _decoderEntries.Clear();
        _encoderEntries.Clear();
        CaptionRegistryDrain<object> retired = SwapState(State.Create([], [], []));
        return Task.WhenAll(
            _retiredStates.Select(state => state.RetireAsync())
                .Append(retired.All.AsTask())
                .Distinct());
    }

    private State CreateDirectState()
    {
        DescriptorEntry[] descriptors = _descriptorEntries
            .GroupBy(entry => entry.Registration.Descriptor.Format)
            .Select(group => group.Last())
            .ToArray();
        DecoderEntry[] decoders = _decoderEntries
            .GroupBy(entry => entry.Registration.Format)
            .Select(group => group.Last())
            .ToArray();
        EncoderEntry[] encoders = _encoderEntries
            .GroupBy(entry => entry.Registration.Format)
            .Select(group => group.Last())
            .ToArray();
        return State.Create(
            descriptors.Select(entry => new CaptionCodecDescriptorRegistration(
                entry.Registration.Descriptor)),
            decoders.Select(entry => new CaptionDecoderRegistration(
                entry.Registration.Format,
                entry.Registration.Decoder)),
            encoders.Select(entry => new CaptionEncoderRegistration(
                entry.Registration.Format,
                entry.Registration.Encoder)),
            descriptors.ToDictionary(
                entry => entry.Registration.Descriptor.Format,
                entry => (object)entry),
            decoders.ToDictionary(
                entry => entry.Registration.Format,
                entry => (object)entry),
            encoders.ToDictionary(
                entry => entry.Registration.Format,
                entry => (object)entry));
    }

    private CaptionRegistryDrain<object> SwapState(State next)
    {
        State previous;
        lock (_stateGate)
        {
            previous = _state;
            _state = next;
        }

        try
        {
            if (!HaveSameMetadata(_codecs, next.Codecs))
                _codecs.Replace(next.Codecs);
        }
        catch
        {
            // Metadata observers cannot roll back a committed capability transition.
        }

        Task retired = previous.RetireAndReleasePayloadAsync();
        if (!retired.IsCompleted)
        {
            _retiredStates.Add(previous);
            _ = retired.ContinueWith(
                _ => RemoveRetiredState(previous),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return new CaptionRegistryDrain<object>(retired, previous.DrainOwnerAsync);
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

    private void RemoveRetiredState(State state)
    {
        lock (_mutationGate)
            _retiredStates.Remove(state);
    }

    private Task RemoveAsync(DescriptorEntry entry)
    {
        lock (_mutationGate)
        {
            if (!_descriptorEntries.Remove(entry))
                return _disposeTask ?? Task.CompletedTask;
            CaptionRegistryDrain<object> retired = SwapState(CreateDirectState());
            return DrainOwnerAcrossRetiredStates(entry, retired);
        }
    }

    private Task RemoveAsync(DecoderEntry entry)
    {
        lock (_mutationGate)
        {
            if (!_decoderEntries.Remove(entry))
                return _disposeTask ?? Task.CompletedTask;
            CaptionRegistryDrain<object> retired = SwapState(CreateDirectState());
            return DrainOwnerAcrossRetiredStates(entry, retired);
        }
    }

    private Task RemoveAsync(EncoderEntry entry)
    {
        lock (_mutationGate)
        {
            if (!_encoderEntries.Remove(entry))
                return _disposeTask ?? Task.CompletedTask;
            CaptionRegistryDrain<object> retired = SwapState(CreateDirectState());
            return DrainOwnerAcrossRetiredStates(entry, retired);
        }
    }

    private Task DrainOwnerAcrossRetiredStates(object owner, CaptionRegistryDrain<object> retired)
        => Task.WhenAll(
            _retiredStates.Select(state => state.DrainOwnerAsync(owner))
                .Append(retired.DrainOwnerAsync(owner))
                .Distinct());

    private static IReadOnlyDictionary<CaptionFormatId, object> ToObjectOwners(
        IReadOnlyDictionary<CaptionFormatId, Extension> owners)
        => owners.ToDictionary(pair => pair.Key, pair => (object)pair.Value);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ValidateRegistrationMode(
        CaptionFormatId format,
        CaptionCodecRegistrationMode mode,
        bool exists,
        string capabilityName)
    {
        if (mode == CaptionCodecRegistrationMode.Add && exists)
        {
            throw new ArgumentException(
                $"Caption format '{format}' already has a {capabilityName}. Use Replace explicitly.");
        }
        if (mode == CaptionCodecRegistrationMode.Replace && !exists)
        {
            throw new ArgumentException(
                $"Caption format '{format}' has no {capabilityName} to replace.");
        }
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

    private sealed class State : CaptionRegistryLeaseState<object>
    {
        private State(
            Dictionary<CaptionFormatId, CaptionCodecDescriptor> descriptors,
            Dictionary<CaptionFormatId, ICaptionDecoder> decoders,
            Dictionary<CaptionFormatId, ICaptionEncoder> encoders,
            Dictionary<string, CaptionFormatId> formatsByExtension,
            IReadOnlyDictionary<CaptionFormatId, object> descriptorOwners,
            IReadOnlyDictionary<CaptionFormatId, object> decoderOwners,
            IReadOnlyDictionary<CaptionFormatId, object> encoderOwners)
        {
            Descriptors = descriptors;
            Decoders = decoders;
            Encoders = encoders;
            FormatsByExtension = formatsByExtension;
            DescriptorOwners = descriptorOwners;
            DecoderOwners = decoderOwners;
            EncoderOwners = encoderOwners;
            CaptionCodecInfo[] codecs = descriptors.Values
                .Select(descriptor => new CaptionCodecInfo(
                    descriptor,
                    decoders.ContainsKey(descriptor.Format),
                    encoders.ContainsKey(descriptor.Format)))
                .OrderBy(codec => codec.Order)
                .ThenBy(codec => codec.Format.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Codecs = codecs;
            CodecsByFormat = codecs.ToDictionary(codec => codec.Format);
        }

        public Dictionary<CaptionFormatId, CaptionCodecDescriptor> Descriptors
        { get; private set; }

        public Dictionary<CaptionFormatId, ICaptionDecoder> Decoders { get; private set; }

        public Dictionary<CaptionFormatId, ICaptionEncoder> Encoders { get; private set; }

        public CaptionCodecInfo[] Codecs { get; private set; }

        public Dictionary<CaptionFormatId, CaptionCodecInfo> CodecsByFormat { get; private set; }

        public Dictionary<string, CaptionFormatId> FormatsByExtension { get; private set; }

        public IReadOnlyDictionary<CaptionFormatId, object> DescriptorOwners { get; private set; }

        public IReadOnlyDictionary<CaptionFormatId, object> DecoderOwners { get; private set; }

        public IReadOnlyDictionary<CaptionFormatId, object> EncoderOwners { get; private set; }

        public IReadOnlyCollection<object> GetDecoderOwners(CaptionFormatId format)
            => DecoderOwners.TryGetValue(format, out object? owner) ? [owner] : [];

        public IReadOnlyCollection<object> GetEncoderOwners(CaptionFormatId format)
            => EncoderOwners.TryGetValue(format, out object? owner) ? [owner] : [];

        public Task RetireAndReleasePayloadAsync()
        {
            Task drain = RetireAsync();
            Descriptors = [];
            Decoders = [];
            Encoders = [];
            Codecs = [];
            CodecsByFormat = [];
            FormatsByExtension = new Dictionary<string, CaptionFormatId>(
                StringComparer.OrdinalIgnoreCase);
            DescriptorOwners = new Dictionary<CaptionFormatId, object>();
            DecoderOwners = new Dictionary<CaptionFormatId, object>();
            EncoderOwners = new Dictionary<CaptionFormatId, object>();
            return drain;
        }

        public static State Create(
            IEnumerable<CaptionCodecDescriptorRegistration> descriptorRegistrations,
            IEnumerable<CaptionDecoderRegistration> decoderRegistrations,
            IEnumerable<CaptionEncoderRegistration> encoderRegistrations,
            IReadOnlyDictionary<CaptionFormatId, object>? descriptorOwners = null,
            IReadOnlyDictionary<CaptionFormatId, object>? decoderOwners = null,
            IReadOnlyDictionary<CaptionFormatId, object>? encoderOwners = null)
        {
            var descriptors = new Dictionary<CaptionFormatId, CaptionCodecDescriptor>();
            var decoders = new Dictionary<CaptionFormatId, ICaptionDecoder>();
            var encoders = new Dictionary<CaptionFormatId, ICaptionEncoder>();
            foreach (CaptionCodecDescriptorRegistration registration in descriptorRegistrations)
            {
                Apply(
                    registration.Descriptor.Format,
                    registration.Descriptor,
                    registration.Mode,
                    descriptors,
                    "descriptor");
            }
            foreach (CaptionDecoderRegistration registration in decoderRegistrations)
            {
                Apply(
                    registration.Format,
                    registration.Decoder,
                    registration.Mode,
                    decoders,
                    "decoder");
            }
            foreach (CaptionEncoderRegistration registration in encoderRegistrations)
            {
                Apply(
                    registration.Format,
                    registration.Encoder,
                    registration.Mode,
                    encoders,
                    "encoder");
            }

            var formatsByExtension = new Dictionary<string, CaptionFormatId>(
                StringComparer.OrdinalIgnoreCase);
            foreach (CaptionCodecDescriptor descriptor in descriptors.Values)
            {
                foreach (string extension in descriptor.FileExtensions)
                {
                    string normalized = NormalizeExtension(extension);
                    if (!formatsByExtension.TryAdd(normalized, descriptor.Format))
                    {
                        throw new ArgumentException(
                            $"File extension '{normalized}' is registered more than once.",
                            nameof(descriptorRegistrations));
                    }
                }
            }

            return new State(
                descriptors,
                decoders,
                encoders,
                formatsByExtension,
                descriptorOwners ?? new Dictionary<CaptionFormatId, object>(),
                decoderOwners ?? new Dictionary<CaptionFormatId, object>(),
                encoderOwners ?? new Dictionary<CaptionFormatId, object>());
        }

        private static void Apply<TCapability>(
            CaptionFormatId format,
            TCapability capability,
            CaptionCodecRegistrationMode mode,
            Dictionary<CaptionFormatId, TCapability> capabilities,
            string capabilityName)
        {
            bool exists = capabilities.ContainsKey(format);
            if (mode == CaptionCodecRegistrationMode.Add && exists)
            {
                throw new ArgumentException(
                    $"Caption format '{format}' already has a {capabilityName}. Use Replace explicitly.");
            }
            if (mode == CaptionCodecRegistrationMode.Replace && !exists)
            {
                throw new ArgumentException(
                    $"Caption format '{format}' has no {capabilityName} to replace.");
            }
            capabilities[format] = capability;
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

    private sealed class DescriptorEntry(CaptionCodecDescriptorRegistration registration)
    {
        public CaptionCodecDescriptorRegistration Registration { get; } = registration;
    }

    private sealed class DecoderEntry(CaptionDecoderRegistration registration)
    {
        public CaptionDecoderRegistration Registration { get; } = registration;
    }

    private sealed class EncoderEntry(CaptionEncoderRegistration registration)
    {
        public CaptionEncoderRegistration Registration { get; } = registration;
    }

    private abstract class Handle
    {
        private readonly Lazy<Task> _dispose;

        protected Handle(Func<Task> dispose)
        {
            _dispose = new Lazy<Task>(dispose, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        protected ValueTask DisposeCoreAsync() => new(_dispose.Value);
    }

    private sealed class DescriptorHandle(
        CaptionCodecRegistry registry,
        DescriptorEntry entry) : Handle(() => registry.RemoveAsync(entry)),
        ICaptionCodecDescriptorRegistration
    {
        public ValueTask DisposeAsync() => DisposeCoreAsync();
    }

    private sealed class DecoderHandle(
        CaptionCodecRegistry registry,
        DecoderEntry entry) : Handle(() => registry.RemoveAsync(entry)),
        ICaptionDecoderRegistration
    {
        public ValueTask DisposeAsync() => DisposeCoreAsync();
    }

    private sealed class EncoderHandle(
        CaptionCodecRegistry registry,
        EncoderEntry entry) : Handle(() => registry.RemoveAsync(entry)),
        ICaptionEncoderRegistration
    {
        public ValueTask DisposeAsync() => DisposeCoreAsync();
    }
}

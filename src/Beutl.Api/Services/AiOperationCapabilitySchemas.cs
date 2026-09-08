using System.Collections.Immutable;
using System.Collections.Specialized;
using Beutl.Extensibility;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

/// <summary>Identifies the supported model-capability shape for an AI operation.</summary>
public enum AiModelCapabilitySchema
{
    Generic,
    Image,
    Video,
}

public enum AiOperationCapabilitySchemaRegistrationMode
{
    Add,
    Replace,
}

/// <summary>Associates one open AI operation identifier with a model-capability schema.</summary>
public sealed class AiOperationCapabilitySchemaRegistration
{
    public AiOperationCapabilitySchemaRegistration(
        AiOperationId operation,
        AiModelCapabilitySchema schema,
        AiOperationCapabilitySchemaRegistrationMode mode =
            AiOperationCapabilitySchemaRegistrationMode.Add)
    {
        if (string.IsNullOrWhiteSpace(operation.Value))
            throw new ArgumentException("An AI operation identifier is required.", nameof(operation));
        if (!Enum.IsDefined(schema))
            throw new ArgumentOutOfRangeException(nameof(schema));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        Operation = operation;
        Schema = schema;
        Mode = mode;
    }

    public AiOperationId Operation { get; }

    public AiModelCapabilitySchema Schema { get; }

    public AiOperationCapabilitySchemaRegistrationMode Mode { get; }
}

/// <summary>
/// Declares capability schemas for custom AI operations without retaining executable package code.
/// </summary>
public abstract class AiOperationCapabilitySchemaExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<AiOperationCapabilitySchemaRegistration> Registrations { get; }
}

internal interface IAiOperationCapabilitySchemaProvider : IBeutlApiResource
{
    event EventHandler? Changed;

    AiOperationCapabilitySchemaSnapshot GetSnapshot();
}

internal readonly record struct AiOperationCapabilitySchemaSnapshot(
    long Revision,
    ImmutableDictionary<AiOperationId, AiModelCapabilitySchema> Schemas)
{
    public AiModelCapabilitySchema GetSchema(AiOperationId operation)
        => Schemas.GetValueOrDefault(operation, GetBuiltInSchema(operation));

    private static AiModelCapabilitySchema GetBuiltInSchema(AiOperationId operation)
    {
        if (operation == AiOperations.VideoGeneration)
            return AiModelCapabilitySchema.Video;
        if (operation == AiOperations.ImageGeneration
            || operation.Value.StartsWith("image.edit.", StringComparison.Ordinal))
        {
            return AiModelCapabilitySchema.Image;
        }

        return AiModelCapabilitySchema.Generic;
    }
}

internal sealed class AiOperationCapabilitySchemaRegistry :
    IAiOperationCapabilitySchemaProvider,
    IDisposable
{
    private static readonly ILogger s_logger =
        Log.CreateLogger<AiOperationCapabilitySchemaRegistry>();
    private readonly IExtensionRegistry _extensions;
    private readonly Action<string, Exception> _reportFailure;
    private readonly object _compositionGate = new();
    private readonly object _gate = new();
    private ImmutableDictionary<AiOperationId, AiModelCapabilitySchema> _schemas =
        ImmutableDictionary<AiOperationId, AiModelCapabilitySchema>.Empty;
    private long _revision;
    private bool _disposed;

    public AiOperationCapabilitySchemaRegistry(IExtensionRegistry extensions)
        : this(extensions, null)
    {
    }

    internal AiOperationCapabilitySchemaRegistry(
        IExtensionRegistry extensions,
        Action<string, Exception>? reportFailure)
    {
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        _reportFailure = reportFailure ?? LogFailure;
        extensions.SynchronizeMutation(() =>
        {
            lock (_compositionGate)
            {
                extensions.AllExtensions.CollectionChanged += OnExtensionsChanged;
                extensions.ExtensionsChanged += OnExtensionsCommitted;
                try
                {
                    RebuildCore();
                }
                catch
                {
                    extensions.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                    extensions.ExtensionsChanged -= OnExtensionsCommitted;
                    throw;
                }
            }
        });
    }

    public event EventHandler? Changed;

    public AiOperationCapabilitySchemaSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new AiOperationCapabilitySchemaSnapshot(_revision, _schemas);
        }
    }

    internal AiModelCapabilitySchema GetSchema(AiOperationId operation)
        => GetSnapshot().GetSchema(operation);

    public void Dispose()
    {
        _extensions.SynchronizeMutation(() =>
        {
            lock (_compositionGate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _extensions.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                _extensions.ExtensionsChanged -= OnExtensionsCommitted;
                lock (_gate)
                    _schemas = ImmutableDictionary<AiOperationId, AiModelCapabilitySchema>.Empty;
            }
        });
    }

    private void OnExtensionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            Rebuild();
    }

    private void OnExtensionsCommitted(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        _extensions.SynchronizeMutation(() =>
        {
            lock (_compositionGate)
            {
                if (!_disposed)
                    RebuildCore();
            }
        });
    }

    private void RebuildCore()
    {
        var readFailures = new List<(string ExtensionType, Exception Exception)>();
        List<SchemaCandidate> candidates = ReadCommittedCandidates(readFailures);

        var failures = new Dictionary<SchemaCandidate, Exception>(
            ReferenceEqualityComparer.Instance);
        Dictionary<AiOperationId, AiModelCapabilitySchema> accepted;
        while (true)
        {
            accepted = [];
            var newFailures = new Dictionary<SchemaCandidate, (Exception Exception, int Phase)>(
                ReferenceEqualityComparer.Instance);
            AiOperationCapabilitySchemaRegistrationMode[] phases =
            [
                AiOperationCapabilitySchemaRegistrationMode.Add,
                AiOperationCapabilitySchemaRegistrationMode.Replace,
            ];
            for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
            {
                AiOperationCapabilitySchemaRegistrationMode phase = phases[phaseIndex];
                foreach (SchemaCandidate candidate in candidates)
                {
                    if (failures.ContainsKey(candidate) || newFailures.ContainsKey(candidate))
                        continue;

                    try
                    {
                        var working = new Dictionary<AiOperationId, AiModelCapabilitySchema>(
                            accepted);
                        foreach (AiOperationCapabilitySchemaRegistration registration
                                 in candidate.Registrations.Where(item => item.Mode == phase))
                        {
                            Apply(registration, working);
                        }
                        accepted = working;
                    }
                    catch (Exception ex)
                    {
                        newFailures.Add(candidate, (ex, phaseIndex));
                    }
                }
            }

            if (newFailures.Count == 0)
                break;

            int latestPhase = newFailures.Values.Max(failure => failure.Phase);
            KeyValuePair<SchemaCandidate, (Exception Exception, int Phase)> rejected =
                newFailures.First(failure => failure.Value.Phase == latestPhase);
            failures.TryAdd(rejected.Key, rejected.Value.Exception);
        }

        ImmutableDictionary<AiOperationId, AiModelCapabilitySchema> next =
            accepted.ToImmutableDictionary();
        bool changed;
        lock (_gate)
        {
            if (_disposed)
                return;
            changed = !HaveSameSchemas(_schemas, next);
            if (changed)
            {
                _schemas = next;
                _revision++;
            }
        }

        if (changed)
            NotifyChanged();
        foreach ((string extensionType, Exception failure) in readFailures)
            ReportFailure(extensionType, failure);
        foreach ((SchemaCandidate candidate, Exception failure) in failures)
            ReportFailure(candidate.ExtensionType, failure);
    }

    private List<SchemaCandidate> ReadCommittedCandidates(
        List<(string ExtensionType, Exception Exception)> failures)
    {
        var result = new List<SchemaCandidate>();
        foreach (ExtensionDescriptor descriptor
                 in _extensions.GetDescriptors<AiOperationCapabilitySchemaExtension>())
        {
            if (!_extensions.TryAcquire(
                    descriptor.Id,
                    out IExtensionLease<AiOperationCapabilitySchemaExtension>? lease))
            {
                continue;
            }

            using (lease)
            {
                try
                {
                    IReadOnlyCollection<AiOperationCapabilitySchemaRegistration>? registrations =
                        lease.Extension.Registrations;
                    if (registrations is null
                        || registrations.Any(registration => registration is null))
                    {
                        throw new InvalidOperationException(
                            "An AI operation capability-schema extension returned invalid registrations.");
                    }

                    result.Add(new SchemaCandidate(
                        descriptor.TypeName,
                        registrations.Select(registration =>
                            new AiOperationCapabilitySchemaRegistration(
                                registration.Operation,
                                registration.Schema,
                                registration.Mode)).ToArray()));
                }
                catch (Exception ex)
                {
                    failures.Add((descriptor.TypeName, ex));
                }
            }
        }

        return result;
    }

    private static bool HaveSameSchemas(
        ImmutableDictionary<AiOperationId, AiModelCapabilitySchema> left,
        ImmutableDictionary<AiOperationId, AiModelCapabilitySchema> right)
        => left.Count == right.Count
           && left.All(pair => right.TryGetValue(pair.Key, out AiModelCapabilitySchema schema)
                               && schema == pair.Value);

    private static void Apply(
        AiOperationCapabilitySchemaRegistration registration,
        Dictionary<AiOperationId, AiModelCapabilitySchema> schemas)
    {
        bool exists = schemas.ContainsKey(registration.Operation)
            || GetBuiltInSchema(registration.Operation) != AiModelCapabilitySchema.Generic;
        if (registration.Mode == AiOperationCapabilitySchemaRegistrationMode.Add && exists)
        {
            throw new ArgumentException(
                $"A capability schema for '{registration.Operation}' is already registered.");
        }
        if (registration.Mode == AiOperationCapabilitySchemaRegistrationMode.Replace && !exists)
        {
            throw new ArgumentException(
                $"A capability schema for '{registration.Operation}' cannot be replaced because it is not registered.");
        }

        schemas[registration.Operation] = registration.Schema;
    }

    private static AiModelCapabilitySchema GetBuiltInSchema(AiOperationId operation)
    {
        if (operation == AiOperations.VideoGeneration)
            return AiModelCapabilitySchema.Video;
        if (operation == AiOperations.ImageGeneration
            || operation.Value.StartsWith("image.edit.", StringComparison.Ordinal))
        {
            return AiModelCapabilitySchema.Image;
        }

        return AiModelCapabilitySchema.Generic;
    }

    private void NotifyChanged()
    {
        if (Changed is not { } observers)
            return;

        foreach (EventHandler observer in observers.GetInvocationList())
        {
            try
            {
                observer(this, EventArgs.Empty);
            }
            catch
            {
            }
        }
    }

    private void ReportFailure(string extensionType, Exception exception)
    {
        try
        {
            _reportFailure(extensionType, exception);
        }
        catch
        {
            // Diagnostics cannot veto a committed extension mutation or interrupt removal.
        }
    }

    private static void LogFailure(string extensionType, Exception exception)
        => s_logger.LogWarning(
            exception,
            "Ignoring invalid AI capability-schema contribution from {ExtensionType}.",
            extensionType);

    private sealed class SchemaCandidate(
        string extensionType,
        AiOperationCapabilitySchemaRegistration[] registrations)
    {
        public string ExtensionType { get; } = extensionType;

        public AiOperationCapabilitySchemaRegistration[] Registrations { get; } = registrations;
    }
}

internal sealed class BuiltInAiOperationCapabilitySchemaProvider :
    IAiOperationCapabilitySchemaProvider
{
    public static BuiltInAiOperationCapabilitySchemaProvider Instance { get; } = new();

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public AiOperationCapabilitySchemaSnapshot GetSnapshot()
        => new(
            0,
            ImmutableDictionary<AiOperationId, AiModelCapabilitySchema>.Empty);
}

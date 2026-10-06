using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Sessions;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Reconciliation;

public sealed partial class Reconciler
{
    // Applies the full desired document to a throwaway clone of the current root, so newly inserted
    // subtrees exist and are findable by Id here — unlike the live root the diff runs against.
    private static CoreObject BuildValidationSandbox(
        IEditingSession session,
        JsonObject currentDocument,
        JsonObject desiredDocument)
    {
        CoreObject sandboxRoot;
        Dictionary<IncidentIdentity, int> existingIncidents;
        using (var baseline = DeserializationIncidents.BeginCapture())
        {
            sandboxRoot = CloneCurrentRoot(session, currentDocument);
            existingIncidents = baseline.Incidents.GroupBy(CreateIncidentIdentity)
                .ToDictionary(group => group.Key, group => group.Count());
        }
        JsonObject payload = (JsonObject)desiredDocument.DeepClone();
        payload.Remove(SchemaVersion.PropertyName);

        using var appliedIncidents = DeserializationIncidents.BeginCapture();
        try
        {
            new DeclarativeDocumentApplier().Apply(sandboxRoot, payload);
        }
        catch (ReconcileException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Desired document could not be applied in the validation sandbox: {ex.Message}",
                null,
                "Call get_schema for the concrete type, then retry apply_edit with the serialized property shapes returned by the schema."));
        }

        ValidateNoNewFallbackObjects(session, sandboxRoot);
        ValidateNoNewIncidents(appliedIncidents.Incidents, existingIncidents);
        // Converters can create fallbacks inside plugin wrappers that graph traversal cannot see.
        // Compare complete snapshots, including unchanged animations skipped by the applier, so an
        // existing fallback cannot lend its allowance to a second, newly inserted occurrence.
        using var incidents = DeserializationIncidents.BeginCapture();
        _ = CloneCurrentRoot(session, session.Documents.Read(sandboxRoot));
        ValidateNoNewIncidents(incidents.Incidents, existingIncidents);

        return sandboxRoot;
    }

    private static void ValidateNoNewIncidents(
        IReadOnlyList<DeserializationIncidents.DeserializationIncident> incidents,
        Dictionary<IncidentIdentity, int> baseline)
    {
        var existingIncidents = new Dictionary<IncidentIdentity, int>(baseline);
        foreach (var incident in incidents)
        {
            IncidentIdentity identity = CreateIncidentIdentity(incident);
            if (existingIncidents.TryGetValue(identity, out int remaining) && remaining > 0)
            {
                existingIncidents[identity] = remaining - 1;
                continue;
            }

            var occurrence = new FallbackOccurrence("$", identity.TypeName, identity.Reason,
                incident.Fallback?.ErrorMessage ?? incident.Message);
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Desired document produced a fallback object for {identity.TypeName ?? "unknown serialized type"}.",
                occurrence.Path,
                CreateFallbackHint(occurrence)));
        }
    }

    private static IncidentIdentity CreateIncidentIdentity(DeserializationIncidents.DeserializationIncident incident)
    {
        if (incident.Fallback is { } fallback)
        {
            fallback.TryGetTypeName(out string? typeName);
            return new IncidentIdentity(typeName, fallback.Reason.ToString(),
                fallback.Json?.ToJsonString());
        }

        return new IncidentIdentity(incident.TypeName,
            incident.Reason?.ToString() ?? nameof(FallbackReason.DeserializationFailed), null);
    }

    private readonly record struct IncidentIdentity(string? TypeName, string Reason, string? Json);

    private static void ValidateNoNewFallbackObjects(IEditingSession session, CoreObject sandboxRoot)
    {
        Dictionary<FallbackIdentity, int> existingFallbacks = CollectFallbackIdentities(session.Root);
        if (FindFirstNewFallback(sandboxRoot, "$", existingFallbacks) is { } occurrence)
        {
            string typeDetail = string.IsNullOrWhiteSpace(occurrence.FallbackTypeName)
                ? "unknown serialized type"
                : occurrence.FallbackTypeName;
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Desired document produced a fallback object at '{occurrence.Path}' for {typeDetail}.",
                occurrence.Path,
                CreateFallbackHint(occurrence)));
        }
    }

    private static CoreObject CloneCurrentRoot(IEditingSession session, JsonObject currentDocument)
    {
        JsonObject snapshot = (JsonObject)currentDocument.DeepClone();
        snapshot.Remove(SchemaVersion.PropertyName);
        return DocumentAdapter.DeserializeDetached(snapshot, session.Root.GetType(), session.Root.Uri);
    }

    private static Dictionary<FallbackIdentity, int> CollectFallbackIdentities(CoreObject root)
    {
        var identities = new Dictionary<FallbackIdentity, int>();
        SerializedGraphTraversal.Visit(root, "$", (node, _) =>
        {
            if (node is IFallback fallback)
            {
                FallbackIdentity identity = CreateFallbackIdentity(fallback);
                identities[identity] = identities.GetValueOrDefault(identity) + 1;
            }

            return false;
        });

        return identities;
    }

    private static FallbackOccurrence? FindFirstNewFallback(
        CoreObject root,
        string path,
        Dictionary<FallbackIdentity, int> existingFallbacks)
    {
        FallbackOccurrence? result = null;
        SerializedGraphTraversal.Visit(root, path, (node, nodePath) =>
        {
            if (node is not IFallback fallback)
            {
                return false;
            }

            FallbackIdentity identity = CreateFallbackIdentity(fallback);
            if (existingFallbacks.TryGetValue(identity, out int remaining) && remaining > 0)
            {
                existingFallbacks[identity] = remaining - 1;
                return false;
            }

            fallback.TryGetTypeName(out string? fallbackTypeName);
            result = new FallbackOccurrence(
                nodePath,
                fallbackTypeName,
                fallback.Reason.ToString(),
                fallback.ErrorMessage);
            return true;
        });
        return result;
    }

    private static FallbackIdentity CreateFallbackIdentity(IFallback fallback)
    {
        return fallback is CoreObject { Id: var id } && id != Guid.Empty
            ? new FallbackIdentity(id, null)
            : new FallbackIdentity(
                null,
                $"{fallback.GetType().AssemblyQualifiedName}|{fallback.Json?.ToJsonString()}");
    }

    private static string CreateFallbackHint(FallbackOccurrence occurrence)
    {
        string baseHint = "Call get_schema for the exact drawable/effect/brush/transform/pen/animation type and use the discriminator and PascalCase property names it returns. Timeline Elements use '$type': '[Beutl.ProjectSystem]:Element'. Objects require concrete EngineObject discriminators from get_schema; typed property values such as Pen, Brush, Transform, Effect, and Animation also require concrete schema-returned object shapes.";
        if (!string.IsNullOrWhiteSpace(occurrence.Message))
        {
            return $"{baseHint} Deserialization error: {occurrence.Message}";
        }

        return $"{baseHint} Fallback reason: {occurrence.Reason}.";
    }

    private readonly record struct FallbackIdentity(Guid? Id, string? Signature);

    private sealed record FallbackOccurrence(
        string Path,
        string? FallbackTypeName,
        string Reason,
        string? Message);
}

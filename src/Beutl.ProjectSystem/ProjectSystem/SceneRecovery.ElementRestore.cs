using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.ProjectSystem;

internal sealed partial class SceneRecovery
{
    public Element RestoreElement(Uri uri)
    {
        using DeserializationIncidents.Capture incidentCapture = DeserializationIncidents.BeginCapture();
        using var storageCapture = new ReferencedStorageCapture();
        try
        {
            Element element = CoreSerializer.RestoreFromUri<Element>(uri);
            MarkRecoveredFallbacks(element, uri, incidentCapture, storageCapture);
            return element;
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex)
                                   && !ExceptionHelpers.ContainsNonRecoverableFileSystemFailure(ex))
        {
            return CreateUnreadableElement(uri, ex, storageCapture);
        }
    }

    private void MarkRecoveredFallbacks(
        Element element,
        Uri uri,
        DeserializationIncidents.Capture incidentCapture,
        ReferencedStorageCapture storageCapture)
    {
        IFallback[] fallbacks = EnumerateSerializedGraphFallbacks(element).ToArray();
        int incidentCount = incidentCapture.Count;

        if (fallbacks.Length > 0 || incidentCount > 0)
        {
            var traversedFallbacks = new HashSet<IFallback>(
                fallbacks,
                ReferenceEqualityComparer.Instance);
            DeserializationIncidents.DeserializationIncident[] untraversedIncidents
                = incidentCapture.Incidents
                    .Where(incident => incident.Fallback is null
                                       || !traversedFallbacks.Contains(incident.Fallback))
                    .ToArray();
            JsonObject[] untraversedFallbacks = untraversedIncidents
                .Where(static incident => incident.Fallback?.Json != null)
                .Select(static incident => incident.Fallback!.Json!.DeepClone().AsObject())
                .ToArray();
            SuppressedRecoveryIncident[] recoveryIncidents = untraversedIncidents
                .Select(CreateSuppressedRecoveryIncident)
                .ToArray();
            foreach (IFallback fallback in fallbacks)
            {
                if (fallback is CoreObject fallbackObject)
                {
                    if (RecoveredElementJsonScanner.TryGetSerializedId(fallback.Json, out Guid serializedId))
                    {
                        fallbackObject.Id = serializedId;
                    }
                    else
                    {
                        _idlessRecoveredDescendants.GetValue(
                            fallbackObject,
                            static _ => new IdlessRecoveredDescendant());
                    }
                }

                EnsureFallbackProjection(fallback);
            }

            MarkRecoveredElement(
                element,
                File.ReadAllBytes(uri.LocalPath),
                uri,
                recoveryIncidents.Length > 0,
                untraversedFallbacks,
                recoveryIncidents,
                storageCapture.Sources);
        }
    }

    private Element CreateUnreadableElement(Uri uri, Exception ex, ReferencedStorageCapture storageCapture)
    {
        // Raw bytes, not text: the sidecar must survive rehoming byte-identically even when it
        // holds a BOM, another encoding, or undecodable bytes. The lossy decode is only scanned
        // for top-level recovery metadata.
        byte[] rawBytes = File.ReadAllBytes(uri.LocalPath);
        string rawText = RecoveredElementJsonScanner.DecodeRecoveryMetadata(rawBytes);
        byte[] metadataBytes = Encoding.UTF8.GetBytes(rawText);
        JsonObject? root = RecoveredElementJsonScanner.TryParseTopLevelObject(rawText);
        var element = new Element
        {
            Id = ResolveRecoveredElementId(metadataBytes, rawText, root, uri),
            Name = Path.GetFileNameWithoutExtension(uri.LocalPath),
            Uri = uri,
            IsEnabled = false,
        };
        string? topLevelTypeName = RecoveredElementJsonScanner.TryGetTopLevelTypeName(metadataBytes, rawText, root);
        FallbackReason fallbackReason = topLevelTypeName is not null
                                        && TypeFormat.ToType(topLevelTypeName) is null
            ? FallbackReason.TypeNotFound
            : FallbackReason.DeserializationFailed;
        var fallback = new FallbackEngineObject
        {
            Name = "Unreadable element data",
            Reason = fallbackReason,
            ErrorMessage = fallbackReason == FallbackReason.DeserializationFailed
                ? $"{ex.GetType().Name}: {ex.Message}"
                : null,
        };
        fallback.Json = CreateFallbackProjection(fallback, topLevelTypeName);
        element.AddObject(fallback);
        _idlessRecoveredDescendants.GetValue(
            fallback,
            static _ => new IdlessRecoveredDescendant());
        MarkRecoveredElement(element, rawBytes, uri, referencedSources: storageCapture.Sources);
        return element;
    }

    private sealed class IdlessRecoveredDescendant;

    private void MarkRecoveredElement(
        Element element,
        byte[] rawBytes,
        Uri uri,
        bool hasNonFallbackIncidents = false,
        JsonObject[]? untraversedFallbacks = null,
        SuppressedRecoveryIncident[]? recoveryIncidents = null,
        IReadOnlyList<Uri>? referencedSources = null)
    {
        string sourceRootPath = Path.GetDirectoryName(Uri?.LocalPath ?? uri.LocalPath)
                                ?? throw new JsonException("Recovered element has no source directory.");
        element.SuppressedStorageSource = new SuppressedStorageSource(
            rawBytes,
            uri,
            hasNonFallbackIncidents,
            untraversedFallbacks,
            CollectReferencedStorageSources(element, uri, sourceRootPath, referencedSources ?? []),
            sourceRootPath,
            recoveryIncidents);
    }

    private static SuppressedRecoveryIncident CreateSuppressedRecoveryIncident(
        DeserializationIncidents.DeserializationIncident incident)
    {
        if (incident.Fallback is { } fallback)
        {
            fallback.TryGetTypeName(out string? typeName);
            return new SuppressedRecoveryIncident(
                fallback.Reason.ToString(),
                typeName,
                fallback.ErrorMessage);
        }

        return new SuppressedRecoveryIncident(
            incident.Reason?.ToString() ?? nameof(FallbackReason.DeserializationFailed),
            incident.TypeName,
            incident.Message);
    }

    private static SuppressedReferencedStorageSource[]? CollectReferencedStorageSources(
        Element element,
        Uri elementUri,
        string sourceRootPath,
        IReadOnlyList<Uri> referencedSources)
    {
        string sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRootPath));
        string resolvedSourceRoot = Path.TrimEndingDirectorySeparator(
            PathBoundary.ResolveDeepestExistingTarget(sourceRoot));
        string elementPath = Path.GetFullPath(elementUri.LocalPath);
        string resolvedElementPath = PathBoundary.ResolveDeepestExistingTarget(elementPath);
        if (!PathBoundary.IsPathInsideRoot(resolvedSourceRoot, resolvedElementPath))
        {
            return null;
        }

        string elementDirectory = Path.GetDirectoryName(elementPath)
                                  ?? throw new JsonException("Recovered element has no source directory.");
        // Save As must retain every serialized path, including distinct symlink aliases.
        // Do not fold case: differently cased sidecars may be distinct files.
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SuppressedReferencedStorageSource>();
        foreach (string sourcePath in EnumerateSerializedGraphObjects(element)
            .OfType<CoreObject>()
            .Select(static coreObject => coreObject.Uri)
            .Concat(referencedSources)
            .Where(static uri => uri is { IsFile: true })
            .Select(static uri => Path.GetFullPath(uri!.LocalPath)))
        {
            string resolvedSourcePath = PathBoundary.ResolveDeepestExistingTarget(sourcePath);
            if (string.Equals(
                    resolvedSourcePath,
                    resolvedElementPath,
                    StringComparison.Ordinal)
                || !PathBoundary.IsPathInsideRoot(resolvedSourceRoot, resolvedSourcePath)
                || !File.Exists(sourcePath)
                || !seenPaths.Add(sourcePath))
            {
                continue;
            }

            result.Add(new SuppressedReferencedStorageSource(
                File.ReadAllBytes(sourcePath),
                Path.GetRelativePath(sourceRoot, sourcePath),
                Path.GetRelativePath(elementDirectory, sourcePath)));
        }

        return result.Count > 0 ? result.ToArray() : null;
    }

    internal static SuppressedStorageSource? TryResumeElementPersistence(Element element)
    {
        if (element.SuppressedStorageSource is not { } source
            || EnumerateSerializedGraphFallbacks(element).Any()
            || HasUnresolvedSerializedRecoveryBlocker(element, source)
            || EnumerateSerializedGraphObjects(element).OfType<KeyFrame>().Any(static keyFrame => keyFrame.HasLossyEasing))
        {
            return null;
        }

        element.SuppressedStorageSource = null;
        return source;
    }

    private static bool HasUnresolvedSerializedRecoveryBlocker(
        Element element,
        SuppressedStorageSource source)
    {
        using var capture = new LossyEasingSerializationCapture();
        using var objects = new SerializedObjectCapture();
        CoreSerializer.SerializeToJsonObject(
            element,
            new CoreSerializerOptions
            {
                BaseUri = element.Uri,
                Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.EmbedReferencedObjects,
            });
        if (capture.HasLossyEasing || objects.HasFallback) return true;
        if (source.UntraversedFallbacks is not { Length: > 0 } snapshots) return false;
        // Keep the original representation for fallback snapshot matching; embedding adds URI metadata.
        JsonObject current = CoreSerializer.SerializeToJsonObject(
            element, new CoreSerializerOptions { BaseUri = element.Uri });
        return snapshots.Any(snapshot => ContainsEquivalentJsonNode(current, snapshot));
    }

    private static bool ContainsEquivalentJsonNode(JsonNode? current, JsonNode snapshot)
    {
        if (JsonNode.DeepEquals(current, snapshot))
        {
            return true;
        }

        return current switch
        {
            JsonObject obj => obj.Any(item =>
                item.Value != null && ContainsEquivalentJsonNode(item.Value, snapshot)),
            JsonArray array => array.Any(item =>
                item != null && ContainsEquivalentJsonNode(item, snapshot)),
            _ => false,
        };
    }

    private static void EnsureFallbackProjection(IFallback fallback)
    {
        if (fallback is not CoreObject coreObject)
        {
            return;
        }

        JsonObject json = fallback.Json ?? new JsonObject();
        if (!json.ContainsKey("$type") && !json.ContainsKey("@type"))
        {
            json.WriteDiscriminator(coreObject.GetType());
        }

        json[nameof(CoreObject.Id)] = coreObject.Id.ToString();
        fallback.Json = json;
    }

    private static JsonObject CreateFallbackProjection(FallbackEngineObject fallback, string? typeName = null)
    {
        var json = new JsonObject
        {
            [nameof(CoreObject.Id)] = fallback.Id.ToString(),
            [nameof(CoreObject.Name)] = fallback.Name,
        };
        if (typeName is not null)
        {
            json["$type"] = typeName;
        }
        else
        {
            json.WriteDiscriminator(typeof(FallbackEngineObject));
        }

        return json;
    }

    private Guid ResolveRecoveredElementId(
        ReadOnlySpan<byte> rawBytes,
        string rawText,
        JsonObject? root,
        Uri uri)
    {
        if (RecoveredElementJsonScanner.TryGetTopLevelId(rawBytes, rawText, root, out Guid id))
        {
            return id;
        }

        string sceneDirectory = Path.GetDirectoryName(Uri!.LocalPath)!;
        string relativePath = GetSceneRelativePath(sceneDirectory, uri);
        return RecoveredIdentity.DeriveElementId(relativePath);
    }
}

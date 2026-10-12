using System.Runtime.CompilerServices;
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
                                   && !ExceptionHelpers.ContainsNonRecoverableFileSystemFailure(ex)
                                   && ExceptionHelpers.Find<UnsupportedFileVersionException>(ex) is null)
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
        IReadOnlyList<DeserializationIncidents.DeserializationIncident> incidents = incidentCapture.Incidents;
        if (fallbacks.Length == 0 && incidents.Count == 0)
        {
            return;
        }

        if (!KeepsOriginalFile(element, incidents))
        {
            // Saving writes every fallback's JSON back, so the element saves normally and stays editable.
            foreach (IFallback fallback in fallbacks)
            {
                GiveIdlessFallbackItsRuntimeId(fallback);
            }

            return;
        }

        var traversedFallbacks = new HashSet<IFallback>(
            fallbacks,
            ReferenceEqualityComparer.Instance);
        SuppressedRecoveryIncident[] recoveryIncidents = incidents
            .Where(incident => incident.Fallback is null
                               || !traversedFallbacks.Contains(incident.Fallback))
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
            recoveryIncidents,
            storageCapture.Sources);
    }

    // Whether saving the element would lose part of what it was read with, so that recovery must keep its
    // original file: content the Beutl 1.x migration could not convert, a load incident that left no fallback
    // (such as a keyframe easing that could not be read), or a fallback that saving the element does not
    // write. A fallback that saving writes is no reason: it writes the JSON it was read from back, so the
    // object whose type could not be loaded survives the save as it was.
    private static bool KeepsOriginalFile(
        Element element,
        IReadOnlyList<DeserializationIncidents.DeserializationIncident> incidents)
    {
        if (element.Objects.Any(ElementMigration.IsUnmigratedContent)
            || incidents.Any(static incident => incident.Fallback is null))
        {
            return true;
        }

        if (incidents.Count == 0)
        {
            return false;
        }

        using var serialized = new SerializedObjectCapture();
        CoreSerializer.SerializeToJsonObject(
            element,
            new CoreSerializerOptions
            {
                BaseUri = element.Uri,
                Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.EmbedReferencedObjects,
            });
        return incidents.Any(incident => !serialized.Contains(incident.Fallback!));
    }

    // A fallback whose JSON has no Id gets the one it has at runtime written into its JSON, so that it keeps one
    // identity across saves and an agent edit can match it. Nothing else in its JSON changes.
    private static void GiveIdlessFallbackItsRuntimeId(IFallback fallback)
    {
        if (fallback is CoreObject coreObject
            && fallback.Json is { } json
            && !json.ContainsKey(nameof(CoreObject.Id)))
        {
            json[nameof(CoreObject.Id)] = coreObject.Id.ToString();
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
        s_unreadableElementData.AddOrUpdate(fallback, s_unreadableElementDataMarker);
        element.AddObject(fallback);
        _idlessRecoveredDescendants.GetValue(
            fallback,
            static _ => new IdlessRecoveredDescendant());
        MarkRecoveredElement(element, rawBytes, uri, referencedSources: storageCapture.Sources);
        return element;
    }

    private sealed class IdlessRecoveredDescendant;

    // The objects that stand in for an element file that could not be read. Unlike other fallbacks, they hold
    // none of the file's content, so removing one is the repair that accepts losing that content.
    private static readonly ConditionalWeakTable<FallbackEngineObject, object> s_unreadableElementData = new();
    private static readonly object s_unreadableElementDataMarker = new();

    private void MarkRecoveredElement(
        Element element,
        byte[] rawBytes,
        Uri uri,
        bool hasNonFallbackIncidents = false,
        SuppressedRecoveryIncident[]? recoveryIncidents = null,
        IReadOnlyList<Uri>? referencedSources = null)
    {
        string sourceRootPath = Path.GetDirectoryName(Uri?.LocalPath ?? uri.LocalPath)
                                ?? throw new JsonException("Recovered element has no source directory.");
        element.SuppressedStorageSource = new SuppressedStorageSource(
            rawBytes,
            uri,
            hasNonFallbackIncidents,
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
            || HoldsUnresolvedRecoveryBlocker(element))
        {
            return null;
        }

        element.SuppressedStorageSource = null;
        return source;
    }

    // Whether a recovery-protected element still holds something that stops
    // TryResumeElementPersistence. False for an element recovery does not protect.
    internal static bool HasUnresolvedRecoveryBlocker(Element element)
    {
        return element.SuppressedStorageSource is not null
               && HoldsUnresolvedRecoveryBlocker(element);
    }

    // Whether content (an element, one of its objects, or a value or animation inside one) holds
    // what recovery keeps the element's original file for: content the Beutl 1.x migration could not
    // convert, the stand-in for an element file that could not be read, or a keyframe whose easing could
    // not be read. A fallback for an object whose type could not be loaded is not one: saving writes its
    // JSON back. Content nested where the serialized graph does not reach (for example inside a record) is
    // not found.
    internal static bool HoldsRecoveryBlocker(object? content)
    {
        return SerializedGraphTraversal.Visit(
            content,
            "$",
            static (value, _) => value is KeyFrame { HasLossyEasing: true }
                                 || (value is EngineObject obj && IsRecoveryBlockerObject(obj)));
    }

    private static bool IsRecoveryBlockerObject(EngineObject obj)
    {
        return ElementMigration.IsUnmigratedContent(obj)
               || (obj is FallbackEngineObject fallback && s_unreadableElementData.TryGetValue(fallback, out _));
    }

    private static bool HoldsUnresolvedRecoveryBlocker(Element element)
    {
        return HoldsRecoveryBlocker(element)
               || HasUnresolvedSerializedRecoveryBlocker(element);
    }

    // Finds the easings that could not be read where the serialized graph does not reach.
    private static bool HasUnresolvedSerializedRecoveryBlocker(Element element)
    {
        using var capture = new LossyEasingSerializationCapture();
        CoreSerializer.SerializeToJsonObject(
            element,
            new CoreSerializerOptions
            {
                BaseUri = element.Uri,
                Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.EmbedReferencedObjects,
            });
        return capture.HasLossyEasing;
    }

    private static void EnsureFallbackProjection(IFallback fallback)
    {
        if (fallback is not CoreObject coreObject)
        {
            return;
        }

        JsonObject json = fallback.Json ?? new JsonObject();
        if (!json.ContainsKey("$type"))
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

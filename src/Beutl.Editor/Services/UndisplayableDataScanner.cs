using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor.Services;

internal enum UndisplayableDataKind
{
    TypeNotFound,
    DeserializationFailed,
    UnmigratedLegacyContent,
    UnknownMembers,
    UnknownAnimations,
    UnknownExpressions,
    LossyEasing,
    DroppedValue,
}

internal sealed record UndisplayableDataEntry
{
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    public Project? Project { get; init; }
    public Scene? Scene { get; init; }
    public Element? Element { get; init; }
    public EngineObject? EngineObject { get; init; }
    public CoreObject? TargetObject { get; init; }
    public CoreObject? PropertyOwner { get; init; }
    public string? PropertyName { get; init; }
    public ElementEdge? TransitionEdge { get; init; }
    public GraphModel? RootGraph { get; init; }
    public GraphModel? ContainingGraph { get; init; }
    public GraphNode? GraphNode { get; init; }
    public IKeyFrameAnimation? Animation { get; init; }
    public KeyFrame? KeyFrame { get; init; }
    public MediaTime? KeyTime { get; init; }
    public TimelineLayer? Layer { get; init; }
    public SceneMarker? Marker { get; init; }
    public required string Breadcrumb { get; init; }
    public required UndisplayableDataKind Kind { get; init; }
    public string? TypeName { get; init; }
    public IReadOnlyList<string> UnknownKeys { get; init; } = [];
    public string? ErrorMessage { get; init; }
    public FallbackReason? Reason { get; init; }
    public string? SourceFilePath { get; init; }
    public bool IsRecoveryProtected { get; init; }
    public bool IsUnreadableElement { get; init; }
    public bool IsLegacyNodeTree { get; init; }
    public JsonNode? Json { get; init; }
    public byte[]? OriginalFile { get; init; }

    public bool HasPreview => Json is not null || OriginalFile is not null || Kind == UndisplayableDataKind.LossyEasing;

    public static string GetKindLabel(UndisplayableDataKind kind, bool unreadable = false, bool? legacyNodeTree = null)
        => kind switch
        {
            UndisplayableDataKind.TypeNotFound => UndisplayableDataStrings.TypeNotFound,
            UndisplayableDataKind.DeserializationFailed => unreadable
                ? UndisplayableDataStrings.UnreadableElement : UndisplayableDataStrings.DeserializationFailed,
            UndisplayableDataKind.UnmigratedLegacyContent => legacyNodeTree switch
            {
                true => UndisplayableDataStrings.LegacyNodeTree,
                false => UndisplayableDataStrings.LegacyOperator,
                null => UndisplayableDataStrings.LegacyContent,
            },
            UndisplayableDataKind.UnknownMembers => UndisplayableDataStrings.UnknownMembers,
            UndisplayableDataKind.UnknownAnimations => UndisplayableDataStrings.UnknownAnimations,
            UndisplayableDataKind.UnknownExpressions => UndisplayableDataStrings.UnknownExpressions,
            UndisplayableDataKind.LossyEasing => UndisplayableDataStrings.LossyEasing,
            _ => UndisplayableDataStrings.DroppedValue,
        };

    public string GetPreview()
    {
        if (OriginalFile is { } bytes)
        {
            string text = RecoveredElementJsonScanner.DecodeRecoveryMetadata(bytes);
            return RecoveredElementJsonScanner.TryParseTopLevelObject(text)?.ToJsonString(s_indented) ?? text;
        }

        return Json?.ToJsonString(s_indented) ?? "null";
    }
}

internal static class UndisplayableDataScanner
{
    public static IReadOnlyList<UndisplayableDataEntry> Scan(Project project, Scene? currentScene = null)
    {
        var entries = new List<UndisplayableDataEntry>();
        var scenes = project.Items.OfType<Scene>().ToArray();
        if (currentScene is not null && scenes.Contains(currentScene))
            ScanScene(currentScene, project, entries);

        foreach (Scene scene in scenes)
        {
            if (!ReferenceEquals(scene, currentScene))
                ScanScene(scene, project, entries);
        }

        foreach (ProjectItem item in project.Items)
        {
            if (item is not Scene)
                VisitObjectGraph(item, project, scene: null, element: null, (visit, issue) => entries.Add(CreateEntry(visit, issue, project, scene: null)));
        }

        ReportDirectObjectIssues(project, project, scene: null, entries);
        return entries;
    }

    public static IReadOnlyList<UndisplayableDataEntry> Scan(Scene scene)
    {
        var entries = new List<UndisplayableDataEntry>();
        ScanScene(scene, scene.FindHierarchicalParent<Project>(), entries);
        return entries;
    }

    // The timeline only needs counts. It does not build breadcrumbs, detail rows or JSON previews.
    public static IReadOnlyDictionary<UndisplayableDataKind, int> CountIssues(Element element)
    {
        Dictionary<UndisplayableDataKind, int>? counts = null;
        VisitObjectGraph(element, project: null, element.FindHierarchicalParent<Scene>(), element, (_, issue) =>
        {
            counts ??= new();
            counts[issue.Kind] = counts.GetValueOrDefault(issue.Kind) + 1;
        });
        return counts is not null ? counts : System.Collections.ObjectModel.ReadOnlyDictionary<UndisplayableDataKind, int>.Empty;
    }

    private static void ScanScene(Scene scene, Project? project, List<UndisplayableDataEntry> entries)
    {
        ReportDirectObjectIssues(scene, project, scene, entries);
        foreach (TimelineLayer layer in scene.Layers)
            VisitObjectGraph(layer, project, scene, element: null, (visit, issue) => entries.Add(CreateEntry(visit, issue, project, scene)));

        foreach (SceneMarker marker in scene.Markers)
            VisitObjectGraph(marker, project, scene, element: null, (visit, issue) => entries.Add(CreateEntry(visit, issue, project, scene)));

        foreach (Element element in scene.Children)
            VisitObjectGraph(element, project, scene, element, (visit, issue) => entries.Add(CreateEntry(visit, issue, project, scene)));
    }

    private static void ReportDirectObjectIssues(CoreObject obj, Project? project, Scene? scene, List<UndisplayableDataEntry> entries)
    {
        var visit = new CoreSerializationVisit(obj, null, null, null, null);
        if (obj is IFallback fallback)
        {
            entries.Add(CreateEntry(visit, FallbackIssue(fallback), project, scene));
            return;
        }

        if (obj.UnknownMembers is { Count: > 0 } members)
            entries.Add(CreateEntry(visit, new(UndisplayableDataKind.UnknownMembers, Json: members), project, scene));
    }

    private static void VisitObjectGraph(
        CoreObject root,
        Project? project,
        Scene? scene,
        Element? element,
        Action<CoreSerializationVisit, Issue> report)
    {
        HashSet<object>? reported = null;
        Dictionary<CoreObject, CoreSerializationVisit>? recoveryLocations = element is { IsRecoveryProtected: true }
            ? new(ReferenceEqualityComparer.Instance)
            : null;
        CoreSerializer.Visit(root, visit =>
        {
            if (CrossedOwnershipBoundary(visit, root, scene, element))
                return false;

            if (recoveryLocations is not null && visit.Value is CoreObject core)
                recoveryLocations.TryAdd(core, visit);

            if (visit.Value is IFallback fallback)
            {
                if (FirstReport(fallback)) report(visit, FallbackIssue(fallback));
            }
            else if (visit.Value is CoreObject obj)
            {
                JsonObject? members = obj.UnknownMembers;
                JsonObject? animations = (obj as EngineObject)?.UnknownAnimations;
                JsonObject? expressions = (obj as EngineObject)?.UnknownExpressions;
                bool lossy = obj is KeyFrame { HasLossyEasing: true };
                if ((members is { Count: > 0 } || animations is { Count: > 0 } || expressions is { Count: > 0 } || lossy)
                    && FirstReport(obj))
                {
                    if (members is { Count: > 0 }) report(visit, new(UndisplayableDataKind.UnknownMembers, Json: members));
                    if (animations is { Count: > 0 }) report(visit, new(UndisplayableDataKind.UnknownAnimations, Json: animations));
                    if (expressions is { Count: > 0 }) report(visit, new(UndisplayableDataKind.UnknownExpressions, Json: expressions));
                    if (lossy) report(visit, EasingIssue((KeyFrame)obj));
                }
            }

            return false;
        });

        if (element is { IsRecoveryProtected: true } && recoveryLocations is not null)
        {
            ReportRecovery(element, recoveryLocations, report);
        }

        bool FirstReport(object value) => (reported ??= new(ReferenceEqualityComparer.Instance)).Add(value);
    }

    // Reference properties (such as SceneDrawable.ReferencedScene or BrushPresenter.Target) serialize their
    // target object directly, so CoreSerializer.Visit walks across ownership boundaries. Skip targets owned
    // by another element or scene so each issue is reported only under its real owner.
    private static bool CrossedOwnershipBoundary(CoreSerializationVisit visit, CoreObject root, Scene? scene, Element? element)
    {
        for (CoreSerializationVisit? item = visit; item is not null && !ReferenceEquals(item.Value, root); item = item.Parent)
        {
            if (item.Value is Scene or Element or Project)
                return true;
        }

        if (visit.Value is IHierarchical hierarchical)
        {
            if (element is not null
                && hierarchical.FindHierarchicalParent<Element>(includeSelf: true) is { } ownerElement
                && !ReferenceEquals(ownerElement, element))
            {
                return true;
            }

            if (scene is not null
                && hierarchical.FindHierarchicalParent<Scene>(includeSelf: true) is { } ownerScene
                && !ReferenceEquals(ownerScene, scene))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportRecovery(
        Element element,
        Dictionary<CoreObject, CoreSerializationVisit> locations,
        Action<CoreSerializationVisit, Issue> report)
    {
        if (element.SuppressedStorageSource is not { } source) return;

        var incidentsByOwner = (source.RecoveryIncidents ?? []).ToLookup(incident =>
            incident.Owner is { } reference && reference.TryGetTarget(out var owner) ? owner : null,
            ReferenceEqualityComparer.Instance);
        CoreSerializationVisit elementVisit = locations[element];
        HashSet<CoreObject>? saved = null;

        foreach (var (owner, member) in SceneRecovery.GetUnresolvedDroppedValues(element, IsInGraph))
        {
            CoreSerializationVisit visit = locations.GetValueOrDefault(owner, elementVisit);
            var matches = incidentsByOwner[owner]
                .Where(incident => incident.IsDroppedValue && (member is null || incident.Member == member))
                .ToArray();
            if (matches.Length == 0)
            {
                report(visit, new(UndisplayableDataKind.DroppedValue, Target: owner, Member: member));
            }

            foreach (var incident in matches)
            {
                report(visit, RecoveryIssue(incident, owner, member));
            }
        }

        // Some values are written through a custom converter, outside the visit. Check their identity
        // against the live serialization so a repaired keyframe or removed owner stops appearing.
        foreach (SuppressedRecoveryIncident incident in source.RecoveryIncidents ?? [])
        {
            if (incident.IsDroppedValue || incident.CoveredByFallback) continue;
            if (incident.Owner is null)
            {
                report(elementVisit, RecoveryIssue(incident));
            }
            else if (incident.Owner.TryGetTarget(out var owner) && !locations.ContainsKey(owner))
            {
                if (owner is KeyFrame { HasLossyEasing: true } keyFrame && IsInGraph(owner))
                    report(elementVisit, EasingIssue(keyFrame) with { Target = owner });
                else if (owner is IFallback fallback && IsInGraph(owner))
                    report(elementVisit, FallbackIssue(fallback) with { Target = owner });
            }
        }

        bool IsInGraph(CoreObject owner)
        {
            if (locations.ContainsKey(owner)) return true;
            if (saved is null)
            {
                using var capture = new SerializedObjectCapture();
                CoreSerializer.SerializeToJsonObject(element, new CoreSerializerOptions
                {
                    BaseUri = element.Uri,
                    Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
                });
                saved = new HashSet<CoreObject>(capture.Objects, ReferenceEqualityComparer.Instance);
            }

            return saved.Contains(owner);
        }
    }

    private static Issue RecoveryIssue(SuppressedRecoveryIncident incident, CoreObject? owner = null, string? member = null)
    {
        var reason = Enum.TryParse(incident.Reason, out FallbackReason parsed) ? parsed : (FallbackReason?)null;
        return new(UndisplayableDataKind.DroppedValue, incident.TypeName, incident.Message, incident.Json,
            reason, Target: owner, Member: member);
    }

    private static Issue FallbackIssue(IFallback fallback)
    {
        if (fallback is FallbackEngineObject engine && SceneRecovery.GetUnreadableElementData(engine) is { } unreadable)
            return new(UndisplayableDataKind.DeserializationFailed, unreadable.TypeName, unreadable.ErrorMessage,
                Reason: FallbackReason.DeserializationFailed, IsUnreadable: true);

        fallback.TryGetTypeName(out string? typeName);
        if (fallback is FallbackEngineObject legacy && ElementMigration.IsUnmigratedContent(legacy))
        {
            JsonObject? original = ElementMigration.GetUnmigratedOperatorJson(legacy);
            if (original?["$type"] is JsonValue serializedType && serializedType.TryGetValue(out string? operatorType))
                typeName = operatorType;

            return new(UndisplayableDataKind.UnmigratedLegacyContent, typeName, fallback.ErrorMessage,
                original ?? fallback.Json, fallback.Reason, IsLegacyNodeTree: ElementMigration.IsUnmigratedNodeTree(legacy));
        }

        var kind = fallback.Reason == FallbackReason.TypeNotFound
            ? UndisplayableDataKind.TypeNotFound
            : UndisplayableDataKind.DeserializationFailed;
        return new(kind, typeName, fallback.ErrorMessage, fallback.Json, fallback.Reason);
    }

    private static Issue EasingIssue(KeyFrame keyFrame)
    {
        var failure = keyFrame.LossyEasingFailure;
        return new(UndisplayableDataKind.LossyEasing, failure?.TypeName, failure?.Message, keyFrame.LossyEasingJson, failure?.Reason);
    }

    private static UndisplayableDataEntry CreateEntry(CoreSerializationVisit visit, Issue issue, Project? project, Scene? scene)
    {
        var element = FindAncestor<Element>(visit);
        scene ??= FindAncestor<Scene>(visit);
        var layer = FindAncestor<TimelineLayer>(visit)
            ?? (element is null ? null : scene?.Layers.FirstOrDefault(item => item.ZIndex.Value == element.ZIndex.Value));
        var marker = FindAncestor<SceneMarker>(visit);
        var target = issue.Target ?? FindAncestor<CoreObject>(visit);
        var keyFrame = target as KeyFrame ?? FindAncestor<KeyFrame>(visit);
        var graphNode = visit.Value is GraphModel ? null : FindAncestor<GraphNode>(visit);
        GraphModel? containingGraph = null;
        GraphModel? rootGraph = null;
        ElementEdge? edge = null;
        CoreObject? propertyOwner = null;
        string? propertyName = issue.Member;
        IKeyFrameAnimation? animation = null;
        EngineObject? engine = target as EngineObject;
        Uri? uri = target?.Uri;
        for (CoreSerializationVisit? item = visit; item is not null; item = item.Parent)
        {
            if (item.Value is GraphModel graph)
            {
                containingGraph ??= graph;
                rootGraph = graph;
            }

            engine ??= item.Value as EngineObject;
            uri ??= (item.Value as CoreObject)?.Uri;
            if (item.Value is IKeyFrameAnimation keyAnimation && animation is null)
            {
                animation = keyAnimation;
                if (item.Parent is { MemberName: "Animations", Parent.Value: EngineObject animationOwner })
                {
                    propertyOwner = animationOwner;
                    propertyName = item.DictionaryKey;
                }
                else if (FindAncestor<NodeMember>(item) is { } port)
                {
                    propertyOwner = port;
                    propertyName = port.Name.Value;
                }
            }
            else if (item.MemberName is { } member && item.Parent?.Value is CoreObject owner)
            {
                if (owner is Element)
                {
                    edge = member switch
                    {
                        nameof(Element.EnterTransition) => ElementEdge.Start,
                        nameof(Element.ExitTransition) => ElementEdge.End,
                        _ => edge,
                    };
                }
                else if (propertyOwner is null && issue.Member is null && visit.Value is IFallback)
                {
                    propertyOwner = owner;
                    propertyName = owner is NodeMember ? owner.Name.Value : member;
                }
            }
        }

        uri ??= element?.SuppressedStorageSource?.SourceUri ?? scene?.Uri ?? project?.Uri;
        bool unknown = issue.Kind is UndisplayableDataKind.UnknownMembers
            or UndisplayableDataKind.UnknownAnimations or UndisplayableDataKind.UnknownExpressions;
        string[] keys = unknown && issue.Json is JsonObject json ? [.. json.Select(member => member.Key)] : [];
        propertyName ??= keys.Length == 1 ? keys[0] : null;
        return new UndisplayableDataEntry
        {
            Project = project,
            Scene = scene,
            Element = element,
            EngineObject = engine,
            TargetObject = target,
            PropertyOwner = propertyOwner,
            PropertyName = propertyName,
            TransitionEdge = edge,
            RootGraph = rootGraph,
            ContainingGraph = containingGraph,
            GraphNode = graphNode,
            Animation = animation,
            KeyFrame = keyFrame,
            KeyTime = keyFrame?.KeyTime.Value,
            Layer = layer,
            Marker = marker,
            Breadcrumb = MakeBreadcrumb(visit, project, scene, element, layer, marker, target, keyFrame, issue.Member),
            Kind = issue.Kind,
            TypeName = issue.TypeName,
            UnknownKeys = keys,
            ErrorMessage = issue.ErrorMessage,
            Reason = issue.Reason,
            SourceFilePath = uri is { IsAbsoluteUri: true, IsFile: true } file ? file.LocalPath : null,
            IsRecoveryProtected = element?.IsRecoveryProtected == true,
            IsUnreadableElement = issue.IsUnreadable,
            IsLegacyNodeTree = issue.IsLegacyNodeTree,
            Json = issue.Json,
            OriginalFile = issue.IsUnreadable ? element?.SuppressedStorageSource?.RawBytes : null,
        };
    }

    private static string MakeBreadcrumb(
        CoreSerializationVisit visit, Project? project, Scene? scene, Element? element, TimelineLayer? layer,
        SceneMarker? marker, CoreObject? target, KeyFrame? keyFrame, string? member)
    {
        var parts = new List<string>
        {
            scene is null ? (project is null ? UndisplayableDataStrings.WholeProject : Name(project, UndisplayableDataStrings.WholeProject))
                : Name(scene, Strings.Scene),
        };
        if (element is not null)
        {
            int index = element.ZIndex.Value;
            parts.Add(string.Format(CultureInfo.CurrentCulture, UndisplayableDataStrings.ClipLocation,
                Name(element, Strings.Element), LayerName(layer, index), FormatTime(element.Range.Start), FormatTime(element.Range.End)));
        }
        else if (layer is not null)
        {
            parts.Add(LayerName(layer, layer.ZIndex.Value));
        }
        else if (marker is not null)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, UndisplayableDataStrings.MarkerLocation,
                Name(marker, UndisplayableDataStrings.Marker), FormatTime(marker.Time.Value)));
        }

        var path = new Stack<string>();
        for (CoreSerializationVisit? item = visit; item is not null; item = item.Parent)
        {
            if (item.Value is Element or Scene or Project or TimelineLayer or SceneMarker) break;
            string? segment = item.Index is { } index ? $"[{index + 1}]" : item.MemberName ?? item.DictionaryKey;
            if (item.Value is CoreObject obj && item.Value is not KeyFrame)
            {
                string name = Name(obj, obj is IFallback ? Strings.Unknown : TypeDisplayHelpers.GetLocalizedName(obj.GetType()));
                segment = segment is null ? name : $"{segment} ({name})";
            }

            if (segment is not null) path.Push(segment);
        }
        parts.AddRange(path);
        if (visit.Value is Element && target is not null && target != element)
            parts.Add(Name(target, TypeDisplayHelpers.GetLocalizedName(target.GetType())));
        if (keyFrame is not null)
            parts.Add(string.Format(CultureInfo.CurrentCulture, UndisplayableDataStrings.KeyframeLocation, FormatTime(keyFrame.KeyTime.Value)));
        if (member is not null) parts.Add(member);
        return string.Join(" › ", parts);
    }

    private static string Name(CoreObject obj, string fallback)
        => !string.IsNullOrWhiteSpace(obj.Name.Value) ? obj.Name.Value
            : obj.Uri is { IsAbsoluteUri: true, IsFile: true } uri ? Path.GetFileNameWithoutExtension(uri.LocalPath) : fallback;

    private static string LayerName(TimelineLayer? layer, int index)
        => !string.IsNullOrWhiteSpace(layer?.Name.Value) ? layer.Name.Value
            : string.Format(CultureInfo.CurrentCulture, UndisplayableDataStrings.LayerLocation, index);

    private static string FormatTime(MediaTime time) => time.ToTimeSpan().ToString("c", CultureInfo.CurrentCulture);

    private static T? FindAncestor<T>(CoreSerializationVisit visit) where T : class
    {
        for (CoreSerializationVisit? item = visit; item is not null; item = item.Parent)
            if (item.Value is T result) return result;
        return null;
    }

    private readonly record struct Issue(
        UndisplayableDataKind Kind,
        string? TypeName = null,
        string? ErrorMessage = null,
        JsonNode? Json = null,
        FallbackReason? Reason = null,
        bool IsUnreadable = false,
        bool IsLegacyNodeTree = false,
        CoreObject? Target = null,
        string? Member = null);
}

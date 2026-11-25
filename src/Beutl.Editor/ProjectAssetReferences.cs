using Beutl.Editor.Operations;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor;

internal sealed class ProjectAssetReferences
{
    internal sealed record Use(IFileSource Source, Uri Uri, string[] Files)
    {
        internal HashSet<CoreObject> Documents { get; } = new(ReferenceEqualityComparer.Instance);
    }

    internal string[] Documents { get; private set; } = [];
    internal Dictionary<IFileSource, Use> Sources { get; } = new(ReferenceEqualityComparer.Instance);
    internal HashSet<IFileSource> OtherEditorSources { get; } = new(ReferenceEqualityComparer.Instance);
    internal HashSet<string> InUse { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> RetainedByHistory { get; } = new(StringComparer.Ordinal);
    internal bool Incomplete { get; private set; }

    // Capture on the editing thread. Each saved document gets its own visit so a source shared
    // through the same nested object in two documents is written to both files after a relink.
    internal static ProjectAssetReferences Capture(Project project, IEnumerable<CoreObject> otherEditors,
        IEnumerable<ChangeOperation> history, CancellationToken token)
    {
        var result = new ProjectAssetReferences();
        var documents = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance) { project };
        var pending = new Queue<CoreObject>();
        pending.Enqueue(project);
        while (pending.TryDequeue(out CoreObject? document))
        {
            CoreSerializer.Visit(document, visit =>
            {
                token.ThrowIfCancellationRequested();
                if (visit.Value is CoreObject { Uri: not null } referenced && visit.Parent != null)
                {
                    if (documents.Add(referenced)) pending.Enqueue(referenced);
                    return CoreSerializationVisitAction.SkipChildren;
                }

                result.Protect(visit.Value, result.InUse);
                if (visit.Value is IFileSource source)
                {
                    if (!result.Sources.TryGetValue(source, out Use? use))
                    {
                        string[] files = [.. FileSourceFiles.EnumerateLocalPaths(source)];
                        if (files.Length == 0) return CoreSerializationVisitAction.Continue;
                        result.Sources.Add(source, use = new Use(source, source.Uri, files));
                    }
                    use.Documents.Add(document);
                }

                return CoreSerializationVisitAction.Continue;
            });
        }

        result.Documents = [.. documents.Select(document => document.Uri).OfType<Uri>()
            .Where(uri => uri.IsFile).Select(uri => uri.LocalPath).Distinct(StringComparer.Ordinal)];
        CoreSerializer.Visit(otherEditors.ToArray(), visit =>
        {
            token.ThrowIfCancellationRequested();
            result.Protect(visit.Value, result.InUse);
            if (visit.Value is IFileSource source) result.OtherEditorSources.Add(source);
            return false;
        });

        var historyValues = new List<object?>();
        foreach (ChangeOperation operation in history)
        {
            switch (operation)
            {
                case IUpdatePropertyValueOperation property:
                    historyValues.Add(property.OldValue);
                    historyValues.Add(property.NewValue);
                    break;
                case ICollectionChangeOperation collection:
                    historyValues.AddRange(collection.Items);
                    break;
                case UpdateNodeMemberOperation member:
                    historyValues.Add(member.OldValue);
                    historyValues.Add(member.NewValue);
                    break;
                case UpdateSplineEasingOperation:
                    break;
                default:
                    result.Incomplete = true;
                    break;
            }
        }

        CoreSerializer.Visit(historyValues, visit =>
        {
            token.ThrowIfCancellationRequested();
            result.Protect(visit.Value, result.RetainedByHistory);
            return false;
        });
        return result;
    }

    private void Protect(object value, HashSet<string> paths)
    {
        paths.UnionWith(FileSourceFiles.EnumerateReferencedPaths(value));
        if (value is IFallback { Json: not null } or ModelSource { HasUri: true, MeshCount: 0 }
            || value is CoreObject obj && (obj.SuppressedStorageSource != null || SceneRecovery.HoldsUnresolvedDroppedValue(obj)))
        {
            Incomplete = true;
        }
    }
}

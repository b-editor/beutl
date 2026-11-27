using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.Language;
using Beutl.Media.Decoding;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record ProjectAssetEntry(
    string Path, string Kind, long SizeBytes, bool IsInsideProject, IReadOnlyList<Guid> ElementIds,
    IReadOnlyList<string> Files, string? BlockingIssue, string? RestoreIssue);

public sealed record InspectProjectAssetsResponse(
    IReadOnlyList<ProjectAssetEntry> External, IReadOnlyList<ProjectAssetEntry> Unused,
    IReadOnlyList<ProjectAssetEntry> Quarantined, IReadOnlyList<ProjectAssetEntry> ReferencedInsideProject,
    string? CleanupIssue);

public sealed record ProjectAssetFileChange(string Path, string? Destination);

public sealed record OrganizeProjectAssetsResponse(string Action, IReadOnlyList<ProjectAssetFileChange> Files);

[McpServerToolType]
public sealed class ProjectAssetTools(
    AgentSessionManager sessions, IProjectSessionGateway projects, DecoderRegistry decoders) : ToolBase
{
    private const string SceneDescription =
        "Live only. ID from list_scenes to identify the open project. Omit to use the open project directly. File calls use the active file session.";

    private readonly ProjectAssetService _assets = new(decoders);

    [McpServerTool(Name = "inspect_project_assets", ReadOnly = true)]
    [Description("Lists the project's external, unused and quarantined assets as in Project Assets, plus referenced assets inside the project. Includes file paths, kinds, sizes in bytes, element IDs and issues that block organizing.")]
    public ValueTask<ToolResult<InspectProjectAssetsResponse>> InspectProjectAssets(
        [Description(SceneDescription)] string? sceneId = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            Guid? scene = ReadSceneId(sceneId);
            ProjectAssetService.Inspection inspection;
            if (projects is ILiveProjectAssetGateway live)
            {
                inspection = await live.InspectProjectAssetsAsync(scene, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                FileEditingSession file = RequireFileSession(scene);
                inspection = await Task.Run(() => file.ReadOnSession(() =>
                {
                    ProjectAssetPaths paths = RequireProjectPaths(file);
                    var references = ProjectAssetReferences.Capture(
                        file.Project, [], file.History.GetRetainedOperations(), cancellationToken, includeElementIds: true);
                    return _assets.Inspect(paths, references, cancellationToken);
                }), cancellationToken).ConfigureAwait(false);
            }

            return new InspectProjectAssetsResponse(
                inspection.External.Select(Describe).ToArray(), inspection.Unused.Select(Describe).ToArray(),
                inspection.Quarantined.Select(Describe).ToArray(), inspection.ReferencedInsideProject.Select(Describe).ToArray(),
                inspection.CleanupIssue?.ToString());
        });

    [McpServerTool(Name = "organize_project_assets")]
    [Description("Applies a Project Assets operation to the supplied paths. Collect copies external files into the project, relinks every use and saves the affected documents. Quarantine moves unused files aside. Restore returns quarantined files to their original location. Delete permanently removes one quarantined file.")]
    public ValueTask<ToolResult<OrganizeProjectAssetsResponse>> OrganizeProjectAssets(
        [Description("Operation to apply: collect, quarantine, restore, or delete.")] string action,
        [Description("Absolute paths from the corresponding list in inspect_project_assets. Supply the main path for a bundle. Delete takes one path.")] string[] paths,
        [Description("Set to true to confirm permanent deletion.")] bool confirmDelete = false,
        [Description(SceneDescription)] string? sceneId = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            ProjectAssetAction operation = action switch
            {
                "collect" => ProjectAssetAction.Collect,
                "quarantine" => ProjectAssetAction.Quarantine,
                "restore" => ProjectAssetAction.Restore,
                "delete" => ProjectAssetAction.Delete,
                _ => throw Rejected(ProjectAssetsStrings.InvalidAction, nameof(action))
            };
            if (paths is not { Length: > 0 })
                throw Rejected(ProjectAssetsStrings.SelectAssetPaths, nameof(paths));
            if (operation == ProjectAssetAction.Delete)
            {
                if (!confirmDelete)
                    throw new ReconcileException(new ToolError(
                        ErrorCode.DestructiveIntent, ProjectAssetsStrings.ConfirmPermanentDelete, nameof(confirmDelete)));
                // The editor deletes each file permanently as it goes. A single-file call cannot
                // leave an earlier deletion committed if a later file fails or the call is canceled.
                if (paths.Length != 1)
                    throw Rejected(ProjectAssetsStrings.DeleteOneAsset, nameof(paths));
            }

            string[] selectedPaths = [.. paths.Select(path => MissingMediaTools.ReadAbsolutePath(path, nameof(paths)))
                .Distinct(StringComparer.Ordinal)];
            Guid? scene = ReadSceneId(sceneId);
            ProjectAssetItem[] selected;
            if (projects is ILiveProjectAssetGateway live)
                selected = await live.OrganizeProjectAssetsAsync(scene, operation, Select, cancellationToken).ConfigureAwait(false);
            else
                selected = await OrganizeFileAssetsAsync(RequireFileSession(scene), operation, Select, cancellationToken).ConfigureAwait(false);

            return new OrganizeProjectAssetsResponse(action, [.. selected.SelectMany(item => item.Files)
                .Select(file => new ProjectAssetFileChange(file.Path, operation == ProjectAssetAction.Delete ? null : file.Destination))]);

            ProjectAssetItem[] Select(ProjectAssetPreview preview)
            {
                var rows = preview.ForAction(operation).ToDictionary(item => item.Path, StringComparer.Ordinal);
                return [.. selectedPaths.Select(path => rows.TryGetValue(path, out ProjectAssetItem? item) && item.CanApply(operation)
                    ? item
                    : throw Rejected(ProjectAssetsStrings.SelectAssetPaths, path))];
            }
        });

    // Keep the session lock through the editor service's file and document transaction.
    private Task<ProjectAssetItem[]> OrganizeFileAssetsAsync(FileEditingSession file, ProjectAssetAction action,
        Func<ProjectAssetPreview, ProjectAssetItem[]> select, CancellationToken token)
        => Task.Run(() => file.EditOnSession(() =>
        {
            ProjectAssetPaths paths = RequireProjectPaths(file);
            file.CheckProjectStamp(paths.ProjectFile);
            // Collection writes project membership and only the documents affected by the relink.
            if (action == ProjectAssetAction.Collect && file.HasUnsavedProjectChanges)
                throw Rejected(ProjectAssetsStrings.SaveProjectBeforeCollecting, "project");

            file.History.FlushPendingMutations();
            var references = ProjectAssetReferences.Capture(file.Project, [], file.History.GetRetainedOperations(), token);
            ProjectAssetService.Analysis analysis = _assets.Analyze(paths, references, Guid.NewGuid(), token);
            ProjectAssetItem[] selected = select(analysis.Preview);
            file.CheckProjectStamp(paths.ProjectFile);
            using var suppressRecording = file.History.SuppressRecording();
            byte[]? projectBeforeSave = null;
            IReadOnlyCollection<CoreObject> saved;
            try
            {
                saved = _assets.ApplyAsync(file.Project, paths, analysis, action, selected, token, beforeCollectionSave: () =>
                {
                    byte[] contents = File.ReadAllBytes(paths.ProjectFile);
                    file.CheckProjectStamp(paths.ProjectFile);
                    projectBeforeSave = contents;
                }).GetAwaiter().GetResult();
            }
            catch
            {
                if (projectBeforeSave is not null)
                    file.AcceptRestoredProjectStamp(paths.ProjectFile, projectBeforeSave);
                throw;
            }

            if (saved.Count > 0) file.AcceptExternalStamp();
            return selected;
        }, token), token);

    private FileEditingSession RequireFileSession(Guid? sceneId)
    {
        if (sceneId is not null)
            throw Rejected(ProjectAssetsStrings.InvalidSceneId, "sceneId");
        return sessions.CurrentSession as FileEditingSession ?? throw new SessionUnavailableException();
    }

    private static ProjectAssetPaths RequireProjectPaths(FileEditingSession file)
    {
        if (file.Project.Uri is not { IsFile: true } uri || !File.Exists(uri.LocalPath))
            throw new ProjectAssetPlanChangedException();
        return new ProjectAssetPaths(file.Project);
    }

    private static Guid? ReadSceneId(string? sceneId)
    {
        if (sceneId is null) return null;
        if (Guid.TryParse(sceneId, out Guid id) && id != Guid.Empty) return id;
        throw Rejected(ProjectAssetsStrings.InvalidSceneId, nameof(sceneId));
    }

    private static ProjectAssetEntry Describe(ProjectAssetService.AssetInfo asset)
        => new(asset.Item.Path, asset.Kind, asset.Item.Size, asset.IsInsideProject, asset.ElementIds,
            [.. asset.Item.Files.Select(file => file.Path)], asset.Item.BlockingIssue?.ToString(), asset.Item.RestoreIssue?.ToString());

    private static ReconcileException Rejected(string message, string target)
        => new(new ToolError(ErrorCode.ValidationRejected, message, target));
}

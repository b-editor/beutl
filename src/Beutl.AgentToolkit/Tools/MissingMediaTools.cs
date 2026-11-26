using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record MissingMediaItem(string Kind, string Name, string? OriginalPath, IReadOnlyList<string> ElementIds);
public sealed record ScanMissingMediaResponse(IReadOnlyList<MissingMediaItem> Missing);
public sealed record MediaRelinkPair(string MissingPath, string NewPath);
public sealed record RelinkedMedia(MissingMediaItem Media, string NewPath);
public sealed record MediaRelinkIssue(string? OriginalPath, string NewPath, string Message);
public sealed record RelinkMissingMediaResponse(
    IReadOnlyList<RelinkedMedia> Relinked, IReadOnlyList<MissingMediaItem> RemainingMissing, IReadOnlyList<MediaRelinkIssue> Issues);

[McpServerToolType]
public sealed class MissingMediaTools(AgentSessionManager sessions, DecoderRegistry decoders) : ToolBase
{
    [McpServerTool(Name = "scan_missing_media", ReadOnly = true)]
    [Description("Scans the target scene for missing or unreadable media and fonts. Returns the resource kind, original file path when available, and element IDs.")]
    public ValueTask<ToolResult<ScanMissingMediaResponse>> ScanMissingMedia(CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            var (_, _, missing) = await ScanAsync(sessions.RequireSession(), cancellationToken).ConfigureAwait(false);
            return new ScanMissingMediaResponse(missing.Select(Describe).ToArray());
        });

    [McpServerTool(Name = "relink_missing_media")]
    [Description("Repairs and saves the target scene's missing media using replacement files or a folder search. Reports the applied repairs and everything still missing. Repairs are outside edit history, as in the Missing Media tab.")]
    public ValueTask<ToolResult<RelinkMissingMediaResponse>> RelinkMissingMedia(
        [Description("Pairs of absolute missingPath and newPath. Use missing paths returned by scan_missing_media.")]
        MediaRelinkPair[]? replacements = null,
        [Description("Absolute folder to search recursively for matching media and fonts. Supply this or replacements.")]
        string? searchFolder = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            if ((replacements is { Length: > 0 }) == (searchFolder is not null))
                throw InvalidInput("Supply either replacement pairs or a search folder.", nameof(replacements));

            IEditingSession session = sessions.RequireSession();
            var (scene, detector, missing) = await ScanAsync(session, cancellationToken).ConfigureAwait(false);
            var issues = new List<MediaRelinkIssue>();
            var candidates = new List<MediaRepairSelection>();
            if (searchFolder is not null)
            {
                string folder = ReadAbsolutePath(searchFolder, nameof(searchFolder));
                if (!Directory.Exists(folder)) throw InvalidInput("The search folder does not exist.", folder);
                if (missing.Count > 0)
                {
                    var matches = await detector.FindMatchesAsync(missing, folder, cancellationToken).ConfigureAwait(false);
                    candidates.AddRange(matches.Select(match => new MediaRepairSelection(match.Key, match.Value[0], match.Value)));
                }
            }
            else
            {
                var paths = new Dictionary<string, string>(StringComparer.FromComparison(PathBoundary.Comparison));
                var missingByPath = missing.Where(media => media.ExpectedUri is not null)
                    .ToLookup(media => media.ExpectedUri!.LocalPath, paths.Comparer);
                foreach (MediaRelinkPair pair in replacements!)
                {
                    string original = ReadAbsolutePath(pair?.MissingPath, nameof(MediaRelinkPair.MissingPath));
                    string replacement = ReadAbsolutePath(pair?.NewPath, nameof(MediaRelinkPair.NewPath));
                    if (!paths.TryAdd(original, replacement) && !paths.Comparer.Equals(paths[original], replacement))
                        throw InvalidInput("A missing path has more than one replacement.", original);
                }

                foreach (var (original, replacement) in paths)
                {
                    var matches = missingByPath[original];
                    if (!matches.Any()) issues.Add(new MediaRelinkIssue(original, replacement, "The path is not missing in this scene."));
                    candidates.AddRange(matches.Select(media => new MediaRepairSelection(media, replacement)));
                }
            }

            var selected = new List<MediaRepairSelection>();
            foreach (MediaRepairSelection candidate in candidates)
            {
                try
                {
                    await detector.ValidateAsync(candidate.Media, candidate.NewPath, cancellationToken).ConfigureAwait(false);
                    selected.Add(candidate);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    issues.Add(new MediaRelinkIssue(candidate.Media.ExpectedUri?.LocalPath, candidate.NewPath,
                        $"The replacement could not be opened as {candidate.Media.Kind.ToString().ToLowerInvariant()} media."));
                }
            }

            MediaRepairSelection[] applied = [];
            if (selected.Count > 0)
            {
                (applied, missing) = session switch
                {
                    LiveEditingSession { MediaRepair: { } live } => await live.ApplyMediaRepairsAsync(
                        scene, selected, LiveEditingSession.EditSessionWait, cancellationToken).ConfigureAwait(false),
                    FileEditingSession file => await ApplyFileRepairsAsync(file, scene, detector, selected, cancellationToken)
                        .ConfigureAwait(false),
                    _ => throw new SessionUnavailableException()
                };
            }

            return new RelinkMissingMediaResponse(applied.Select(item => new RelinkedMedia(Describe(item.Media), item.NewPath)).ToArray(),
                missing.Select(Describe).ToArray(), issues);
        });

    private async Task<(Scene Scene, MissingMediaService Detector, IReadOnlyList<MissingMedia> Missing)> ScanAsync(
        IEditingSession session, CancellationToken token)
    {
        var (scene, detector, scan) = session.ReadOnSession(() =>
        {
            Scene scene = RequireSceneRoot(session);
            ILiveMediaRepairBinding? live = (session as LiveEditingSession)?.MediaRepair;
            MissingMediaService detector = live?.MissingMediaDetector ?? new MissingMediaService(session.Fonts, decoders);
            var references = live?.MediaReferencesForRepair ?? new MediaReferenceIndex(scene).GetReferences(includeSharedReferences: true);
            return (scene, detector, detector.FindMissingAsync(scene, references, token, revalidateExisting: true));
        });
        return (scene, detector, await scan.ConfigureAwait(false));
    }

    private static async Task<(MediaRepairSelection[], IReadOnlyList<MissingMedia>)> ApplyFileRepairsAsync(
        FileEditingSession session, Scene scene, MissingMediaService detector, IReadOnlyList<MediaRepairSelection> selections,
        CancellationToken token)
    {
        // File sessions use a synchronous dispatch lock. Run the repair there on a worker, including
        // the copy and save, so concurrent scene changes cannot get between them.
        var (applied, scan) = await Task.Run(() => session.EditOnSession(() =>
        {
            if (!ReferenceEquals(scene, session.Scene)) throw SceneChanged();
            var validated = selections.Select(selection => MissingMediaRepair.ValidateAsync(detector, selection, token)
                .GetAwaiter().GetResult()).ToArray();
            using var repair = MissingMediaRepair.Create(scene, session.History, session.Fonts,
                () => session.Fonts.LoadProjectFonts(session.Project),
                new MediaReferenceIndex(scene).GetReferences(includeSharedReferences: true), validated) ?? throw SceneChanged();
            if (repair.Applied.Length > 0)
            {
                repair.ApplyAsync(token).GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
                using var write = StorageWriteTransaction.Begin();
                session.Save(skipConflictCheck: false);
                write.Commit();
                repair.Commit();
            }

            var references = new MediaReferenceIndex(scene).GetReferences(includeSharedReferences: true);
            return (repair.Applied, detector.FindMissingAsync(scene, references, token, revalidateExisting: true));
        }, token), token).ConfigureAwait(false);
        return (applied, await scan.ConfigureAwait(false));
    }

    private static MissingMediaItem Describe(MissingMedia media) => new(
        media.Kind.ToString().ToLowerInvariant(), media.Name, media.ExpectedUri?.LocalPath,
        media.References.Select(reference => reference.Element).OfType<Element>()
            .Select(element => element.Id.Value.ToString()).Distinct().ToArray());

    internal static string ReadAbsolutePath(string? path, string target)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw InvalidInput("Supply an absolute filesystem path.", target);
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw InvalidInput("Supply a valid filesystem path.", target);
        }
    }

    internal static ReconcileException SceneChanged() => new(new ToolError(ErrorCode.StaleHandle,
        "The scene changed during media repair.", null, "Run scan_missing_media again and retry."));

    private static ReconcileException InvalidInput(string message, string target)
        => new(new ToolError(ErrorCode.ValidationRejected, message, target));
}

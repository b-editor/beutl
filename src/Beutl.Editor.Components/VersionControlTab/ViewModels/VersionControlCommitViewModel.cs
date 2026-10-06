using Beutl.Editor.VersionControl;
using Reactive.Bindings;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal sealed class VersionControlCommitViewModel : IDisposable
{
    private readonly VersionControlTabViewModel _owner;

    internal VersionControlCommitViewModel(
        VersionControlTabViewModel owner,
        CommitInfo commit,
        VersionControlRelativeTimeFormatter relativeTimeFormatter)
    {
        _owner = owner;
        Commit = commit;
        KindText = GetKindText(commit.Kind);
        DisplayMessage = commit.Subject;
        AuthorAndRelativeDate = string.Format(
            CultureInfo.CurrentCulture,
            "{0} · {1}",
            commit.AuthorName,
            relativeTimeFormatter.Format(commit.AuthorDate));
        AbsoluteLocalDate = relativeTimeFormatter.FormatAbsoluteLocal(commit.AuthorDate);
        RestoreCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.RestoreAsync(Commit));
        RestoreToNewBranchCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.RestoreToNewBranchAsync(Commit));
    }

    public CommitInfo Commit { get; }

    public string KindText { get; }

    public bool IsManual => Commit.Kind == SnapshotKind.Manual;

    public bool IsSave => Commit.Kind == SnapshotKind.Save;

    public bool IsClose => Commit.Kind == SnapshotKind.Close;

    public bool IsSafety => Commit.Kind == SnapshotKind.Safety;

    public bool IsRestore => Commit.Kind is SnapshotKind.Restore or SnapshotKind.Recovery;

    public bool IsInit => Commit.Kind == SnapshotKind.Init;

    public string DisplayMessage { get; }

    public string AuthorAndRelativeDate { get; }

    public string AbsoluteLocalDate { get; }

    public AsyncReactiveCommand RestoreCommand { get; }

    public AsyncReactiveCommand RestoreToNewBranchCommand { get; }

    public void Dispose()
    {
        RestoreCommand.Dispose();
        RestoreToNewBranchCommand.Dispose();
    }

    private static string GetKindText(SnapshotKind kind)
    {
        return kind switch
        {
            SnapshotKind.Save => Strings.VersionControl_SnapshotSave,
            SnapshotKind.Close => Strings.VersionControl_SnapshotClose,
            SnapshotKind.Safety => Strings.VersionControl_SnapshotSafety,
            SnapshotKind.Restore => Strings.VersionControl_SnapshotRestore,
            SnapshotKind.Recovery => Strings.VersionControl_SnapshotRecovery,
            SnapshotKind.Init => Strings.VersionControl_SnapshotInit,
            _ => Strings.VersionControl_SnapshotManual,
        };
    }
}

using System.Windows.Input;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal enum VersionControlPrimaryActionKind
{
    Commit,
    Pull,
    Push,
    UpToDate,
    PublishBranch,
    Cancel,
}

internal sealed record VersionControlPrimaryAction(
    VersionControlPrimaryActionKind Kind,
    string Label,
    ICommand Command);

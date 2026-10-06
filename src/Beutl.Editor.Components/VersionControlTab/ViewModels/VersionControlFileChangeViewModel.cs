using Beutl.Editor.VersionControl;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal sealed class VersionControlFileChangeViewModel
{
    public VersionControlFileChangeViewModel(FileChange change)
    {
        Change = change;
    }

    public FileChange Change { get; }

    public string StatusText => Change.Status switch
    {
        FileChangeStatus.Added => "A",
        FileChangeStatus.Deleted => "D",
        FileChangeStatus.Renamed => "R",
        _ => "M",
    };

    public string PathText => Change.OldPath is null
        ? Change.Path
        : $"{Change.OldPath} → {Change.Path}";
}

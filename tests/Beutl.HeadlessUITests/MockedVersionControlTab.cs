using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// The version control tab over a mocked service whose history lists the commits, each changing the files
// with the diff.
internal sealed class MockedVersionControlTab : IDisposable
{
    private readonly ReactivePropertySlim<IProjectVersionControlService?> _source;

    private MockedVersionControlTab(
        Mock<IProjectVersionControlService> service,
        ReactivePropertySlim<IProjectVersionControlService?> source,
        VersionControlTabViewModel viewModel)
    {
        Service = service;
        _source = source;
        ViewModel = viewModel;
    }

    public Mock<IProjectVersionControlService> Service { get; }

    public VersionControlTabViewModel ViewModel { get; }

    public static async Task<MockedVersionControlTab> CreateAsync(
        string root,
        IReadOnlyList<CommitInfo> commits,
        IReadOnlyList<FileChange> files,
        string diff,
        WorkspaceStatus? status = null,
        IProjectVersionControlCoordinator? coordinator = null)
    {
        var service = new Mock<IProjectVersionControlService>();
        service.SetupGet(x => x.Repository).Returns(new RepositoryInfo(root, root));
        service.Setup(x => x.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitAvailability(GitAvailabilityState.Installed, "git", new Version(2, 50), false));
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status ?? new WorkspaceStatus("main", 0, 0, [], false));
        service.Setup(x => x.GetRemotesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        service.Setup(x => x.GetHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(commits);
        service.Setup(x => x.GetCommitFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(files);
        service.Setup(x => x.GetDiffAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(diff);
        var source = new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        var viewModel = new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(), Mock.Of<IEditorContext>(), source, coordinator, action => action());
        await viewModel.Initialization;
        return new MockedVersionControlTab(service, source, viewModel);
    }

    public void Dispose()
    {
        ViewModel.Dispose();
        _source.Dispose();
    }
}

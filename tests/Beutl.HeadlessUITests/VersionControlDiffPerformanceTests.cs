using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.Components.VersionControlTab.Views;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlDiffPerformanceTests
{
    [AvaloniaTest]
    [TestCase(320)]
    [TestCase(900)]
    public async Task Large_diff_realizes_only_visible_rows_and_can_scroll_to_the_end(int width)
    {
        string root = Path.GetTempPath();
        var service = new Mock<IProjectVersionControlService>();
        service.SetupGet(x => x.Repository).Returns(new RepositoryInfo(root, root));
        service.Setup(x => x.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitAvailability(GitAvailabilityState.Installed, "git", new Version(2, 50), false));
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, [], false));
        service.Setup(x => x.GetRemotesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var commit = new CommitInfo(new string('a', 40), "aaaaaaa", "large diff", "Test", DateTimeOffset.UnixEpoch, SnapshotKind.Save);
        var file = new FileChange("project.bep", FileChangeStatus.Modified);
        service.Setup(x => x.GetHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([commit]);
        service.Setup(x => x.GetCommitFilesAsync(commit.Sha, It.IsAny<CancellationToken>())).ReturnsAsync([file]);
        string diff = "@@ -0,0 +1,30000 @@\n+" + new string('x', 300) + "\n"
            + string.Join('\n', Enumerable.Range(1, 29999).Select(i => $"+line {i}"));
        service.Setup(x => x.GetDiffAsync(commit.Sha, file.Path, It.IsAny<CancellationToken>())).ReturnsAsync(diff);
        using var source = new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var viewModel = new VersionControlTabViewModel(
            Mock.Of<ToolTabExtension>(), Mock.Of<IEditorContext>(), source, null, action => action());
        await viewModel.Initialization;
        await viewModel.OpenCommitDetailAsync(viewModel.Commits[0]);
        await viewModel.SelectFileAsync(viewModel.ChangedFiles[0]);
        var view = new VersionControlTabView { DataContext = viewModel };
        var window = new Window { Width = width, Height = 600, Content = view };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            // Attaching the two responsive history views initializes their selection bindings.
            // Select the preview after that, as the user does in the attached view.
            await viewModel.OpenCommitDetailAsync(viewModel.Commits[0]);
            HeadlessTestHelpers.Render();
            await viewModel.SelectFileAsync(viewModel.ChangedFiles[0]);
            HeadlessTestHelpers.Render();
            Assert.That(viewModel.DiffLines.Count, Is.EqualTo(30001));
            var changes = view.FindControl<VersionControlChangesView>(
                width == 320 ? "NarrowChangesView" : "WideChangesView")!;
            var list = changes.FindControl<ItemsControl>("DiffList")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(list.ItemsPanelRoot, Is.TypeOf<VirtualizingStackPanel>());
                Assert.That(list.GetRealizedContainers().Count(), Is.InRange(1, 200));
                Assert.That(scroll.Extent.Width, Is.GreaterThan(scroll.Viewport.Width));
            });
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(list.GetRealizedContainers().Count(), Is.InRange(1, 200));
                Assert.That(list.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "+line 29999"), Is.True);
            });
        }
        finally
        {
            window.Close();
        }
    }
}

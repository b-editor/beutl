using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.Components.VersionControlTab.Views;
using Beutl.Editor.VersionControl;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlDiffPerformanceTests
{
    [AvaloniaTest]
    [TestCase(320)]
    [TestCase(900)]
    public async Task Metadata_change_replaces_visible_diff_without_reselecting_the_file(int width)
    {
        var commit = new CommitInfo(new string('a', 40), "aaaaaaa", "preview", "Test", DateTimeOffset.UnixEpoch, SnapshotKind.Save);
        var file = new FileChange("project.bep", FileChangeStatus.Modified);
        var status = new WorkspaceStatus("main", 0, 0, [], false) { HeadCommit = commit.Sha };
        using MockedVersionControlTab tab = await MockedVersionControlTab.CreateAsync(
            Path.GetTempPath(), [commit], [file], "+old-text", status);
        Mock<IProjectVersionControlService> service = tab.Service;
        VersionControlTabViewModel viewModel = tab.ViewModel;
        var view = new VersionControlTabView { DataContext = viewModel };
        var window = new Window { Width = width, Height = 600, Content = view };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            await viewModel.OpenCommitDetailAsync(viewModel.Commits[0]);
            HeadlessTestHelpers.Render();
            await viewModel.SelectFileAsync(viewModel.ChangedFiles[0]);
            HeadlessTestHelpers.Render();
            var selection = viewModel.SelectedFile.Value;
            var changes = view.FindControl<VersionControlChangesView>(width == 320 ? "NarrowChangesView" : "WideChangesView")!;
            var list = changes.FindControl<ItemsControl>("DiffList")!;
            Assert.That(list.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "+old-text"), Is.True);

            const string binaryDiff = "Binary files a/project.bep and b/project.bep differ";
            service.Setup(x => x.GetDiffAsync(commit.Sha, file.Path, It.IsAny<CancellationToken>())).ReturnsAsync(binaryDiff);
            service.Raise(x => x.StatusChanged += null, service.Object,
                status with { NotificationSequence = 1, ChangeKind = RepositoryChangeKind.Metadata });
            await viewModel.Initialization;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.SelectedFile.Value, Is.SameAs(selection));
                Assert.That(viewModel.ShowingDetail.Value, Is.True);
                Assert.That(list.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == binaryDiff), Is.True);
                Assert.That(list.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "+old-text"), Is.False);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(320)]
    [TestCase(900)]
    public async Task Large_diff_realizes_only_visible_rows_and_can_scroll_to_the_end(int width)
    {
        var commit = new CommitInfo(new string('a', 40), "aaaaaaa", "large diff", "Test", DateTimeOffset.UnixEpoch, SnapshotKind.Save);
        var file = new FileChange("project.bep", FileChangeStatus.Modified);
        string diff = "@@ -0,0 +1,30000 @@\n+" + new string('x', 300) + "\n"
            + string.Join('\n', Enumerable.Range(1, 29999).Select(i => $"+line {i}"));
        using MockedVersionControlTab tab = await MockedVersionControlTab.CreateAsync(
            Path.GetTempPath(), [commit], [file], diff);
        VersionControlTabViewModel viewModel = tab.ViewModel;
        var view = new VersionControlTabView { DataContext = viewModel };
        var window = new Window { Width = width, Height = 600, Content = view };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            // Select after attaching: attaching initializes the history views' selection bindings.
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

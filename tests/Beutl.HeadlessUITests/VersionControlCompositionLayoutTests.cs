using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
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
public class VersionControlCompositionLayoutTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(320, true)]
    [TestCase(900, false)]
    [TestCase(900, true)]
    public async Task Composer_tracks_history_width_and_details_use_the_full_height(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"version-layout-{Guid.NewGuid():N}");
        string message = "Adjust animation timing and preview layout across the entire composition\nKeep transitions smooth between scenes.";
        CommitInfo[] commits =
        [
            new("abcdef1234567890", "abcdef1", message, "Editor", DateTimeOffset.UtcNow, SnapshotKind.Manual),
            new("1234567890abcdef", "1234567", "Save project", "Editor", DateTimeOffset.UtcNow.AddMinutes(-8), SnapshotKind.Save),
            new("9876543210abcdef", "9876543", "Restore composition", "Editor", DateTimeOffset.UtcNow.AddHours(-1), SnapshotKind.Restore)
        ];
        FileChange[] files =
        [
            new("scenes/main.scene", FileChangeStatus.Modified),
            new("assets/title-animation.belm", FileChangeStatus.Added),
            new("assets/old-caption.belm", FileChangeStatus.Deleted)
        ];
        var service = new Mock<IProjectVersionControlService>();
        service.SetupGet(x => x.Repository).Returns(new RepositoryInfo(root, root));
        service.Setup(x => x.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitAvailability(GitAvailabilityState.Installed, "git", new Version(2, 50), false));
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceStatus("main", 0, 0, files, false));
        service.Setup(x => x.GetHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(commits);
        service.Setup(x => x.GetRemotesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        service.Setup(x => x.GetCommitFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(files);
        service.Setup(x => x.GetDiffAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("diff --git a/scenes/main.scene b/scenes/main.scene\n@@ -1,2 +1,2 @@\n- previous value\n+ updated value\n unchanged");
        var coordinator = new Mock<IProjectVersionControlCoordinator>();
        coordinator.SetReturnsDefault(Task.FromResult<IReadOnlyList<ProjectRecoveryInfo>>([]));
        using var serviceSource = new ReactivePropertySlim<IProjectVersionControlService?>(service.Object);
        using var model = new VersionControlTabViewModel(Mock.Of<ToolTabExtension>(), Mock.Of<IEditorContext>(),
            serviceSource, coordinator.Object, action => action());
        await model.Initialization;
        var view = new VersionControlTabView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 640,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            TextBox input = view.FindControl<TextBox>("CommitMessageTextBox")!;
            SplitButton primary = view.FindControl<SplitButton>("PrimaryActionSplitButton")!;
            Grid layout = view.FindControl<Grid>("TrackedLayoutRoot")!;
            VersionControlChangesView wideChanges = view.FindControl<VersionControlChangesView>("WideChangesView")!;
            double singleLineHeight = input.Bounds.Height;
            AssertLayout();

            input.Text = "Adjust animation timing";
            input.CaretIndex = input.Text.Length;
            Assert.That(input.Focus(), Is.True);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyTextInput("Keep transitions smooth");
            HeadlessTestHelpers.Render(3);
            string draft = input.Text!;
            Assert.That(draft.Replace("\r\n", "\n"), Is.EqualTo("Adjust animation timing\nKeep transitions smooth"));
            Assert.That(model.CommitMessage.Value, Is.EqualTo(draft));
            Assert.That(input.Bounds.Height, Is.GreaterThan(singleLineHeight));
            AssertLayout();
            Capture("multiline");

            input.Text = string.Join('\n', Enumerable.Repeat("A long commit message with multiple lines", 12));
            HeadlessTestHelpers.Render(3);
            Assert.That(input.Bounds.Height, Is.LessThanOrEqualTo(96));
            ScrollViewer messageScroll = input.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.That(messageScroll.Extent.Height, Is.GreaterThan(messageScroll.Viewport.Height));
            AssertLayout();
            input.Text = draft;

            if (width >= 600)
            {
                GridSplitter splitter = layout.Children.OfType<GridSplitter>().Single();
                GridLength originalLeft = layout.ColumnDefinitions[0].Width;
                GridLength originalRight = layout.ColumnDefinitions[2].Width;
                double originalWidth = input.Bounds.Width;
                splitter.Focus();
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                HeadlessTestHelpers.Render(3);
                Assert.That(input.Bounds.Width, Is.GreaterThan(originalWidth));
                AssertLayout();
                foreach (int resizedWidth in new[] { 1100, 500, width })
                {
                    window.Width = resizedWidth;
                    HeadlessTestHelpers.Render(3);
                    AssertLayout();
                    Assert.That(input.Text, Is.EqualTo(draft));
                }
                layout.ColumnDefinitions[0].Width = originalLeft;
                layout.ColumnDefinitions[2].Width = originalRight;
                HeadlessTestHelpers.Render(3);
            }

            VersionControlHistoryView history = view.FindControl<VersionControlHistoryView>(view.IsNarrowLayout ? "NarrowHistoryRoot" : "WideHistoryView")!;
            ListBox list = history.FindControl<ListBox>("CommitList")!;
            var item = (ListBoxItem)list.ContainerFromIndex(0)!;
            TextBlock title = item.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "HistoryMessageText");
            Assert.That(title.TextLayout.TextLines, Has.Count.EqualTo(1));
            Assert.That(item.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text), Does.Not.Contain(commits[0].ShortSha));
            ToolTip.SetIsOpen(title, true);
            HeadlessTestHelpers.Render(3);
            var tip = (StackPanel)ToolTip.GetTip(title)!;
            Assert.That(tip.Children.OfType<TextBlock>().Select(x => x.Text), Does.Contain(message).And.Contain(commits[0].ShortSha));
            Assert.That(tip.Children.OfType<TextBlock>().First().TextLayout.TextLines.Count, Is.GreaterThan(1));
            ToolTip.SetIsOpen(title, false);
            Click(item);
            Assert.That(model.SelectedCommit.Value?.Commit.Sha, Is.EqualTo(commits[0].Sha));
            VersionControlChangesView changes = view.IsNarrowLayout
                ? view.FindControl<VersionControlChangesView>("NarrowChangesView")! : wideChanges;
            ListBox fileList = changes.FindControl<ListBox>("ChangedFileList")!;
            Click((ListBoxItem)fileList.ContainerFromIndex(0)!);
            Assert.That(model.SelectedFile.Value?.Change.Path, Is.EqualTo(files[0].Path));
            foreach (TextBlock text in view.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible))
                Assert.That(text.FontWeight, Is.EqualTo(FontWeight.Normal), text.Text);
            foreach (TextBlock header in view.GetVisualDescendants().OfType<TextBlock>()
                         .Where(x => x.IsEffectivelyVisible && x.Classes.Contains("sectionHeader")))
                Assert.That(((ISolidColorBrush)header.Foreground!).Color,
                    Is.EqualTo(((ISolidColorBrush)input.Foreground!).Color), header.Text);
            Capture("detail");
            if (view.IsNarrowLayout)
            {
                var detailHeader = view.FindControl<VersionControlDetailHeaderView>("NarrowDetailHeader")!;
                Click(detailHeader.FindControl<Button>("BackButton")!);
                Assert.That(model.ShowingDetail.Value, Is.False);
                Assert.That(input.Text, Is.EqualTo(draft));
            }

            void AssertLayout()
            {
                Assert.That(primary.Bounds.Width, Is.EqualTo(input.Bounds.Width).Within(1));
                Assert.That(primary.TranslatePoint(default, view)!.Value.X,
                    Is.EqualTo(input.TranslatePoint(default, view)!.Value.X).Within(1));
                if (view.IsNarrowLayout)
                {
                    Assert.That(input.Bounds.Width, Is.EqualTo(view.Bounds.Width - 16).Within(1));
                    Assert.That(wideChanges.IsVisible, Is.False);
                }
                else
                {
                    var wideHistory = view.FindControl<VersionControlHistoryView>("WideHistoryView")!;
                    ListBox historyList = wideHistory.FindControl<ListBox>("CommitList")!;
                    var row = (ListBoxItem)historyList.ContainerFromIndex(0)!;
                    Assert.That(input.Bounds.Width, Is.EqualTo(row.Bounds.Width).Within(1));
                    Assert.That(input.TranslatePoint(default, view)!.Value.X,
                        Is.EqualTo(row.TranslatePoint(default, view)!.Value.X).Within(1));
                    Assert.That(wideChanges.TranslatePoint(default, layout)!.Value.Y, Is.EqualTo(0).Within(1));
                    Assert.That(wideChanges.Bounds.Height, Is.EqualTo(layout.Bounds.Height).Within(1));
                }
            }
        }
        finally { view.DataContext = null; window.Close(); }

        void Click(Control control)
        {
            control.BringIntoView();
            HeadlessTestHelpers.Render(3);
            using var frame = window.CaptureRenderedFrame();
            Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render(3);
        }

        void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_VERSION_LAYOUT_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}

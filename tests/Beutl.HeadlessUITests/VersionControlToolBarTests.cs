using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.Components.VersionControlTab.Views;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Reactive.Bindings;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlToolBarTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task Status_and_detail_bars_keep_context_visible_and_back_navigation_working(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"version-control-bars-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "version-control-bars", root))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        using var service = new ReactivePropertySlim<IProjectVersionControlService?>();
        using var model = new VersionControlTabViewModel(
            VersionControlTabExtension.Instance,
            TestShell.Editor.SelectedTabItem.Value!.Context.Value,
            service, null, action => action());
        await model.Initialization;
        var view = new VersionControlTabView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 480,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ToolTabBar status = view.FindControl<ToolTabBar>("StatusBar")!;
            Assert.That(status.IsEffectivelyVisible, Is.False);
            model.HasBlockingGuidance.Value = false;
            model.IsTracked.Value = true;
            model.DirtySummary.Value = Strings.VersionControl_WorktreeClean;
            HeadlessTestHelpers.Render();
            TextBlock summary = view.FindControl<TextBlock>("DirtySummaryText")!;
            TextBlock scope = view.FindControl<TextBlock>("RepositoryScopeText")!;
            Assert.Multiple(() =>
            {
                Assert.That(status.IsEffectivelyVisible, Is.True);
                Assert.That(status.Bounds.Height, Is.EqualTo(38));
                Assert.That(summary.Text, Is.EqualTo(model.DirtySummary.Value));
                Assert.That(summary.FontWeight, Is.EqualTo(FontWeight.Normal));
                Assert.That(scope.IsEffectivelyVisible, Is.False);
                Assert.That(view.FindControl<Grid>("CommitComposer")!.TranslatePoint(default, status)!.Value.Y,
                    Is.GreaterThanOrEqualTo(status.Bounds.Height));
            });
            CheckBounds(summary, status);
            Capture("status-clean");

            model.DirtySummary.Value = string.Format(Strings.VersionControl_DirtySummaryFormat, 12345);
            model.IsNestedRepository.Value = true;
            model.RepositoryScopeText.Value = string.Format(Strings.VersionControl_EnclosingRepositoryScopeFormat,
                "/workspace/parent-repository-with-a-long-name/projects/shared-assets");
            HeadlessTestHelpers.Render();
            Assert.That(scope.IsEffectivelyVisible, Is.True);
            Assert.That(summary.Text, Is.EqualTo(model.DirtySummary.Value));
            Assert.That(scope.Text, Is.EqualTo(model.RepositoryScopeText.Value));
            CheckBounds(summary, status);
            CheckBounds(scope, status);
            Capture("status-nested");

            model.HasBlockingGuidance.Value = true;
            HeadlessTestHelpers.Render();
            Assert.That(status.IsEffectivelyVisible, Is.False);
            model.HasBlockingGuidance.Value = false;

            var detail = new VersionControlDetailHeaderView
            {
                DataContext = model,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };
            var formatter = new VersionControlRelativeTimeFormatter(TimeProvider.System, CultureInfo.CurrentUICulture);
            using var shortCommit = new VersionControlCommitViewModel(model,
                new CommitInfo("abcdef1234567890", "abcdef1", "Update project", "Test Author", DateTimeOffset.UtcNow, SnapshotKind.Manual),
                formatter);
            await model.OpenCommitDetailAsync(shortCommit);
            window.Content = detail;
            HeadlessTestHelpers.Render();
            ToolTabBar detailBar = detail.FindControl<ToolTabBar>("DetailBar")!;
            TextBlock message = detail.FindControl<TextBlock>("CommitMessageText")!;
            TextBlock sha = detail.FindControl<TextBlock>("ShortShaText")!;
            Button back = detail.FindControl<Button>("BackButton")!;
            Assert.That(back.Content, Is.TypeOf<FluentIcon>());
            Assert.That(((FluentIcon)back.Content!).Icon, Is.EqualTo(Icon.ArrowLeft));
            Assert.That(AutomationProperties.GetName(back), Is.EqualTo(Strings.Back));
            Assert.That(ToolTip.GetTip(back), Is.EqualTo(Strings.Back));
            Assert.That(message.Text, Is.EqualTo(shortCommit.DisplayMessage));
            Assert.That(sha.Text, Is.EqualTo(shortCommit.Commit.ShortSha));
            Assert.That(message.FontWeight, Is.EqualTo(FontWeight.Normal));
            CheckBounds(back, detailBar);
            CheckBounds(message, detailBar);
            Capture("detail-short");

            using var longCommit = new VersionControlCommitViewModel(model,
                new CommitInfo("1234567890abcdef", "1234567", string.Concat(Enumerable.Repeat("長いコミットメッセージ / long commit message ", 12)),
                    "Test Author", DateTimeOffset.UtcNow, SnapshotKind.Manual), formatter);
            await model.OpenCommitDetailAsync(longCommit);
            HeadlessTestHelpers.Render();
            Assert.That(message.Text, Is.EqualTo(longCommit.DisplayMessage));
            Assert.That(ToolTip.GetTip(message), Is.EqualTo(longCommit.DisplayMessage));
            Assert.That(message.TextLayout.TextLines.Count, Is.EqualTo(1));
            CheckBounds(message, detailBar);
            CheckBounds(sha, detailBar);
            CheckBounds(back, detailBar);
            Capture("detail-long");

            Point center = back.TranslatePoint(new Point(back.Bounds.Width / 2, back.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            Assert.That(model.ShowingDetail.Value, Is.False);
            Assert.That(model.SelectedCommit.Value, Is.SameAs(longCommit));
        }
        finally { window.Close(); }

        void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_VERSION_CONTROL_BAR_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task Repository_adoption_is_answered_inside_the_tab_and_survives_view_detachment(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"adoption-panel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "adoption-panel", root))!;
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().Single());
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var repository = new RepositoryInfo(root, root);
        using var cancellation = new CancellationTokenSource();
        var window = new Window
        {
            Width = width, Height = 640,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            Task<bool> confirmation = TestShell.VersionControl.ConfirmAdoptExistingRepositoryAsync(repository, cancellation.Token);
            await WaitUntilAsync(() => editor.FindToolTab<VersionControlTabViewModel>()?.HasPendingRepositoryAdoption.Value == true);
            VersionControlTabViewModel model = editor.FindToolTab<VersionControlTabViewModel>()!;
            var view = new VersionControlTabView { DataContext = model };
            window.Content = view;
            window.Show();
            await model.Initialization;
            HeadlessTestHelpers.Render();
            Border panel = view.FindControl<Border>("RepositoryAdoptionPanel")!;
            Button accept = view.FindControl<Button>("AcceptRepositoryAdoptionButton")!;
            Button cancel = view.FindControl<Button>("CancelRepositoryAdoptionButton")!;
            Assert.Multiple(() =>
            {
                Assert.That(panel.IsEffectivelyVisible, Is.True);
                Assert.That(view.PromptFlyout.IsOpen, Is.False);
                Border onboarding = view.FindControl<Border>("UntrackedProjectPanel")!;
                Assert.That(onboarding.IsAttachedToVisualTree() && onboarding.IsEffectivelyVisible, Is.False);
                Assert.That(view.FindControl<TextBlock>("RepositoryAdoptionMessage")!.Text,
                    Is.EqualTo(Strings.VersionControl_AdoptExistingRepository));
                Assert.That(view.FindControl<SelectableTextBlock>("RepositoryAdoptionPath")!.Text, Is.EqualTo(root));
                Assert.That(model.CanEnableVersionControl.Value, Is.False);
                Assert.That(confirmation.IsCompleted, Is.False);
            });
            foreach (Button button in new[] { accept, cancel })
            {
                Point position = button.TranslatePoint(default, window)!.Value;
                Assert.That(position.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(position.X + button.Bounds.Width, Is.LessThanOrEqualTo(window.Bounds.Width));
                Assert.That(position.Y + button.Bounds.Height, Is.LessThanOrEqualTo(window.Bounds.Height));
            }
            if (Environment.GetEnvironmentVariable("BEUTL_VERSION_CONTROL_BAR_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(directory, $"adoption-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
            }

            window.Content = new Border();
            HeadlessTestHelpers.Render();
            Assert.That(confirmation.IsCompleted, Is.False);
            window.Content = view;
            HeadlessTestHelpers.Render();
            Click(cancel);
            Assert.That(await confirmation, Is.False);
            await WaitUntilAsync(() => !model.HasPendingRepositoryAdoption.Value);
            HeadlessTestHelpers.Render();
            Assert.That(view.FindControl<Border>("UntrackedProjectPanel")!.IsAttachedToVisualTree(), Is.True);
            Assert.That(view.FindControl<Border>("UntrackedProjectPanel")!.IsEffectivelyVisible, Is.True);

            confirmation = TestShell.VersionControl.ConfirmAdoptExistingRepositoryAsync(repository, cancellation.Token);
            await WaitUntilAsync(() => model.HasPendingRepositoryAdoption.Value);
            Click(accept);
            Assert.That(await confirmation, Is.True);
            await WaitUntilAsync(() => !model.HasPendingRepositoryAdoption.Value);

            confirmation = TestShell.VersionControl.ConfirmAdoptExistingRepositoryAsync(repository, cancellation.Token);
            await WaitUntilAsync(() => model.HasPendingRepositoryAdoption.Value);
            RepositoryAdoptionRequest stale = model.PendingRepositoryAdoption.Value!;
            cancellation.Cancel();
            try
            {
                await confirmation;
                Assert.Fail("The old repository decision must be cancelled.");
            }
            catch (OperationCanceledException) { }
            await WaitUntilAsync(() => !model.HasPendingRepositoryAdoption.Value);
            Assert.That(stale.Respond(true), Is.False);
        }
        finally
        {
            cancellation.Cancel();
            window.Close();
            await TestReset.ResetShellAsync();
        }

        void Click(Button button)
        {
            HeadlessTestHelpers.Render();
            Point point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            HeadlessTestHelpers.Settle();
        }
        Assert.That(condition(), Is.True);
    }

    private static void CheckBounds(Control control, ToolTabBar bar)
    {
        Point position = control.TranslatePoint(default, bar)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(position.X, Is.GreaterThanOrEqualTo(bar.Padding.Left));
            Assert.That(position.Y, Is.GreaterThanOrEqualTo(bar.Padding.Top));
            Assert.That(position.X + control.Bounds.Width, Is.LessThanOrEqualTo(bar.Bounds.Width - bar.Padding.Right));
            Assert.That(position.Y + control.Bounds.Height, Is.LessThanOrEqualTo(bar.Bounds.Height - bar.Padding.Bottom));
        });
    }
}

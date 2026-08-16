using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.Views;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiNarrowLayoutTests
{
    [AvaloniaTest, SetUICulture("ja-JP")]
    public void MainView_TitleBarActionsRemainWithinSixHundredPixels()
    {
        var view = new MainView();
        view.Measure(new Size(600, 420));
        view.Arrange(new Rect(0, 0, 600, 420));

        Control titlebar = view.FindControl<Control>("Titlebar")!;
        Control menu = view.FindControl<Control>("MenuBar")!;
        Control actions = view.FindControl<Control>("TitlebarActions")!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(titlebar.Bounds.Width, Is.LessThanOrEqualTo(600));
            Assert.That(menu.Bounds.Right, Is.LessThanOrEqualTo(actions.Bounds.Left + 1));
            Assert.That(actions.Bounds.Right, Is.LessThanOrEqualTo(titlebar.Bounds.Width + 1));
        }
    }

    [AvaloniaTest]
    public void AiToolViews_NarrowWidthDoesNotRequireHorizontalScrolling()
    {
        UserControl[] views =
        [
            new AiImageEditView(),
            new AiImageGenerationView(),
            new AiSubtitleView(),
            new AiVideoGenerationView(),
        ];

        foreach (UserControl view in views)
        {
            var window = new Window { Content = view, Width = 180, Height = 500 };
            try
            {
                window.Show();
                HeadlessTestHelpers.Render();

                ScrollViewer scrollViewer = (ScrollViewer)view.Content!;
                Assert.That(
                    scrollViewer.Extent.Width,
                    Is.LessThanOrEqualTo(scrollViewer.Viewport.Width + 1),
                    $"{view.GetType().Name} must keep every action reachable without horizontal scrolling.");

                foreach (ProgressBar progressBar in view.GetLogicalDescendants().OfType<ProgressBar>())
                {
                    Assert.That(progressBar.MinWidth, Is.Zero,
                        $"{view.GetType().Name} progress must shrink with its dock pane.");
                }
            }
            finally
            {
                window.Close();
                HeadlessTestHelpers.Settle();
            }
        }
    }

    [AvaloniaTest]
    public void AiJobCenter_NarrowActionsWrapWithinCardsAndConfirmation()
    {
        var view = new AiJobCenterView();

        using (Assert.EnterMultipleScope())
        {
            WrapPanel confirmationActions = view.GetLogicalDescendants()
                .OfType<WrapPanel>()
                .Single(panel => panel.Children.OfType<Button>()
                    .Any(button => AutomationProperties.GetName(button) == Strings.Cancel));
            Assert.That(confirmationActions.Orientation, Is.EqualTo(Orientation.Horizontal));

            WrapPanel footerActions = view.GetLogicalDescendants()
                .OfType<WrapPanel>()
                .Single(panel => panel.Children.OfType<Button>()
                    .Any(button => button.Name == "LoadMoreButton"));
            Assert.That(footerActions.Orientation, Is.EqualTo(Orientation.Horizontal));

            ProgressBar usageProgress = view.GetLogicalDescendants().OfType<ProgressBar>().Single();
            Assert.That(usageProgress.MinWidth, Is.Zero);
        }
    }
}

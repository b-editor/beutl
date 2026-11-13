using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Controls.Styling.Themes;
using Beutl.PackageTools.UI.Models;
using Beutl.PackageTools.UI.ViewModels;
using Beutl.PackageTools.UI.Views;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using PackageToolsMainViewModel = Beutl.PackageTools.UI.ViewModels.MainViewModel;
using PackageToolsStrings = Beutl.PackageTools.UI.Resources.Strings;

namespace Beutl.HeadlessUITests;

// The package tools' part of a restart: the changes and the clean-up start without waiting for Start, and
// the result page asks whether to start Beutl.
[TestFixture]
public class PackageToolsRestartTests
{
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task The_first_page_moves_on_by_itself_only_when_Beutl_restarts(bool restarting)
    {
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        PackageToolsMainViewModel viewModel = CreateViewModel(http, app, _ => initialized.Task, restarting, static () => { });
        (Window window, FAFrame frame, _) = Show(viewModel);

        try
        {
            frame.NavigateFromObject(viewModel);
            HeadlessTestHelpers.Render(3);
            Assert.That(frame.Content, Is.TypeOf<DisplayPackagesPage>(), "Nothing starts before the changes are known.");

            initialized.SetResult();
            await GetInitializationTask(viewModel).WaitAsync(TimeSpan.FromSeconds(5));
            if (restarting)
            {
                await HeadlessTestHelpers.WaitUntilAsync(
                    () => frame.Content is not DisplayPackagesPage,
                    TimeSpan.FromSeconds(5),
                    "A restart must press Start once the changes are known.");
            }
            else
            {
                HeadlessTestHelpers.Render(3);
                Assert.That(frame.Content, Is.TypeOf<DisplayPackagesPage>(), "Without a restart the changes wait for Start.");
            }
        }
        finally
        {
            initialized.TrySetResult();
            await viewModel.DisposeAsync();
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_first_page_stays_when_reading_the_changes_throws()
    {
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        PackageToolsMainViewModel viewModel = CreateViewModel(
            http,
            app,
            static _ => Task.FromException(new IOException("The changes could not be read.")),
            true,
            static () => { });
        (Window window, FAFrame frame, _) = Show(viewModel);

        try
        {
            frame.NavigateFromObject(viewModel);
            HeadlessTestHelpers.Render(3);
            await Assert.ThrowsAsync<IOException>(async () =>
                await GetInitializationTask(viewModel).WaitAsync(TimeSpan.FromSeconds(5)));
            HeadlessTestHelpers.Render(3);

            Assert.That(frame.Content, Is.TypeOf<DisplayPackagesPage>(), "A restart must not press Start for changes it could not read.");
        }
        finally
        {
            await viewModel.DisposeAsync();
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task The_clean_page_runs_by_itself_only_when_Beutl_restarts(bool restarting)
    {
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var clean = new CleanViewModel(app);
        Assume.That(clean.RegistryUnavailable, Is.False, "The clean page offers Start only with a readable package registry.");
        // Other tests share this home, so nothing they left behind is deleted here.
        foreach (CleanPackage item in clean.Items)
        {
            item.Condition.Value = false;
        }

        PackageToolsMainViewModel viewModel = CreateViewModel(http, app, static _ => Task.CompletedTask, restarting, static () => { });
        (Window window, FAFrame frame, _) = Show(viewModel);

        try
        {
            frame.NavigateFromObject(clean);
            HeadlessTestHelpers.Render(3);
            if (restarting)
            {
                await HeadlessTestHelpers.WaitUntilAsync(
                    () => frame.Content is ResultPage,
                    TimeSpan.FromSeconds(10),
                    "A restart must press the clean page's Start.");
                Assert.That(clean.Succeeded.Value, Is.True);
            }
            else
            {
                HeadlessTestHelpers.Render(3);
                Assert.Multiple(() =>
                {
                    Assert.That(frame.Content, Is.TypeOf<CleanPage>());
                    Assert.That(clean.Finished.Value, Is.False, "Without a restart the clean-up waits for Start.");
                });
            }
        }
        finally
        {
            await viewModel.DisposeAsync();
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task The_result_page_starts_Beutl_after_closing_only_when_asked(bool asked)
    {
        int starts = 0;
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        PackageToolsMainViewModel viewModel = CreateViewModel(http, app, static _ => Task.CompletedTask, false, () => starts++);
        (Window window, FAFrame frame, Border buttons) = Show(viewModel);
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        try
        {
            frame.NavigateFromObject(viewModel.Result());
            HeadlessTestHelpers.Render(3);
            buttons.Child = ((ResultPage)frame.Content!).ButtonsContainer;
            HeadlessTestHelpers.Render();

            FATaskDialogButtonHost[] hosts = [.. buttons.GetVisualDescendants().OfType<FATaskDialogButtonHost>()];
            FATaskDialogButtonHost start = hosts.Single(x => Equals(x.Content, PackageToolsStrings.StartBeutl));
            FATaskDialogButtonHost close = hosts.Single(x => Equals(x.Content, PackageToolsStrings.Close));
            Assert.That(start.Classes, Does.Contain("accent"));

            (asked ? start : close).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.True, "Either button closes the package tools.");
                Assert.That(starts, Is.Zero, "Beutl waits until the package tools have released what they hold.");
            });

            await viewModel.DisposeAsync();
            Assert.That(starts, Is.EqualTo(asked ? 1 : 0));
        }
        finally
        {
            await viewModel.DisposeAsync();
            if (!closed)
                window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [Explicit("Produces PNG captures for manual design review; not a regression test.")]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task Capture_result_page(string theme)
    {
        ThemeCaptureTests.UseCaptureTheme(
            theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
            theme == "dark" ? BeutlDarkBorderTheme.AccentColor : null);
        var window = new MainWindow();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();

        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            FAFrame frame = window.FindControl<FAFrame>("frame")!;
            frame.NavigateFromObject(((PackageToolsMainViewModel)window.DataContext!).Result());
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(30);
                HeadlessTestHelpers.Render();
            }

            ThemeCaptureTests.Capture(window, $"package-tools-result-{theme}.png");
        }
        finally
        {
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();
        }
    }

    // Lays the frame out as the package tools' window does, with the page's buttons below it.
    private static (Window Window, FAFrame Frame, Border Buttons) Show(PackageToolsMainViewModel viewModel)
    {
        var frame = new FAFrame { NavigationPageFactory = new MainWindow.MyNavigationPageFactory() };
        var buttons = new Border();
        Grid.SetRow(buttons, 1);
        var window = new Window
        {
            Content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { frame, buttons } },
            DataContext = viewModel,
            Width = 320,
            Height = 450,
        };
        window.Show();
        return (window, frame, buttons);
    }

    // The members that take the parts are internal to Beutl.PackageTools.UI. This assembly cannot see its
    // internals: Beutl compiles some of the same internal types, which would then be visible twice.
    private static PackageToolsMainViewModel CreateViewModel(
        HttpClient http,
        BeutlApiApplication app,
        Func<CancellationToken, Task> initialize,
        bool restarting,
        Action startBeutl)
    {
        ConstructorInfo constructor = typeof(PackageToolsMainViewModel)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(c => c.GetParameters().Length == 10);
        return (PackageToolsMainViewModel)constructor.Invoke(
        [
            http,
            app,
            new ChangesModel(),
            Array.Empty<Process>(),
            Array.Empty<Process>(),
            initialize,
            (Action)(static () => { }),
            (Func<ValueTask>)(static () => ValueTask.CompletedTask),
            restarting,
            startBeutl,
        ]);
    }

    private static Task GetInitializationTask(PackageToolsMainViewModel viewModel)
    {
        return (Task)typeof(PackageToolsMainViewModel)
            .GetProperty("InitializationTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(viewModel)!;
    }
}

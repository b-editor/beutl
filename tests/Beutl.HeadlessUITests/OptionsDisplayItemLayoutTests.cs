using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Pages;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class OptionsDisplayItemLayoutTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Wide_actions_stack_on_resize_without_recreating_the_input(bool light, bool icon)
    {
        var input = new TextBox { MinWidth = 250, Text = "A setting being edited" };
        var row = new OptionsDisplayItem
        {
            Header = "Automatically scroll the timeline during playback",
            Description = "Choose how the timeline follows the current playback position while editing a scene.",
            ActionButton = input,
            Icon = icon ? new FASymbolIcon { Symbol = FASymbol.Settings } : null
        };
        var window = new Window
        {
            Content = row, Width = 640, Height = 220,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        row.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            StackPanel text = Part<StackPanel>(row, "TextHost");
            ContentPresenter action = Part<ContentPresenter>(row, "ActionHost");
            Assert.That(action.Bounds.Top, Is.LessThan(text.Bounds.Bottom));
            input.Focus(NavigationMethod.Tab);
            input.CaretIndex = 7;

            window.Width = 320;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(action.Bounds.Top, Is.GreaterThanOrEqualTo(text.Bounds.Bottom + 8));
                Assert.That(text.Bounds.Width, Is.GreaterThanOrEqualTo(200));
                Assert.That(row.Bounds.Height, Is.LessThan(220));
                Assert.That(input.IsFocused, Is.True);
                Assert.That(input.CaretIndex, Is.EqualTo(7));
                Assert.That(input.Text, Is.EqualTo("A setting being edited"));
            });
            AssertContained(input, row);
            Capture(window, $"row-320-{light}-{icon}");

            window.Width = 640;
            HeadlessTestHelpers.Render();
            Assert.That(action.Bounds.Top, Is.LessThan(text.Bounds.Bottom));
            AssertContained(input, row);
            Capture(window, $"row-640-{light}-{icon}");

            // A small action fits beside the long text when there is no icon column.
            window.Width = 320;
            row.ActionButton = new Button { Content = "Reset" };
            HeadlessTestHelpers.Render();
            Assert.That(action.Bounds.Top >= text.Bounds.Bottom, Is.EqualTo(icon));

            // Short labels need less space and can stay beside the action even with an icon.
            row.Header = "Setting";
            row.Description = null;
            HeadlessTestHelpers.Render();
            Assert.That(action.Bounds.Top, Is.LessThan(text.Bounds.Bottom));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Settings_pages_adapt_to_navigation_and_keep_actions_inside_rows(bool light)
    {
        await TestReset.ResetShellAsync();
        using var model = TestShell.MainViewModel.CreateSettingsDialog();
        var dialog = new SettingsDialog
        {
            DataContext = model, Width = 800, Height = 700,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            model.GoToBrowserSettingsPage();
            dialog.Show();
            HeadlessTestHelpers.Render(3);
            var frame = dialog.FindControl<FAFrame>("frame")!;
            var nav = dialog.FindControl<FANavigationView>("nav")!;
            nav.PaneDisplayMode = FANavigationViewPaneDisplayMode.Left;
            foreach (bool open in new[] { true, false, true })
            {
                nav.IsPaneOpen = open;
                HeadlessTestHelpers.Render(3);
                CheckRows((Control)frame.Content!);
                Capture(dialog, $"browser-800-nav-{open}-{light}");
            }

            frame.Navigate(typeof(EditorSettingsPage), model.Editor, new FASuppressNavigationTransitionInfo());
            HeadlessTestHelpers.Render(3);
            CheckRows((Control)frame.Content!);
            Capture(dialog, $"editor-800-nav-open-{light}");

            nav.PaneDisplayMode = FANavigationViewPaneDisplayMode.Auto;
            dialog.Width = 480;
            nav.IsPaneOpen = false;
            HeadlessTestHelpers.Render(3);
            CheckRows((Control)frame.Content!);
            Capture(dialog, $"editor-480-{light}");

            frame.Navigate(typeof(ViewSettingsPage), model.View, new FASuppressNavigationTransitionInfo());
            HeadlessTestHelpers.Render(3);
            CheckRows((Control)frame.Content!);
            Capture(dialog, $"appearance-480-{light}");
        }
        finally { dialog.Close(); }

        static void CheckRows(Control page)
        {
            OptionsDisplayItem[] rows = page.GetVisualDescendants().OfType<OptionsDisplayItem>().ToArray();
            Assert.That(rows, Is.Not.Empty);
            foreach (OptionsDisplayItem row in rows.Where(x => x.ActionButton?.IsVisible == true))
            {
                StackPanel text = Part<StackPanel>(row, "TextHost");
                if (row.Description is string { Length: > 0 })
                    Assert.That(text.Bounds.Width, Is.GreaterThanOrEqualTo(120), $"Description width for {row.Header}");
                AssertContained(row.ActionButton, row);
            }
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Stacked_rows_preserve_expand_and_navigation_actions(bool grouped)
    {
        var expandedContent = new TextBox { Text = "Expanded setting" };
        var row = new OptionsDisplayItem
        {
            Header = "A long setting heading", Description = "Description of the setting",
            ActionButton = new TextBox { MinWidth = 250 }, Expands = true, Content = expandedContent,
            ContentTransition = null
        };
        Control content = grouped
            ? new Border { Classes = { "options-group" }, Child = new StackPanel { Children = { row } } }
            : row;
        var window = new Window { Content = content, Width = 320, Height = 220 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Border separator = Part<Border>(row, "ExpandedSeparator");
            Assert.That(separator.IsVisible, Is.False);
            ClickHeading();
            Assert.That(row.IsExpanded, Is.True);
            ContentPresenter expanded = Part<ContentPresenter>(row, "ExpandedContentPresenter");
            Assert.That(expanded.IsVisible, Is.True);
            Assert.That(separator.IsVisible, Is.EqualTo(grouped));
            if (grouped)
            {
                Assert.That(Part<Border>(row, "LayoutRoot").BorderThickness, Is.EqualTo(default(Thickness)));
                Assert.That(expanded.BorderThickness, Is.EqualTo(default(Thickness)), "Expanding a grouped row must not add another frame.");
                Assert.That(separator.Bounds.Top, Is.EqualTo(Part<Border>(row, "LayoutRoot").Bounds.Bottom));
                Assert.That(separator.Bounds.Height, Is.EqualTo(1));
                Assert.That(expanded.Bounds.Top, Is.EqualTo(separator.Bounds.Bottom));
                AssertContained(row.ActionButton, row);
            }
            ClickHeading();
            Assert.That(row.IsExpanded, Is.False);
            Assert.That(separator.IsVisible, Is.False);
            row.Expands = false;
            row.Navigates = true;
            int requests = 0;
            row.NavigationRequested += (_, _) => requests++;
            ClickHeading();
            Assert.That(requests, Is.EqualTo(1));

            void ClickHeading()
            {
                Point point = Part<StackPanel>(row, "TextHost").TranslatePoint(new Point(10, 10), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                HeadlessTestHelpers.Render();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Only_headers_with_an_action_react_to_hover_and_press(bool grouped, bool light)
    {
        var toggle = new ToggleSwitch { IsChecked = false };
        var row = new OptionsDisplayItem { Header = "Editable setting", ActionButton = toggle, ContentTransition = null };
        var container = new Border { Child = row, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        if (grouped) container.Classes.Add("options-group");
        var window = new Window
        {
            Content = container, Width = 640, Height = 220,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Border header = Part<Border>(row, "LayoutRoot");
            header.Transitions = null;
            window.MouseMove(new Point(639, 219));
            HeadlessTestHelpers.Render();
            var background = header.Background;
            var borderBrush = header.BorderBrush;

            Point heading = header.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(heading);
            AssertUnchanged();
            window.MouseDown(heading, MouseButton.Left);
            AssertUnchanged();
            window.MouseUp(heading, MouseButton.Left);
            Assert.That(toggle.IsChecked, Is.False);
            Click(toggle);
            Assert.That(toggle.IsChecked, Is.True);
            AssertUnchanged();

            var input = new TextBox { MinWidth = 150, Text = "Initial" };
            row.ActionButton = input;
            HeadlessTestHelpers.Render();
            Click(input);
            Assert.That(input.IsFocused, Is.True);
            AssertUnchanged();

            int buttonClicks = 0;
            var button = new Button { Content = "Action" };
            button.Click += (_, _) => buttonClicks++;
            row.ActionButton = button;
            HeadlessTestHelpers.Render();
            Click(button);
            Assert.That(buttonClicks, Is.EqualTo(1));
            AssertUnchanged();

            row.Expands = true;
            window.MouseMove(heading);
            HeadlessTestHelpers.Render();
            Assert.That(header.Background, Is.Not.EqualTo(background));
            var hoverBackground = header.Background;
            window.MouseDown(heading, MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.That(header.Background, Is.Not.EqualTo(hoverBackground));
            window.MouseUp(heading, MouseButton.Left);
            Assert.That(row.IsExpanded, Is.True);

            // Revoking clickability during a press cancels the pending header action.
            window.MouseDown(heading, MouseButton.Left);
            row.Clickable = false;
            window.MouseUp(heading, MouseButton.Left);
            Assert.That(row.IsExpanded, Is.True);
            AssertUnchanged();

            row.Expands = false;
            row.Navigates = true;
            row.Clickable = true;
            int navigationRequests = 0;
            row.NavigationRequested += (_, _) => navigationRequests++;
            Click(header, new Point(10, 10));
            Assert.That(navigationRequests, Is.EqualTo(1));
            Assert.That(header.Background, Is.Not.EqualTo(background));
            row.Navigates = false;
            AssertUnchanged();

            void AssertUnchanged()
            {
                HeadlessTestHelpers.Render();
                Assert.That(header.Background, Is.EqualTo(background));
                Assert.That(header.BorderBrush, Is.EqualTo(borderBrush));
            }

            void Click(Control control, Point? local = null)
            {
                Point point = control.TranslatePoint(local ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                HeadlessTestHelpers.Render();
            }
        }
        finally { window.Close(); }
    }

    private static T Part<T>(OptionsDisplayItem row, string name) where T : Control =>
        row.GetVisualDescendants().OfType<T>().First(x => x.Name == name);

    private static void AssertContained(Control control, OptionsDisplayItem row)
    {
        Point point = control.TranslatePoint(default, row)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(point.X, Is.GreaterThanOrEqualTo(0), $"Left edge for {row.Header}");
            Assert.That(point.X + control.Bounds.Width, Is.LessThanOrEqualTo(row.Bounds.Width + 0.001), $"Right edge for {row.Header}");
            Assert.That(point.Y + control.Bounds.Height, Is.LessThanOrEqualTo(row.Bounds.Height + 0.001), $"Bottom edge for {row.Header}");
        });
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_OPTIONS_LAYOUT_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Testing.Headless;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class EmptyStateAndStatusLabelTests
{
    [AvaloniaTest]
    public void A_part_of_an_empty_state_that_is_not_set_takes_no_space()
    {
        var titleOnly = new EmptyState { Title = "Nothing here" };
        var full = new EmptyState
        {
            Title = "Nothing here",
            Description = "Add something to get started.",
            Icon = new FluentIcon { Icon = Icon.Link },
            Content = new Button { Content = "Add" }
        };
        var panel = new StackPanel { Children = { titleOnly, full } };
        var window = new Window { Content = panel, Width = 400, Height = 500 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(window, nameof(EmptyStateAndStatusLabelTests), "empty-states");

            Assert.Multiple(() =>
            {
                Assert.That(Part(titleOnly, "PART_Title").IsVisible, Is.True);
                Assert.That(Part(titleOnly, "PART_IconBackground").IsVisible, Is.False);
                Assert.That(Part(titleOnly, "PART_Description").IsVisible, Is.False);
                Assert.That(Part(titleOnly, "PART_ContentPresenter").IsVisible, Is.False);
                Assert.That(Part(full, "PART_IconBackground").IsVisible, Is.True);
                Assert.That(Part(full, "PART_Description").IsVisible, Is.True);
                Assert.That(Part(full, "PART_ContentPresenter").IsVisible, Is.True);
                Assert.That(titleOnly.Bounds.Height, Is.LessThan(full.Bounds.Height));
                Assert.That(((Button)full.Content!).IsEffectivelyVisible, Is.True);
                Assert.That(full.GetVisualDescendants().OfType<FluentIcon>().Single().Bounds.Width, Is.GreaterThan(0));
            });
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void A_live_setting_on_the_control_reaches_the_text_it_announces()
    {
        var empty = new EmptyState { Title = "Nothing here" };
        var label = new StatusLabel { Text = "Running" };
        AutomationProperties.SetLiveSetting(empty, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Assertive);
        var window = new Window { Content = new StackPanel { Children = { empty, label } }, Width = 400, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Assert.Multiple(() =>
            {
                Assert.That(AutomationProperties.GetLiveSetting(Part(empty, "PART_Title")), Is.EqualTo(AutomationLiveSetting.Polite));
                Assert.That(AutomationProperties.GetLiveSetting(Part(label, "PART_Text")), Is.EqualTo(AutomationLiveSetting.Assertive));
            });
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Each_kind_of_status_label_has_its_own_colors()
    {
        StatusLabel[] labels =
        [
            .. Enum.GetValues<StatusLabelKind>().Select(kind => new StatusLabel { Kind = kind, Text = kind.ToString() })
        ];
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.AddRange(labels);
        var window = new Window { Content = panel, Width = 400, Height = 100 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(window, nameof(EmptyStateAndStatusLabelTests), "status-labels");

            Assert.Multiple(() =>
            {
                Assert.That(labels.Select(label => ((ISolidColorBrush)label.Foreground!).Color).Distinct().Count(), Is.EqualTo(labels.Length));
                Assert.That(labels.Select(label => Part(label, "PART_Text")).OfType<TextBlock>().Select(text => text.Text),
                    Is.EqualTo(Enum.GetNames<StatusLabelKind>()));
                Assert.That(labels.Select(label => Part(label, "PART_Text")).OfType<TextBlock>().Select(text => text.FontSize), Is.All.EqualTo(10));
            });

            StatusLabel changed = labels[0];
            IBrush? neutral = changed.Foreground;
            changed.Kind = StatusLabelKind.Critical;
            HeadlessTestHelpers.Render();
            Assert.That(changed.Foreground, Is.Not.SameAs(neutral));
            changed.Kind = StatusLabelKind.Neutral;
            HeadlessTestHelpers.Render();
            Assert.That(changed.Foreground, Is.SameAs(neutral));
        }
        finally { window.Close(); }
    }

    private static Control Part(Control control, string name)
        => control.GetVisualDescendants().OfType<Control>().First(child => child.Name == name);
}

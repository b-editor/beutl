using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class Avalonia12NavigationTests
{
    [AvaloniaTest]
    public void Navigation_item_switches_between_regular_and_selected_icons()
    {
        var regular = new FAFontIconSource { Glyph = "R" };
        var filled = new FluentIconSource { Icon = Icon.Settings };
        var behavior = new NavItemHelper { RegularIcon = regular, FilledIcon = filled };
        var item = new FANavigationViewItem { Content = "Settings" };
        Interaction.GetBehaviors(item).Add(behavior);
        try
        {
            Assert.That(item.IconSource, Is.SameAs(regular));
            item.IsSelected = true;
            Assert.That(item.IconSource, Is.SameAs(filled));
            item.IsSelected = false;
            Assert.Multiple(() =>
            {
                Assert.That(item.IconSource, Is.SameAs(regular));
                Assert.That(regular.FontSize, Is.EqualTo(48));
                Assert.That(filled.FontSize, Is.EqualTo(48));
            });
        }
        finally
        {
            Interaction.GetBehaviors(item).Remove(behavior);
        }
    }

    private sealed record Context(string Name);

    public sealed class Page : UserControl;
}

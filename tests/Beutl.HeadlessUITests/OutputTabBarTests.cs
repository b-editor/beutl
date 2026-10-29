using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.OutputTab.ViewModels;
using Beutl.Editor.Components.OutputTab.Views;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class OutputTabBarTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task Profile_bar_stays_separate_from_encoding_content(int width, bool light)
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("output-tab-bar");
        using var model = new OutputTabViewModel(editor);
        var view = new OutputTabView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 600,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ToolTabBar bar = view.FindControl<ToolTabBar>("ToolBar")!;
            ContentControl content = view.FindControl<ContentControl>("contentControl")!;
            DropDownButton profiles = view.FindControl<DropDownButton>("ProfilesButton")!;
            Button add = view.FindControl<Button>("AddProfileButton")!;
            Button more = view.FindControl<Button>("MoreButton")!;
            Border profileBorder = profiles.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RootBorder");
            Assert.That(profileBorder.BorderThickness, Is.EqualTo(default(Thickness)));
            Assert.That(((ISolidColorBrush)profileBorder.Background!).Color.A, Is.Zero);
            Assert.Multiple(() =>
            {
                Assert.That(bar.GetLogicalDescendants().OfType<ProgressBar>(), Is.Empty);
                Assert.That(bar.GetLogicalDescendants().OfType<OutputView>(), Is.Empty);
                Assert.That(bar.GetLogicalDescendants().OfType<Button>().Select(b => b.Content),
                    Does.Not.Contain(Strings.Encode).And.Not.Contain(Strings.Cancel));
                Assert.That(content.TranslatePoint(default, view)!.Value.Y,
                    Is.GreaterThanOrEqualTo(bar.TranslatePoint(default, view)!.Value.Y + bar.Bounds.Height));
            });

            OutputView output = content.GetVisualDescendants().OfType<OutputView>().Single();
            Assert.That(output.GetLogicalDescendants().OfType<ProgressBar>(), Is.Not.Empty);
            ToolTabBar sectionBar = output.GetLogicalDescendants().OfType<ToolTabBar>().Single();
            Assert.That(sectionBar.GetLogicalDescendants().OfType<TabStrip>().Single(),
                Is.SameAs(output.FindControl<TabStrip>("SectionStrip")));
            Assert.That(sectionBar.GetLogicalDescendants().OfType<ProgressBar>(), Is.Empty);
            Assert.That(sectionBar.GetLogicalDescendants().OfType<Button>(), Is.Empty);
            Assert.That(sectionBar.TranslatePoint(default, view)!.Value.Y,
                Is.GreaterThanOrEqualTo(bar.TranslatePoint(default, view)!.Value.Y + bar.Bounds.Height));
            Assert.That(output.GetLogicalDescendants().OfType<Button>().Select(b => b.Content),
                Does.Contain(Strings.Encode).And.Contain(Strings.Cancel));

            int previousCount = model.Items.Count;
            HeadlessTestHelpers.Click(add);
            Assert.That(model.Items.Count, Is.EqualTo(previousCount + 1));
            string name = new('P', 160);
            model.SelectedItem.Value!.Context.Name.Value = name;
            HeadlessTestHelpers.Render();
            Assert.That(((TextBlock)profiles.Content!).Text, Is.EqualTo(name));
            foreach (Button button in new Button[] { profiles, add, more })
            {
                Point position = button.TranslatePoint(default, bar)!.Value;
                Assert.That(position.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(position.X + button.Bounds.Width, Is.LessThanOrEqualTo(bar.Bounds.Width));
            }
            HeadlessTestHelpers.Click(more);
            Assert.That(more.Flyout!.IsOpen, Is.True);
            more.Flyout.Hide();

            Point profileCenter = profiles.TranslatePoint(new Point(profiles.Bounds.Width / 2, profiles.Bounds.Height / 2), window)!.Value;
            window.MouseMove(profileCenter);
            HeadlessTestHelpers.Render();
            Assert.That(((ISolidColorBrush)profileBorder.Background!).Color.A, Is.Zero);
            HeadlessTestHelpers.Click(profiles);
            var popup = window.OpenedPopups.Single(popup => popup.Child is OutputPickerFlyoutPresenter);
            var presenter = (OutputPickerFlyoutPresenter)popup.Child!;
            HeadlessTestHelpers.Click(presenter.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ProfilesTabButton"));
            Assert.That(presenter.ShowPresets, Is.False);
            var profileList = presenter.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "PART_ProfileListBox");
            profileList.SelectedItem = presenter.ProfileItems!.Single(item => ReferenceEquals(item.UserData, model.Items[0]));
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Click(presenter.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AcceptButton"));
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(model.SelectedItem.Value, Is.SameAs(model.Items[0]));
        }
        finally { window.Close(); }
    }
}

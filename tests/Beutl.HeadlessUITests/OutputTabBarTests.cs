using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
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
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"output-tab-bar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "output-tab-bar", root))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        using var model = new OutputTabViewModel(editor);
        var view = new OutputTab { DataContext = model };
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
            Assert.That(output.GetLogicalDescendants().OfType<ToolTabBar>(), Is.Empty);
            Assert.That(output.GetLogicalDescendants().OfType<Button>().Select(b => b.Content),
                Does.Contain(Strings.Encode).And.Contain(Strings.Cancel));

            // Capture only the default profile without loading native encoder-settings views.
            if (Environment.GetEnvironmentVariable("BEUTL_OUTPUT_TAB_CAPTURE") is { Length: > 0 } directory)
            {
                Assert.That(((OutputViewModel)output.DataContext!).SelectedEncoder.Value, Is.Null);
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(directory, $"output-tab-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
            }

            int previousCount = model.Items.Count;
            Click(add);
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
            Click(more);
            Assert.That(more.Flyout!.IsOpen, Is.True);
            more.Flyout.Hide();

            Point profileCenter = profiles.TranslatePoint(new Point(profiles.Bounds.Width / 2, profiles.Bounds.Height / 2), window)!.Value;
            window.MouseMove(profileCenter);
            HeadlessTestHelpers.Render();
            Assert.That(((ISolidColorBrush)profileBorder.Background!).Color.A, Is.Zero);
            Click(profiles);
            var picker = (OutputPickerFlyout)typeof(OutputTab)
                .GetField("_activeFlyout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(view)!;
            Assert.That(picker.IsOpen, Is.True);
            var presenter = (OutputPickerFlyoutPresenter)picker.Popup.Child!;
            Click(presenter.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ProfilesTabButton"));
            Assert.That(picker.ViewModel.ShowPresets.Value, Is.False);
            var profileList = presenter.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "PART_ProfileListBox");
            profileList.SelectedItem = presenter.ProfileItems!.Single(item => ReferenceEquals(item.UserData, model.Items[0]));
            HeadlessTestHelpers.Settle();
            Click(presenter.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AcceptButton"));
            Assert.That(picker.IsOpen, Is.False);
            Assert.That(model.SelectedItem.Value, Is.SameAs(model.Items[0]));
        }
        finally { window.Close(); }

        void Click(Button button)
        {
            TopLevel topLevel = TopLevel.GetTopLevel(button)!;
            Point center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), topLevel)!.Value;
            topLevel.MouseDown(center, MouseButton.Left);
            topLevel.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Render();
        }
    }
}

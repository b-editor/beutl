using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class EditorExtensionPriorityPageTests
{
    [AvaloniaTest]
    public void Priority_and_available_editor_rows_display_their_command_bar_actions()
    {
        using var viewModel = new EditorExtensionPriorityPageViewModel(new ExtensionProvider());
        // Populate both row templates without modifying the persisted extension priorities.
        viewModel.SelectedFileExtension.Value = null;
        viewModel.EditorExtensions1.Add(new("Preferred editor", "preferred", "PreferredEditor"));
        viewModel.EditorExtensions2.Add(new("Available editor", "available", "AvailableEditor"));
        var view = new EditorExtensionPriorityPage { DataContext = viewModel };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ListBox[] lists = view.GetVisualDescendants().OfType<ListBox>().ToArray();
            Assert.That(lists, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(lists[0].GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == "Preferred editor"), Is.True);
                Assert.That(lists[1].GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == "Available editor"), Is.True);
                Assert.That(lists[0].GetVisualDescendants().OfType<FACommandBarButton>().Count(), Is.EqualTo(3));
                Assert.That(lists[1].GetVisualDescendants().OfType<FACommandBarButton>().Count(), Is.EqualTo(1));
            });

            FACommandBar bar = view.GetVisualDescendants().OfType<FACommandBar>().Single();
            Assert.That(bar.PrimaryCommands.OfType<FACommandBarButton>().Single().IconSource, Is.Not.Null);
            foreach (FACommandBarButton button in lists.SelectMany(list => list.GetVisualDescendants())
                         .OfType<FACommandBarButton>())
            {
                Assert.Multiple(() =>
                {
                    Assert.That(button.IconSource, Is.Not.Null, button.Label);
                    Assert.That(button.Label, Is.Not.Null.And.Not.Empty);
                    Assert.That(button.CommandParameter,
                        Is.InstanceOf<EditorExtensionPriorityPageViewModel.EditorExtensionWrapper>());
                });
            }
        }
        finally
        {
            view.DataContext = null;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}

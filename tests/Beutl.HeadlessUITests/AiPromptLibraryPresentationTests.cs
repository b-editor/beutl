using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiPromptLibraryPresentationTests
{
    private string _storageDirectory = string.Empty;

    [SetUp]
    public void SetUp()
        => _storageDirectory = Path.Combine(
            Path.GetTempPath(),
            $"beutl-prompt-presentation-{Guid.NewGuid():N}");

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_storageDirectory))
        {
            Directory.Delete(_storageDirectory, true);
        }
    }

    [AvaloniaTest]
    public void Templates_ListEachSavedPromptWithItsOwnPinAndDeleteButtons()
    {
        var library = CreateLibrary();
        library.SaveTemplate("Sunset", PromptTaskKind.Image, "a calm sunset");
        library.SaveTemplate("Neon", PromptTaskKind.Image, "a neon city");
        string? applied = null;
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            prompt => applied = prompt,
            library);
        var view = new AiPromptTemplatesView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 340, Height = 400 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ListBox list = view.GetLogicalDescendants().OfType<ListBox>().Single();
            List<ListBoxItem> rows = list.GetRealizedContainers().OfType<ListBoxItem>().ToList();
            Assert.That(rows, Has.Count.EqualTo(2), "Every saved template stays on screen.");

            ListBoxItem row = FindRow(list, "Sunset");
            Button apply = Apply(list, "Sunset");
            ToggleButton pin = Pin(list, "Sunset");
            Button delete = Delete(list, "Sunset");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    row.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text),
                    Does.Contain("Sunset"),
                    "The name is text; applying is one of the row's buttons.");
                Assert.That(pin.IsChecked, Is.False, "A template starts unpinned.");
                Assert.That(pin.Command, Is.SameAs(viewModel.TogglePin));
                Assert.That(delete.Command, Is.SameAs(viewModel.Delete));
                Assert.That(apply.Command, Is.SameAs(viewModel.Apply));
                Assert.That(apply.CommandParameter, Is.SameAs(row.DataContext));
            }

            Invoke(apply);
            Assert.That(applied, Is.EqualTo("a calm sunset"),
                "Clicking a template applies it rather than only selecting it.");

            Invoke(pin);
            HeadlessTestHelpers.Render();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    viewModel.Templates.Single(choice => choice.Name == "Sunset").IsPinned,
                    Is.True);
                Assert.That(
                    viewModel.Templates.Select(choice => choice.Name),
                    Is.EqualTo(new[] { "Sunset", "Neon" }),
                    "Pinning moves the template to the top of its own list.");
            }

            // The rows follow the reordered list, so the button has to be found again
            // rather than reused from the container that has since been recycled.
            Invoke(Delete(list, "Neon"));
            Assert.That(viewModel.Templates.Select(choice => choice.Name), Is.EqualTo(new[] { "Sunset" }));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Templates_KeepTheRowActionsOutOfTheWayUntilTheRowIsReachedFor()
    {
        var library = CreateLibrary();
        library.SaveTemplate("Sunset", PromptTaskKind.Image, "a calm sunset");
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            _ => { },
            library);
        var view = new AiPromptTemplatesView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 340, Height = 400 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ListBox list = view.GetLogicalDescendants().OfType<ListBox>().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Apply(list, "Sunset").Opacity, Is.Zero);
                Assert.That(Pin(list, "Sunset").Opacity, Is.Zero);
                Assert.That(Delete(list, "Sunset").Opacity, Is.Zero,
                    "The actions stay out of a list that is read far more often than it is edited.");
                Assert.That(
                    FindRow(list, "Sunset").GetVisualDescendants().OfType<TextBlock>()
                        .Single(text => text.Text == "Sunset").Opacity,
                    Is.EqualTo(1),
                    "The name is the row, so it is always readable.");
            }

            list.SelectedItem = FindRow(list, "Sunset").DataContext;
            HeadlessTestHelpers.Render();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Apply(list, "Sunset").Opacity, Is.EqualTo(1));
                Assert.That(Pin(list, "Sunset").Opacity, Is.EqualTo(1));
                Assert.That(Delete(list, "Sunset").Opacity, Is.EqualTo(1));
            }

            Invoke(Pin(list, "Sunset"));
            list.SelectedItem = null;
            HeadlessTestHelpers.Render();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Pin(list, "Sunset").Opacity, Is.EqualTo(1),
                    "A pinned row says so without being reached for: the pin is state, not just an action.");
                Assert.That(Apply(list, "Sunset").Opacity, Is.Zero);
                Assert.That(Delete(list, "Sunset").Opacity, Is.Zero);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void HistoryButton_KeepsThePastPromptsBehindAPopupRatherThanInThePanel()
    {
        var library = CreateLibrary();
        library.Record(PromptTaskKind.Image, "an earlier prompt");
        string? applied = null;
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            prompt => applied = prompt,
            library);
        var view = new AiPromptHistoryButton { DataContext = viewModel };
        var window = new Window { Content = view, Width = 340, Height = 400 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ToggleButton toggle = view.GetLogicalDescendants().OfType<ToggleButton>().Single();
            Popup popup = view.GetLogicalDescendants().OfType<Popup>().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(popup.IsOpen, Is.False, "The history stays out of the way until asked for.");
                Assert.That(toggle.Bounds.Width, Is.LessThanOrEqualTo(40),
                    "The button is small enough to sit above the prompt box.");
            }

            toggle.IsChecked = true;
            HeadlessTestHelpers.Render();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(viewModel.IsHistoryOpen.Value, Is.True);
                Assert.That(popup.IsOpen, Is.True);
            }

            ListBox list = popup.Child!.GetLogicalDescendants().OfType<ListBox>().Single();

            Invoke(Apply(list, "an earlier prompt"));
            HeadlessTestHelpers.Render();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(applied, Is.EqualTo("an earlier prompt"));
                Assert.That(viewModel.IsHistoryOpen.Value, Is.False);
                Assert.That(toggle.IsChecked, Is.False, "The button reflects the popup it drives.");
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_PutsTheHistoryButtonAboveTheRightEdgeOfThePromptBox()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 420, Height = 800 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            AiPromptHistoryButton button = view.GetLogicalDescendants()
                .OfType<AiPromptHistoryButton>()
                .Single();
            TextBox prompt = view.GetLogicalDescendants()
                .OfType<TextBox>()
                .First(box => box.AcceptsReturn);
            Point buttonOrigin = button.TranslatePoint(default, view) ?? default;
            Point promptOrigin = prompt.TranslatePoint(default, view) ?? default;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(buttonOrigin.Y + button.Bounds.Height, Is.LessThanOrEqualTo(promptOrigin.Y + 1),
                    "The button sits above the box, not inside the panel below it.");
                Assert.That(
                    buttonOrigin.X + button.Bounds.Width,
                    Is.GreaterThanOrEqualTo(promptOrigin.X + prompt.Bounds.Width - 1),
                    "It is aligned with the right edge of the box it belongs to.");
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private PersistentPromptLibrary CreateLibrary()
        => new(Path.Combine(_storageDirectory, "prompts.json"));

    private static Button Apply(ListBox list, string name)
        => RowAction(list, name, Strings.AiPromptApply);

    private static ToggleButton Pin(ListBox list, string name)
        => FindRow(list, name).GetVisualDescendants().OfType<ToggleButton>().Single();

    private static Button Delete(ListBox list, string name)
        => RowAction(list, name, Strings.Delete);

    private static Button RowAction(ListBox list, string name, string action)
        => FindRow(list, name).GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == action);

    private static ListBoxItem FindRow(ListBox list, string name)
        => list.GetRealizedContainers()
            .OfType<ListBoxItem>()
            .Single(item => ((AiPromptChoice)item.DataContext!).Name == name);

    private static void Invoke(Button button)
    {
        ICommand command = button.Command!;
        Assert.That(command.CanExecute(button.CommandParameter), Is.True);
        command.Execute(button.CommandParameter);
    }

    private static AiImageGenerationDialogViewModel CreateImageGenerationDialog(BeutlApiApplication clients)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiImageGenerationService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            editViewModel: null,
            requestRecoveryContext: AiRetryTestContext.CreateForm());
}

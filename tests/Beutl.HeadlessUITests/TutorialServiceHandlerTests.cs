using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Services.Tutorials;
using Beutl.Views.Tutorial;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TutorialServiceHandlerTests
{
    [Test]
    public void GetAvailableTutorials_returns_a_copy_that_later_changes_leave_unchanged()
    {
        var service = new TutorialServiceHandler();
        service.Register(Tutorial("first"));
        IReadOnlyList<TutorialDefinition> before = service.GetAvailableTutorials();

        service.Register(Tutorial("second"));
        service.Unregister("first");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Select(tutorial => tutorial.Id), Is.EqualTo(new[] { "first" }));
            Assert.That(service.GetAvailableTutorials().Select(tutorial => tutorial.Id), Is.EqualTo(new[] { "second" }));
        }
    }

    [Test]
    public void An_extension_registers_and_unregisters_only_in_its_host_service()
    {
        var first = new TutorialServiceHandler();
        var second = new TutorialServiceHandler();
        using var services = new Beutl.Editor.Services.EditorServiceRegistry()
            .Add<ITutorialService>(() => first);
        var extension = new TestTutorialExtension { Services = services };

        extension.Load();
        Assert.Multiple(() =>
        {
            Assert.That(first.GetAvailableTutorials().Select(tutorial => tutorial.Id), Is.EqualTo(new[] { "extension-tutorial" }));
            Assert.That(second.GetAvailableTutorials(), Is.Empty);
        });
        extension.Unload();
        Assert.That(first.GetAvailableTutorials(), Is.Empty);
    }

    private sealed class TestTutorialExtension : Beutl.Extensibility.TutorialExtension
    {
        public override IReadOnlyList<TutorialDefinition> GetTutorials() => [Tutorial("extension-tutorial")];
    }

    [AvaloniaTest]
    public void The_tutorial_dialog_loads_its_XAML_and_lists_the_supplied_service()
    {
        var service = new TutorialServiceHandler();
        service.Register(Tutorial("dialog-tutorial"));
        var dialog = new TutorialListDialog(service);

        Assert.That(dialog.FindControl<ItemsControl>("TutorialList")!.ItemsSource,
            Is.EqualTo(service.GetAvailableTutorials()));
    }

    private static TutorialDefinition Tutorial(string id) => new() { Id = id, Title = id, Description = id, Steps = [] };
}

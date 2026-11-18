using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Services.Tutorials;
using Beutl.Views.Tutorial;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TutorialServiceHandlerTests
{
    [Test]
    public void GetAvailableTutorials_returns_a_copy_that_later_changes_leave_unchanged()
    {
        var service = new TutorialServiceHandler(new Beutl.Configuration.TutorialConfig());
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
        var first = new TutorialServiceHandler(new Beutl.Configuration.TutorialConfig());
        var second = new TutorialServiceHandler(new Beutl.Configuration.TutorialConfig());
        using var firstServices = new EditorServiceRegistry().Add<ITutorialService>(() => first);
        using var secondServices = new EditorServiceRegistry().Add<ITutorialService>(() => second);
        var firstContext = new ExtensionContext(firstServices);
        var secondContext = new ExtensionContext(secondServices);
        var extension = new TestTutorialExtension();

        extension.Load(firstContext);
        extension.Load(secondContext);
        Assert.That(first.GetAvailableTutorials().Single().Id, Is.EqualTo("extension-tutorial"));
        Assert.That(second.GetAvailableTutorials().Single().Id, Is.EqualTo("extension-tutorial"));

        extension.Unload(firstContext);
        extension.Unload(firstContext);
        Assert.That(first.GetAvailableTutorials(), Is.Empty);
        Assert.That(second.GetAvailableTutorials().Single().Id, Is.EqualTo("extension-tutorial"));

        extension.Unload(secondContext);
        Assert.That(second.GetAvailableTutorials(), Is.Empty);
    }

    private sealed class TestTutorialExtension : Beutl.Extensibility.TutorialExtension
    {
        public override IReadOnlyList<TutorialDefinition> GetTutorials() => [Tutorial("extension-tutorial")];
    }

    [AvaloniaTest]
    public void The_tutorial_dialog_loads_its_XAML_and_lists_the_supplied_service()
    {
        var service = new TutorialServiceHandler(new Beutl.Configuration.TutorialConfig());
        service.Register(Tutorial("dialog-tutorial"));
        var dialog = new TutorialListDialog(service);

        Assert.That(dialog.FindControl<ItemsControl>("TutorialList")!.ItemsSource,
            Is.EqualTo(service.GetAvailableTutorials()));
    }

    private static TutorialDefinition Tutorial(string id) => new() { Id = id, Title = id, Description = id, Steps = [] };
}

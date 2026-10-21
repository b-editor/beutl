using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Transitions;
using Beutl.Helpers;
using Beutl.NodeGraph;
using Beutl.Services;
using Beutl.Services.Tutorials;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// TestApp calls App.RegisterProcessServices, so the UI tests run with the app's process-wide hooks.
[TestFixture]
public sealed class ProcessServicesTests
{
    [AvaloniaTest]
    public void The_test_app_has_the_process_services_of_the_app()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PropertyEditorExtension.DefaultHandler, Is.InstanceOf<PropertyEditorService.PropertyEditorExtensionImpl>());
            Assert.That(NotificationService.Handler, Is.InstanceOf<NotificationServiceHandler>());
            Assert.That(TutorialService.Current, Is.InstanceOf<TutorialServiceHandler>());
            Assert.That(AppHelper.GetContextCommandManager?.Invoke(), Is.SameAs(TestShell.MainViewModel.ContextCommandManager));
            Assert.That(LibraryService.Current.FindItem(typeof(CrossDissolveTransition)), Is.Not.Null);
            Assert.That(GraphNodeRegistry.GetRegistered(), Is.Not.Empty);
            Assert.That(ReactivePropertyScheduler.Default, Is.SameAs(UiThreadScheduler.Instance));
        });
    }
}

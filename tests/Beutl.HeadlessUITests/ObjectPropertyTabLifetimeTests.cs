using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ObjectPropertyTabLifetimeTests
{
    // Each editor listens to its object and to the clock, so one left behind goes on working for a closed tab.
    [AvaloniaTest]
    public void Disposing_the_tab_disposes_the_editors_it_keeps_for_going_back()
    {
        var created = new List<Mock<IPropertiesEditorViewModel>>();
        var factory = new Mock<IPropertiesEditorFactory>();
        factory.Setup(x => x.Create(It.IsAny<ICoreObject>())).Returns((ICoreObject target) =>
        {
            var editor = new Mock<IPropertiesEditorViewModel>();
            editor.SetupGet(x => x.Target).Returns(target);
            editor.SetupGet(x => x.Properties).Returns([]);
            created.Add(editor);
            return editor.Object;
        });
        var context = new Mock<IEditorContext>();
        context.Setup(x => x.GetService(typeof(IPropertiesEditorFactory))).Returns(factory.Object);
        var tab = new ObjectPropertyTabViewModel(context.Object);
        tab.NavigateCore(new RectShape(), false, null);
        tab.NavigateCore(new RectShape(), false, null);

        tab.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(tab.ChildContext.Value, Is.Null);
            foreach (Mock<IPropertiesEditorViewModel> editor in created)
                editor.Verify(x => x.Dispose(), Times.Once);
        });
    }
}

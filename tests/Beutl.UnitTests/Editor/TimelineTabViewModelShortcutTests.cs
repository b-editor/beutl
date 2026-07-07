using Avalonia.Controls;
using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TimelineTabViewModelShortcutTests
{
    [Test]
    public void IsTextInputSource_ReturnsTrue_ForTextBox()
    {
        Assert.That(TimelineTabViewModel.IsTextInputSource(new TextBox()), Is.True);
    }

    [Test]
    public void IsTextInputSource_ReturnsFalse_ForNonTextVisual()
    {
        Assert.That(TimelineTabViewModel.IsTextInputSource(new Border()), Is.False);
    }

    [Test]
    public void IsTextInputSource_ReturnsFalse_ForNull()
    {
        Assert.That(TimelineTabViewModel.IsTextInputSource(null), Is.False);
    }
}

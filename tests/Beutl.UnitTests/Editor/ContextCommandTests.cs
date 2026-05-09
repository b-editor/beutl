using System.Runtime.InteropServices;
using Beutl.Extensibility;

namespace Beutl.UnitTests.Editor;

public class ContextCommandTests
{
    [Test]
    public void ContextCommandExecution_StoresName()
    {
        var exec = new ContextCommandExecution("Save");
        Assert.That(exec.CommandName, Is.EqualTo("Save"));
        Assert.That(exec.KeyEventArgs, Is.Null);
    }

    [Test]
    public void ContextCommandAttribute_NameIsSettable()
    {
        var attr = new ContextCommandAttribute { Name = "MyCmd" };
        Assert.That(attr.Name, Is.EqualTo("MyCmd"));
    }

    [Test]
    public void ContextCommandKeyGesture_StoresValues()
    {
        var g = new ContextCommandKeyGesture("Ctrl+S", OSPlatform.Windows);
        Assert.That(g.KeyGesture, Is.EqualTo("Ctrl+S"));
        Assert.That(g.Platform, Is.EqualTo(OSPlatform.Windows));
    }

    [Test]
    public void ContextCommandDefinition_StoresAllFields()
    {
        var win = new ContextCommandKeyGesture("Ctrl+S", OSPlatform.Windows);
        var def = new ContextCommandDefinition("Save", "Save File", "Saves current document", [win]);

        Assert.That(def.Name, Is.EqualTo("Save"));
        Assert.That(def.DisplayName, Is.EqualTo("Save File"));
        Assert.That(def.Description, Is.EqualTo("Saves current document"));
        Assert.That(def.KeyGestures, Is.Not.Null);
        Assert.That(def.KeyGestures, Has.Length.EqualTo(1));
    }

    [Test]
    public void ContextCommandDefinition_NullGestures_StaysNull()
    {
        var def = new ContextCommandDefinition("Cmd");
        Assert.That(def.KeyGestures, Is.Null);
    }

    [Test]
    public void ContextCommandDefinition_FallbackGesture_FillsAllPlatforms()
    {
        var fallback = new ContextCommandKeyGesture("Ctrl+S");
        var def = new ContextCommandDefinition("Save", keyGestures: [fallback]);

        Assert.That(def.KeyGestures, Is.Not.Null);
        Assert.That(def.KeyGestures, Has.Length.EqualTo(3));

        var platforms = def.KeyGestures!.Select(g => g.Platform).ToArray();
        Assert.That(platforms, Does.Contain(OSPlatform.Windows));
        Assert.That(platforms, Does.Contain(OSPlatform.Linux));
        Assert.That(platforms, Does.Contain(OSPlatform.OSX));
    }

    [Test]
    public void ContextCommandDefinition_PerPlatformGestures_PreserveValues()
    {
        var win = new ContextCommandKeyGesture("Ctrl+S", OSPlatform.Windows);
        var mac = new ContextCommandKeyGesture("Cmd+S", OSPlatform.OSX);
        var def = new ContextCommandDefinition("Save", keyGestures: [win, mac]);

        Assert.That(def.KeyGestures, Has.Length.EqualTo(2));
        Assert.That(def.KeyGestures!.Any(g => g.Platform == OSPlatform.Windows && g.KeyGesture == "Ctrl+S"));
        Assert.That(def.KeyGestures!.Any(g => g.Platform == OSPlatform.OSX && g.KeyGesture == "Cmd+S"));
    }

    [Test]
    public void ContextCommandDefinition_FallbackPlusOverride_OverrideWins()
    {
        var fallback = new ContextCommandKeyGesture("Ctrl+S");
        var mac = new ContextCommandKeyGesture("Cmd+S", OSPlatform.OSX);
        var def = new ContextCommandDefinition("Save", keyGestures: [fallback, mac]);

        var macGesture = def.KeyGestures!.Single(g => g.Platform == OSPlatform.OSX);
        Assert.That(macGesture.KeyGesture, Is.EqualTo("Cmd+S"));
    }
}

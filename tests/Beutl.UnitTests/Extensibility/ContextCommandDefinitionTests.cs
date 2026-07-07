using System.Runtime.InteropServices;

using Beutl.Extensibility;

namespace Beutl.UnitTests.Extensibility;

[TestFixture]
public class ContextCommandDefinitionTests
{
    private static readonly OSPlatform[] s_platforms = [OSPlatform.Windows, OSPlatform.Linux, OSPlatform.OSX];

    [Test]
    public void Normalize_MultiplePlatformlessGestures_KeepsAllPerPlatform()
    {
        var def = new ContextCommandDefinition(
            "ExitTool",
            keyGestures: [new ContextCommandKeyGesture("V"), new ContextCommandKeyGesture("Escape")]);

        Assert.That(def.KeyGestures, Is.Not.Null);
        foreach (OSPlatform platform in s_platforms)
        {
            string?[] keys = def.KeyGestures!.Where(g => g.Platform == platform).Select(g => g.KeyGesture).ToArray();
            Assert.That(keys, Is.EquivalentTo(new[] { "V", "Escape" }), $"platform {platform}");
        }
    }

    [Test]
    public void Normalize_SinglePlatformlessGesture_ExpandsToEachPlatform()
    {
        var def = new ContextCommandDefinition("Toggle", keyGestures: [new ContextCommandKeyGesture("C")]);

        foreach (OSPlatform platform in s_platforms)
        {
            string?[] keys = def.KeyGestures!.Where(g => g.Platform == platform).Select(g => g.KeyGesture).ToArray();
            Assert.That(keys, Is.EquivalentTo(new[] { "C" }), $"platform {platform}");
        }
    }

    [Test]
    public void Normalize_ExplicitPlatformGesture_OverridesFallback()
    {
        var def = new ContextCommandDefinition(
            "Cmd",
            keyGestures:
            [
                new ContextCommandKeyGesture("A"),
                new ContextCommandKeyGesture("B", OSPlatform.Windows),
            ]);

        string?[] windows = def.KeyGestures!.Where(g => g.Platform == OSPlatform.Windows).Select(g => g.KeyGesture).ToArray();
        string?[] osx = def.KeyGestures!.Where(g => g.Platform == OSPlatform.OSX).Select(g => g.KeyGesture).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(windows, Is.EquivalentTo(new[] { "B" }));
            Assert.That(osx, Is.EquivalentTo(new[] { "A" }));
        });
    }
}

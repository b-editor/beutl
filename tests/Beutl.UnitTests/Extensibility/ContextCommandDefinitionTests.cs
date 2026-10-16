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
            "test.exitTool",
            keyGestures: [new ContextCommandKeyGesture("V"), new ContextCommandKeyGesture("Escape")]);

        Assert.That(def.KeyGestures, Is.Not.Null);
        foreach (OSPlatform platform in s_platforms)
        {
            string?[] keys = def.KeyGestures!.Where(g => g.Platform == platform).Select(g => g.KeyGesture).ToArray();
            Assert.That(keys, Is.EquivalentTo(new[] { "V", "Escape" }), $"platform {platform}");
        }
    }

    [Test]
    public void Normalize_ExplicitPlatformGesture_OverridesFallback()
    {
        var def = new ContextCommandDefinition(
            "test.cmd",
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

    [Test]
    public async Task A_command_its_extension_handles_runs_only_without_a_context()
    {
        int runs = 0;
        bool enabled = false;
        var definition = new ContextCommandDefinition("test.run")
        {
            CanExecute = _ => enabled,
            ExecuteAsync = _ =>
            {
                runs++;
                return Task.CompletedTask;
            }
        };
        var execution = new ContextCommandExecution("test.run");

        bool disabled = definition.CanRun(null, execution);
        enabled = true;
        Assert.Multiple(() =>
        {
            Assert.That(definition.HasHandler, Is.True);
            Assert.That(disabled, Is.False);
            Assert.That(definition.CanRun(null, execution), Is.True);
            Assert.That(definition.CanRun(new object(), execution), Is.False);
            Assert.That(new ContextCommandDefinition("test.none").HasHandler, Is.False);
        });
        await definition.RunAsync(new object(), execution);
        await definition.RunAsync(null, execution);
        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public async Task A_typed_command_runs_on_a_context_of_its_type()
    {
        var definition = new ContextCommandDefinition<List<string>>("test.add")
        {
            CanExecuteOn = (list, _) => list.Count == 0,
            ExecuteOnAsync = (list, execution) =>
            {
                list.Add(execution.CommandId);
                return Task.CompletedTask;
            }
        };
        var execution = new ContextCommandExecution("test.add");
        var list = new List<string>();

        Assert.Multiple(() =>
        {
            Assert.That(definition.HasHandler, Is.True);
            Assert.That(definition.CanRun(list, execution), Is.True);
            Assert.That(definition.CanRun(new object(), execution), Is.False);
            Assert.That(definition.CanRun(null, execution), Is.False);
        });
        await definition.RunAsync(new object(), execution);
        await definition.RunAsync(list, execution);
        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { "test.add" }));
            Assert.That(definition.CanRun(list, execution), Is.False);
        });
    }
}

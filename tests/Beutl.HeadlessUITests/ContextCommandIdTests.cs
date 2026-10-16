using Avalonia.Headless.NUnit;
using Beutl.Api.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ContextCommandIdTests
{
    [AvaloniaTest]
    public void Every_command_has_its_own_id_and_its_key_from_before_ids_moves_to_it()
    {
        ContextCommandEntry[] entries = [.. TestShell.MainViewModel.ContextCommandManager.GetDefinitions()
            .Where(entry => entry.ExtensionType.Assembly != typeof(ContextCommandIdTests).Assembly)];

        Assert.Multiple(() =>
        {
            Assert.That(entries, Is.Not.Empty);
            Assert.That(entries.Select(entry => entry.Definition.Id), Is.Unique);
            Assert.That(
                entries.Select(entry => ContextCommandLegacyKeys.Ids.GetValueOrDefault($"{entry.ExtensionType.FullName}.{entry.Definition.Name}")),
                Is.EqualTo(entries.Select(entry => entry.Definition.Id)));
            Assert.That(ContextCommandLegacyKeys.Ids, Has.Count.EqualTo(entries.Length));
        });
    }
}

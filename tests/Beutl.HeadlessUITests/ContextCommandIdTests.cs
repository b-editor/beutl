using Avalonia.Headless.NUnit;
using Beutl.Api.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ContextCommandIdTests
{
    [AvaloniaTest]
    public void Ids_are_unique_and_every_key_from_before_ids_moves_to_one_of_them()
    {
        string[] ids = [.. TestShell.MainViewModel.ContextCommandManager.GetDefinitions()
            .Select(entry => entry.Definition.Id)];

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Not.Empty);
            Assert.That(ids, Is.All.Not.Empty);
            Assert.That(ids, Is.Unique);
            Assert.That(ContextCommandLegacyKeys.Ids.Values, Is.SubsetOf(ids));
            Assert.That(ContextCommandLegacyKeys.Removed.Intersect(ids), Is.Empty, "Dropping a live id loses its gestures.");
        });
    }
}

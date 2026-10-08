using Beutl.Editor.Services;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Editor.Services;

public class ObjectSearcherTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void A_large_script_is_a_leaf_and_does_not_hide_other_references(bool findAll)
    {
        var script = new SKSLScriptEffect { Script = { CurrentValue = new string('x', 1024 * 1024) } };
        var target = new object();
        int characterVisits = 0;
        var searcher = new ObjectSearcher(new object[] { script, target }, value =>
        {
            if (value is char) characterVisits++;
            return ReferenceEquals(value, target);
        });
        long before = GC.GetAllocatedBytesForCurrentThread();
        object? result = findAll ? searcher.SearchAll().Single() : searcher.Search();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(result, Is.SameAs(target));
        Assert.That(characterVisits, Is.Zero);
        Assert.That(allocated, Is.LessThan(512 * 1024), "Searching must not box and retain the script's characters.");
    }
}

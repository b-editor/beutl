using Beutl.Services.Tutorials;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TutorialServiceHandlerTests
{
    [Test]
    public void GetAvailableTutorials_returns_a_copy_that_later_changes_leave_unchanged()
    {
        var service = new TutorialServiceHandler();
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

    private static TutorialDefinition Tutorial(string id) => new() { Id = id, Title = id, Description = id, Steps = [] };
}

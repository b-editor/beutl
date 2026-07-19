using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Schema;

public sealed class SafeFeedbackTests
{
    [Test]
    public void Out_of_range_values_are_reported_before_apply()
    {
        var text = new TextBlock();
        ValidationOutcome outcome = ValidationEvaluator.Evaluate(text.Size, -10f, options: null);

        Assert.That(outcome.Status, Is.AnyOf(ValidationStatus.Coerced, ValidationStatus.Rejected));
    }

    [Test]
    public void Missing_media_returns_typed_error_without_corrupting_project()
    {
        using var session = new AgentToolkitTestSession(new Scene());
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new ElementTools(manager);

        var result = tools.AddElement(0, 1, 0, "image", mediaPath: Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing.png"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.MediaNotFound));
            Assert.That(((Scene)session.Root).Children, Is.Empty);
        });
    }

    [Test]
    public void Unknown_content_type_returns_typed_error()
    {
        var ex = Assert.Throws<ReconcileException>(() => ContentFactory.Create(new ContentRequest("not-installed")));

        Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.UnknownType));
    }
}

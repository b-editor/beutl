using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

[TestFixture]
[NonParallelizable]
public class TemplateSavingTests
{
    private ObjectTemplateService _templates = null!;

    [SetUp]
    public void SetUpTemplates() => _templates = new ObjectTemplateService(TestFonts.Manager,
        Path.Combine(Path.GetTempPath(), $"beutl-templates-{Guid.NewGuid():N}"));

    [TearDown]
    public void DisposeTemplates()
    {
        _templates.Dispose();
        Directory.Delete(_templates.DirectoryPath, recursive: true);
    }

    [Test]
    public async Task A_saved_template_calls_back_without_an_error()
    {
        using var notifications = new NotificationCapture();
        int saved = 0;

        bool result = await TemplateSaving.SaveAsync(notifications, _templates, null,
            new Audio.Effects.AudioEffectGroup(), $"saving-{Guid.NewGuid():N}", () => saved++);

        Assert.That(result, Is.True);
        Assert.That(saved, Is.EqualTo(1));
        Assert.That(notifications.All, Is.Empty);
    }

    [Test]
    public async Task A_refused_name_shows_the_error_and_does_not_call_back()
    {
        using var notifications = new NotificationCapture();
        int saved = 0;

        bool result = await TemplateSaving.SaveAsync(notifications, _templates, null, new Audio.Effects.AudioEffectGroup(), "a/b", () => saved++);

        Assert.That(result, Is.False);
        Assert.That(saved, Is.Zero);
        Assert.That(notifications.HasError(Strings.SaveAsTemplate, MessageStrings.OperationFailed), Is.True);
    }
}

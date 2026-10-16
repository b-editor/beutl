using Avalonia.Headless.NUnit;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public class DocumentWriteCheckTests
{
    [AvaloniaTest]
    public async Task Writes_to_the_open_project_off_the_UI_thread_are_reported()
    {
        await TestReset.ResetShellAsync();
        Project project = await TestShell.CreateProjectAsync("document-write-check");
        var shape = new RectShape();
        var element = new Element();
        element.AddObject(shape);
        project.Items.OfType<Scene>().Single().Children.Add(element);
        var reports = new List<string>();
        Action<string>? recorder = DocumentWriteCheck.Report;
        DocumentWriteCheck.Report = message =>
        {
            lock (reports) reports.Add(message);
        };
        try
        {
            await Task.Run(() =>
            {
                element.Name = "off";
                element.IsEnabled = false;
                shape.Opacity.CurrentValue = 50;
                element.Objects.Add(new RectShape());
                new Element().Name = "detached";
            });
            element.Name = "on";
        }
        finally
        {
            DocumentWriteCheck.Report = recorder;
        }

        Assert.Multiple(() =>
        {
            Assert.That(reports, Has.Exactly(1).StartsWith("Element.Name "));
            Assert.That(reports, Has.Exactly(1).StartsWith("Element.IsEnabled "));
            Assert.That(reports, Has.Exactly(1).StartsWith("RectShape.Opacity "));
            Assert.That(reports, Has.Exactly(1).StartsWith("Element.Items "));
        });
    }
}

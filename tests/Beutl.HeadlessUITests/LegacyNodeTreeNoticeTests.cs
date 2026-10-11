using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// A Beutl 1.x element drawn by its node tree keeps its file in 2.x, but saving converts the rest of
// the project. Opening such a project has to say so once, before anything is saved.
[TestFixture]
public sealed class LegacyNodeTreeNoticeTests
{
    private const string ElementName = "Node tree clip";

    [AvaloniaTest]
    public async Task Opening_a_1x_project_shows_one_notice_and_closing_keeps_the_element_file()
    {
        await TestReset.ResetShellAsync();
        (string projectPath, string elementPath) = WriteLegacyProject();
        byte[] original = File.ReadAllBytes(elementPath);
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        var handler = new CaptureNotificationHandler();
        NotificationService.Handler = handler;
        try
        {
            await TestShell.Project.OpenProject(projectPath);
            HeadlessTestHelpers.Settle();
            Notification[] notices = handler.Notifications
                .Where(notification => notification.Title == MessageStrings.UnmigratedNodeTreeTitle)
                .ToArray();

            Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
            Assert.That(notices, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(notices[0].Type, Is.EqualTo(NotificationType.Warning));
                Assert.That(notices[0].Expiration, Is.EqualTo(Timeout.InfiniteTimeSpan));
                Assert.That(notices[0].Message, Does.Contain(ElementName));
                Assert.That(notices[0].CancellationToken.IsCancellationRequested, Is.False);
            });

            SaveNotice(notices[0], "legacy-node-tree-notice-dark.png", ThemeVariant.Dark);
            SaveNotice(notices[0], "legacy-node-tree-notice-light.png", ThemeVariant.Light);

            // Closing saves every open scene.
            await TestReset.ResetShellAsync();
            Assert.Multiple(() =>
            {
                Assert.That(notices[0].CancellationToken.IsCancellationRequested, Is.True,
                    "Closing the project must dismiss its notice.");
                Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(original));
            });
        }
        finally
        {
            NotificationService.Handler = previousHandler;
            await TestReset.ResetShellAsync();
        }
    }

    private static void SaveNotice(Notification notice, string fileName, ThemeVariant theme)
    {
        using ThemeVariantScope scope = ThemeVariantScope.Use(theme);
        var infoBar = new NotificationServiceHandler(() => null)
            .BuildInfoBar(notice, new TaskCompletionSource(), () => { });
        var window = new Window { Content = infoBar, Width = 380, Height = 420 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            string output = Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            frame!.Save(output, PngBitmapEncoderOptions.Default);
        }
        finally
        {
            window.Close();
        }
    }

    // A project, a scene and an element as Beutl 1.1.0 saved them, with the element's UseNode toggle
    // on and a node tree, and the empty operator list 1.x always wrote.
    private static (string ProjectPath, string ElementPath) WriteLegacyProject()
    {
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"legacy-node-tree-{Guid.NewGuid():N}");
        string sceneDirectory = Path.Combine(root, "Scene1");
        Directory.CreateDirectory(sceneDirectory);
        string projectPath = Path.Combine(root, "Legacy.bep");
        string elementPath = Path.Combine(sceneDirectory, "dg5k1o0w.belm");
        File.WriteAllText(projectPath, """
            {
              "Id": "3f0d6c52-8a0e-4c1e-9b5e-6a51d7c0f2b4",
              "Name": "",
              "appVersion": "1.1.0",
              "minAppVersion": "1.0.0-preview.9",
              "items": [
                "Scene1/Scene1.scene"
              ],
              "variables": {
                "framerate": "30",
                "samplerate": "44100"
              }
            }
            """);
        File.WriteAllText(Path.Combine(sceneDirectory, "Scene1.scene"), """
            {
              "Id": "a7f3e1c9-2b4d-4f6a-8c0e-1d3b5f7a9c2e",
              "Name": "Scene1",
              "FrameSize": "1920, 1080",
              "Start": "00:00:00",
              "Duration": "00:00:10",
              "Width": 1920,
              "Height": 1080,
              "Elements": {
                "Include": "dg5k1o0w.belm"
              }
            }
            """);
        File.WriteAllText(elementPath, $$"""
            {
              "Id": "2b0c6f8e-7c1d-4b5e-9a39-0d1c55b0a7e1",
              "Name": "{{ElementName}}",
              "Start": "00:00:00",
              "Length": "00:00:05",
              "ZIndex": 0,
              "AccentColor": "#ff2196f3",
              "IsEnabled": true,
              "UseNode": true,
              "Operation": {
                "Id": "5f0e3b8a-1d2c-4e6f-8a9b-0c1d2e3f4a5b",
                "Name": "",
                "Children": []
              },
              "NodeTree": {
                "Id": "9d8c7b6a-5e4f-4a3b-9c2d-1e0f9a8b7c6d",
                "Name": "",
                "Nodes": [
                  {
                    "Id": "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d",
                    "Name": "",
                    "Position": "120,80",
                    "Items": [
                      {
                        "Id": "c1a2b3c4-d5e6-4f70-8192-a3b4c5d6e7f8",
                        "Name": "Geometry",
                        "LocalId": 0,
                        "connection-inputs": [],
                        "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:OutputSocket<[Beutl.Engine]Beutl.Media:RectGeometry>"
                      }
                    ],
                    "$type": "[Beutl.ProjectSystem]Beutl.NodeTree.Nodes.Geometry:RectGeometryNode"
                  }
                ],
                "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:ElementNodeTreeModel"
              }
            }
            """);
        return (projectPath, elementPath);
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        public List<Notification> Notifications { get; } = [];

        public void Show(Notification notification) => Notifications.Add(notification);
    }
}

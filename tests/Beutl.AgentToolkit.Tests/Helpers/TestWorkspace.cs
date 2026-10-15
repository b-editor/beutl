using Beutl.AgentToolkit.Sessions;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Helpers;

internal static class TestWorkspace
{
    public static string CreateDirectory()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    // A 1920x1080 scene whose file sits in a new directory.
    public static Scene CreateScene(string name = "Scene")
    {
        return new Scene(1920, 1080, name)
        {
            Uri = new Uri(Path.Combine(CreateDirectory(), $"{name}.scene"))
        };
    }

    // Adds an element whose file sits next to the scene file.
    public static Element AddElement(Scene scene, TimeSpan start, TimeSpan length, string? fileName = null)
    {
        var element = new Element
        {
            Start = start,
            Length = length,
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, fileName ?? "element.belm"))
        };
        scene.Children.Add(element);
        return element;
    }

    public static void CreateSymbolicLinkOrIgnore(string link, string target, bool directory)
    {
        try
        {
            if (directory)
            {
                Directory.CreateSymbolicLink(link, target);
            }
            else
            {
                File.CreateSymbolicLink(link, target);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not creatable in this environment: {ex.Message}");
        }
    }

    // Creates a project session and makes it current, without the first save the gateway does.
    public static FileEditingSession CreateFileSession(FileSessionSource source, ProjectCreateOptions options)
    {
        FileEditingSession session = source.CreateProjectSession(options);
        source.MakeCurrent(session);
        return session;
    }

    public static KeyFrameAnimation<float> CreateLinearFloatAnimation(float firstValue, float secondValue)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.AddSorted(
            new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = firstValue, Easing = new LinearEasing() });
        animation.KeyFrames.AddSorted(
            new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = secondValue, Easing = new LinearEasing() });
        return animation;
    }
}

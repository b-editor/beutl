using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.ProjectSystem;

public sealed class SceneFile
{
    public SceneFile(string fileName)
    {
        FileName = fileName;

        JsonNode json = JsonNode.Parse(File.ReadAllText(fileName))!;

        Width = json["width"]?.AsValue()?.GetValue<int>() ?? 0;
        Height = json["height"]?.AsValue()?.GetValue<int>() ?? 0;

        if (json["duration"]?.AsValue()?.GetValue<string>() is { } durationStr
            && TimeSpan.TryParse(durationStr, out var duration))
        {
            Duration = duration;
        }

        if (json["currentFrame"]?.AsValue()?.GetValue<string>() is { } currentStr
            && TimeSpan.TryParse(currentStr, out var current))
        {
            CurrentFrame = current;
        }
    }

    public SceneFile(int width, int height, TimeSpan duration, TimeSpan currentFrame, string fileName)
    {
        Width = width;
        Height = height;
        Duration = duration;
        CurrentFrame = currentFrame;
        FileName = fileName;
    }

    public int Width { get; }

    public int Height { get; }

    public TimeSpan Duration { get; }

    public TimeSpan CurrentFrame { get; }

    public string FileName { get; }
}

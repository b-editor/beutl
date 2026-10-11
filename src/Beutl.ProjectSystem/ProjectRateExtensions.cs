namespace Beutl;

public static class ProjectRateExtensions
{
    public static int GetFrameRate(this Project? project)
        => GetPositiveRate(project, ProjectVariableKeys.FrameRate, 30);

    public static int GetSampleRate(this Project? project)
        => GetPositiveRate(project, ProjectVariableKeys.SampleRate, 44100);

    private static int GetPositiveRate(Project? project, string key, int fallback)
        => project?.Variables.TryGetValue(key, out string? value) == true
            && int.TryParse(value, out int rate) && rate > 0
            ? rate
            : fallback;
}

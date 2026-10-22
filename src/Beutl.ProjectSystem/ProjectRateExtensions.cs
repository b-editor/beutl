namespace Beutl;

public static class ProjectRateExtensions
{
    // A whole number such as "30" or a fraction such as "30000/1001"; anything that is not a positive rate reads as 30.
    public static Rational GetFrameRate(this Project? project) => Project.ReadFrameRate(project);

    public static int GetSampleRate(this Project? project)
        => GetPositiveRate(project, ProjectVariableKeys.SampleRate, 44100);

    private static int GetPositiveRate(Project? project, string key, int fallback)
        => project?.Variables.TryGetValue(key, out string? value) == true
            && int.TryParse(value, out int rate) && rate > 0
            ? rate
            : fallback;
}

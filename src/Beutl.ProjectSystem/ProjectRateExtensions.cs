using System.Globalization;

namespace Beutl;

public static class ProjectRateExtensions
{
    // A whole number such as "30" or a fraction such as "30000/1001"; anything that is not a positive rate reads as 30.
    public static Rational GetFrameRate(this Project? project)
        => project?.Variables.TryGetValue(ProjectVariableKeys.FrameRate, out string? value) == true
            && Rational.TryParse(value, CultureInfo.InvariantCulture, out Rational rate)
            && rate.Numerator > 0 && rate.Denominator > 0
            ? rate
            : new Rational(30);

    public static int GetSampleRate(this Project? project)
        => GetPositiveRate(project, ProjectVariableKeys.SampleRate, 44100);

    private static int GetPositiveRate(Project? project, string key, int fallback)
        => project?.Variables.TryGetValue(key, out string? value) == true
            && int.TryParse(value, out int rate) && rate > 0
            ? rate
            : fallback;
}

using Beutl;
using Beutl.Collections;
using Beutl.Media;

namespace PackageSample;

public sealed class WellKnownSizesProvider : IChoicesProvider
{
    private static readonly CoreList<WellKnownSize> s_choices =
    [
        new("WQHD", new(2560, 1440)),
        new("Full HD", new(1920, 1080)),
        new("HD", new(1280, 720))
    ];

    public static void AddChoice(string name, PixelSize size)
    {
        s_choices.Add(new(name, size));
    }

    public static ICoreReadOnlyList<WellKnownSize> GetTypedChoices()
    {
        return s_choices;
    }

    public static void RemoveChoice(WellKnownSize item)
    {
        s_choices.Remove(item);
    }

    public static IReadOnlyList<object> GetChoices()
    {
        return s_choices;
    }
}

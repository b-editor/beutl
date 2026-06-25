namespace Beutl.Services.Tutorials;

public static class TutorialService
{
    private static ITutorialService? s_current;

    public static ITutorialService Current
    {
        get => s_current!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            s_current = value;
        }
    }
}

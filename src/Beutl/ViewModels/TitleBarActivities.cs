namespace Beutl.ViewModels;

// The kinds of background work the title bar's status button can show at once.
[Flags]
public enum TitleBarActivities
{
    None = 0,
    Startup = 1,
    Output = 2,
    AiJob = 4,
    Update = 8,
}

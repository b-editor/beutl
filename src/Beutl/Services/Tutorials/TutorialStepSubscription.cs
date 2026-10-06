namespace Beutl.Services.Tutorials;

// What a tutorial step listens to while it is shown; dismissing the step releases it.
internal sealed class TutorialStepSubscription
{
    public IDisposable? Current { get; set; }

    public void Release()
    {
        Current?.Dispose();
        Current = null;
    }
}
